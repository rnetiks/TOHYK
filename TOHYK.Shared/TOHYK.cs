using BepInEx;
using BepInEx.Configuration;
using Studio;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using KKAPI;
using KKAPI.Utilities;
using UnityEngine;

namespace TOHYK
{
    [BepInProcess("CharaStudio")]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInPlugin(Guid, PluginName, Version)]
    public class TOHYK : BaseUnityPlugin
    {
        // ReSharper disable MemberCanBePrivate.Global
        public const string Guid = "org.fox.TOHYK";
        public const string PluginName = "TOHYK";
        public const string Version = "1.2.2";
        // ReSharper restore MemberCanBePrivate.Global

        private InputHandler _inputHandler;
        private Transformer transformer;
        private MeshRaycaster _meshRaycaster;
        private UndoManager _undoManager;
        private GuideRenderer _guideRenderer;
        private UIDisplay _uiDisplay;

        public static ManualLogSource Log;

        private TransformMode _mode = TransformMode.None;
        private AxisConstraint _constraint = AxisConstraint.Free;
        private ConstraintSpace _space = ConstraintSpace.Global;
        private bool _snapping;

        private readonly Dictionary<int, GuideObject> _targets = new Dictionary<int, GuideObject>();
        private readonly Dictionary<int, Vector3> _initPos = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _initRot = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _initScale = new Dictionary<int, Vector3>();

        private Vector3 _pivotWorld;
        private GuideObject _activeTarget;

        private Vector2 _startMouseScreen;
        private Vector3 _startMousePlane;
        private Vector3 _startMouseAxis;
        private float _startDist;
        private Vector3 _rotationAxis;

        private GameObject _guideObjectWorkplace;

        internal static ConfigEntry<PivotMode> CfgPivotMode;
        private ConfigEntry<bool> _cfgSurfaceSnap;
        private ConfigEntry<bool> _cfgSurfaceAlignNormal;
        private ConfigEntry<float> _cfgSnapAngle;
        internal static ConfigEntry<bool> InvertYZ;

        private void Awake()
        {
            Log = Logger;
            _inputHandler = new InputHandler(Config);

            CfgPivotMode = Config.Bind("Pivot", "Pivot Mode", PivotMode.MedianPoint, "Transform pivot point.");
            _cfgSurfaceSnap = Config.Bind("Snapping", "Surface Snap", false,
                "When enabled during Move, raycast from camera and snap to mesh surfaces and colliders.");
            _cfgSurfaceAlignNormal = Config.Bind("Snapping", "Align To Surface Normal", false,
                "When surface-snapping, align the object's up direction to the hit normal.");
            InvertYZ = Config.Bind("General", "Switch Y/Z", false, "Switch Y and Z buttons");

            var cfgCursorSize = Config.Bind("Cursor", "Cursor Size", 35f,
                "On-screen size (in pixels) of the Move/Rotate/Scale cursor icons.");
            var cfgCursorAntialiasing = Config.Bind("Cursor", "Cursor Antialiasing", true,
                "Smooth (bilinear-filtered) cursor icons when enabled, hard/pixelated (point-filtered) when disabled.");

            CursorService.IconDisplaySize = cfgCursorSize.Value;
            CursorService.SetAntialiasing(cfgCursorAntialiasing.Value);

            cfgCursorSize.SettingChanged += (sender, args) =>
                CursorService.IconDisplaySize = cfgCursorSize.Value;
            cfgCursorAntialiasing.SettingChanged += (sender, args) =>
                CursorService.SetAntialiasing(cfgCursorAntialiasing.Value);

            var snapDistance = Config.Bind("Snapping", "Snap Distance", 0.1f, "Grid snap increment for position.");
            var snapAngle = Config.Bind("Snapping", "Snap Angle", 5f, "Grid snap increment for rotation in degrees.");
            var snapScale = Config.Bind("Snapping", "Snap Scale", 0.1f, "Grid snap increment for scale.");
            var rotateSensitivity =
                Config.Bind("Sensitivity", "Rotate Sensitivity", 1f, "Multiplier for rotation speed.");
            var surfaceSnapRadius =
                Config.Bind("Snapping", "Surface Snap Max Distance", 100f, "Maximum raycast distance.");

            _cfgSnapAngle = snapAngle;
            _meshRaycaster = new MeshRaycaster();
            _undoManager = new UndoManager();

            transformer = new Transformer(snapDistance,
                snapAngle,
                snapScale,
                rotateSensitivity,
                GetCamera,
                GetCameraForward);

            _uiDisplay = new UIDisplay();

            var shader = Shader.Find("Hidden/Internal-Colored");
            Material glMat = null;
            if (shader != null)
            {
                glMat = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                glMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                glMat.SetInt("_ZWrite", 0);
                glMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }

            _guideRenderer = new GuideRenderer(glMat);

            Camera.onPostRender += OnCameraPostRender;
        }

        private void OnDestroy()
        {
            Camera.onPostRender -= OnCameraPostRender;
            SetGuideObjectWorkplaceActive(true);
        }

        private void Update()
        {
            if (!Singleton<GuideObjectManager>.IsInstance())
                return;

            // Advance the wrap-around ("infinite drag") virtual mouse position
            // before anything this frame reads it. No-op when no transform
            // mode is active.
            MouseWrapService.Tick();

            if (_inputHandler.IsPivotCyclePressed)
            {
                CyclePivotMode();
                if (_mode != TransformMode.None)
                    RefreshMouseReferences();
            }

            if (_inputHandler.IsSnapTogglePressed)
            {
                _cfgSurfaceSnap.Value = !_cfgSurfaceSnap.Value;
            }

            if (_mode != TransformMode.None)
            {
                UpdateActiveMode();
            }
            else
            {
                CheckModeEntry();
            }
        }

        private void CyclePivotMode()
        {
            switch (CfgPivotMode.Value)
            {
                case PivotMode.MedianPoint:
                    CfgPivotMode.Value = PivotMode.ActiveElement;
                    break;
                case PivotMode.ActiveElement:
                    CfgPivotMode.Value = PivotMode.IndividualOrigins;
                    break;
                case PivotMode.IndividualOrigins:
                    CfgPivotMode.Value = PivotMode.MedianPoint;
                    break;
            }
        }

        private void SetGuideObjectWorkplaceActive(bool active)
        {
            if (_guideObjectWorkplace == null)
                _guideObjectWorkplace = GameObject.Find("StudioScene/GuideObjectWorkplace");

            if (_guideObjectWorkplace != null)
                _guideObjectWorkplace.SetActive(active);
        }

        private static Vector3 GetPivot(IDictionary<int, GuideObject> targets, GuideObject activeTarget, PivotMode mode)
        {
            if (targets == null || targets.Count == 0)
                return Vector3.zero;

            switch (mode)
            {
                case PivotMode.ActiveElement:
                    if (activeTarget != null)
                        return activeTarget.transformTarget.position;
                    goto case PivotMode.MedianPoint;

                case PivotMode.IndividualOrigins:
                    goto case PivotMode.MedianPoint;

                case PivotMode.MedianPoint:
                default:
                    Vector3 sum = Vector3.zero;
                    foreach (var go in targets.Values)
                        sum += go.transformTarget.position;
                    return sum / targets.Count;
            }
        }


        private void CheckModeEntry()
        {
            var selected = Singleton<GuideObjectManager>.Instance.selectObjects;
            if (selected == null || selected.Length == 0)
                return;

            if (_inputHandler.HasKeyboardFocus)
                return;

            var newMode = _inputHandler.GetTransformModeInput();
            if (newMode == null)
                return;

            _targets.Clear();
            _initPos.Clear();
            _initRot.Clear();
            _initScale.Clear();

            foreach (var go in selected)
            {
                bool valid;
                switch (newMode)
                {
                    case TransformMode.Move:
                        valid = go.enablePos;
                        break;
                    case TransformMode.Rotate:
                        valid = go.enableRot;
                        break;
                    case TransformMode.Scale:
                        valid = go.enableScale;
                        break;
                    default:
                        valid = false;
                        break;
                }

                if (!valid)
                    continue;

                _targets[go.dicKey] = go;
                _initPos[go.dicKey] = go.changeAmount.pos;
                _initRot[go.dicKey] = go.changeAmount.rot;
                _initScale[go.dicKey] = go.changeAmount.scale;
            }

            if (_targets.Count == 0)
                return;

            _activeTarget = GetActiveTarget();
            _pivotWorld = GetPivot(_targets, _activeTarget, CfgPivotMode.Value);

            MouseWrapService.BeginTracking();
            _startMouseScreen = MouseWrapService.VirtualMousePosition;
            _startMousePlane = GetMouseWorldOnPlane(_pivotWorld, GetCameraForward());
            _startDist = 0f;

            if (newMode == TransformMode.Rotate)
            {
                var pivotScreen = GetCamera().WorldToScreenPoint(_pivotWorld);
                _ = Mathf.Atan2(_startMouseScreen.y - pivotScreen.y, _startMouseScreen.x - pivotScreen.x);
            }

            if (newMode == TransformMode.Scale)
            {
                var pivotScreen = GetCamera().WorldToScreenPoint(_pivotWorld);
                _startDist = Vector2.Distance(_startMouseScreen, new Vector2(pivotScreen.x, pivotScreen.y));
                if (_startDist < 1f)
                    _startDist = 1f;
            }

            _mode = newMode.Value;
            CursorService.SetForMode(_mode);
            _constraint = newMode == TransformMode.Rotate ? AxisConstraint.CameraForward : AxisConstraint.Free;
            _space = ConstraintSpace.Global;
            _snapping = false;
            _rotationAxis = Vector3.zero;

            if (_mode == TransformMode.Move && _cfgSurfaceSnap.Value)
            {
                _meshRaycaster.BuildExcludeSet(_targets);
                _meshRaycaster.BuildCache(_targets);
            }

            SetGuideObjectWorkplaceActive(false);
        }

        private void Confirm()
        {
            _constraint = AxisConstraint.Free;

            switch (_mode)
            {
                case TransformMode.Move:
                    _undoManager.PushMoveUndo(_targets, _initPos);
                    break;
                case TransformMode.Rotate:
                    _undoManager.PushRotateUndo(_targets, _initPos, _initRot);
                    break;
                case TransformMode.Scale:
                    _undoManager.PushScaleUndo(_targets, _initPos, _initScale);
                    break;
            }

            _mode = TransformMode.None;
            CursorService.Reset();
            _rotationAxis = Vector3.zero;
            _meshRaycaster.Clear();
            MouseWrapService.EndTracking();
            SetGuideObjectWorkplaceActive(true);
        }

        private void Cancel()
        {
            _constraint = AxisConstraint.Free;
            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                switch (_mode)
                {
                    case TransformMode.Move:
                        go.changeAmount.pos = _initPos[kvp.Key];
                        go.transformTarget.localPosition = _initPos[kvp.Key];
                        break;
                    case TransformMode.Rotate:
                        go.changeAmount.rot = _initRot[kvp.Key];
                        go.transformTarget.localEulerAngles = _initRot[kvp.Key];
                        if (go.enablePos && go.changeAmount.pos != _initPos[kvp.Key])
                        {
                            go.changeAmount.pos = _initPos[kvp.Key];
                            go.transformTarget.localPosition = _initPos[kvp.Key];
                        }

                        break;
                    case TransformMode.Scale:
                        go.changeAmount.scale = _initScale[kvp.Key];
                        go.transformTarget.localScale = _initScale[kvp.Key];
                        go.changeAmount.pos = _initPos[kvp.Key];
                        go.transformTarget.localPosition = _initPos[kvp.Key];
                        break;
                }
            }

            _mode = TransformMode.None;
            CursorService.Reset();
            _rotationAxis = Vector3.zero;
            _meshRaycaster.Clear();
            MouseWrapService.EndTracking();
            SetGuideObjectWorkplaceActive(true);
        }


        private void UpdateActiveMode()
        {
            if (_mode == TransformMode.Rotate && _inputHandler.GetTransformModeInput() == TransformMode.Rotate)
            {
                _constraint = _constraint == AxisConstraint.CameraForward
                    ? AxisConstraint.Free
                    : AxisConstraint.CameraForward;

                _space = ConstraintSpace.Global;
                _rotationAxis = Vector3.zero;
                RefreshMouseReferences();
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Cancel();
                return;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
            {
                Confirm();
                return;
            }

            HandleConstraintInput();

            _snapping = _inputHandler.IsSnapping;

            if (_targets.Count == 0)
            {
                Cancel();
                return;
            }

            switch (_mode)
            {
                case TransformMode.Move:
                    UpdateMove();
                    break;
                case TransformMode.Rotate:
                    UpdateRotate();
                    break;
                case TransformMode.Scale:
                    UpdateScale();
                    break;
            }
        }

        private void HandleConstraintInput()
        {
            var axisInput = _inputHandler.GetAxisInput();
            if (axisInput == AxisInput.None)
                return;

            AxisConstraint targetAxis = axisInput.Constraint;

            if (axisInput.IsShiftHeld)
            {
                targetAxis = GetPlaneFromAxis(axisInput.Constraint);
            }

            if (_constraint == targetAxis && _space == ConstraintSpace.Global)
            {
                _space = ConstraintSpace.Local;
            }
            else if (_constraint == targetAxis && _space == ConstraintSpace.Local)
            {
                _constraint = AxisConstraint.Free;
                _space = ConstraintSpace.Global;
            }
            else
            {
                _constraint = targetAxis;
                _space = ConstraintSpace.Global;
            }

            _rotationAxis = Vector3.zero;
            RefreshMouseReferences();
        }

        private AxisConstraint GetPlaneFromAxis(AxisConstraint axis)
        {
            switch (axis)
            {
                case AxisConstraint.AxisX:
                    return AxisConstraint.PlaneYZ;
                case AxisConstraint.AxisY:
                    return AxisConstraint.PlaneXZ;
                case AxisConstraint.AxisZ:
                    return AxisConstraint.PlaneXY;
                default:
                    return AxisConstraint.Free;
            }
        }
        
        private void RefreshMouseReferences()
        {
            _startMouseScreen = MouseWrapService.VirtualMousePosition;
            _startMousePlane = GetMouseWorldOnPlane(_pivotWorld, GetPlaneNormal());

            if (_rotationAxis == Vector3.zero && _space == ConstraintSpace.Local && _constraint != AxisConstraint.Free)
                _rotationAxis = GetConstraintAxisDir();

            _startMouseAxis = GetMouseOnAxis(_pivotWorld,
                _rotationAxis != Vector3.zero ? _rotationAxis : GetConstraintAxisDir());

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                switch (_mode)
                {
                    case TransformMode.Move:
                        go.changeAmount.pos = _initPos[kvp.Key];
                        go.transformTarget.localPosition = _initPos[kvp.Key];
                        break;
                    case TransformMode.Rotate:
                        /*go.changeAmount.pos = _initPos[kvp.Key];
                        go.transformTarget.localPosition = _initPos[kvp.Key];*/
                        go.changeAmount.rot = _initRot[kvp.Key];
                        go.transformTarget.localEulerAngles = _initRot[kvp.Key];
                        break;
                    case TransformMode.Scale:
                        go.changeAmount.scale = _initScale[kvp.Key];
                        go.transformTarget.localScale = _initScale[kvp.Key];
                        break;
                }
            }

            _pivotWorld = GetPivot(_targets, _activeTarget, CfgPivotMode.Value);

            if (_mode == TransformMode.Rotate)
            {
                var pivotScreen = GetCamera().WorldToScreenPoint(_pivotWorld);
                _ = Mathf.Atan2(_startMouseScreen.y - pivotScreen.y, _startMouseScreen.x - pivotScreen.x);
            }

            if (_mode == TransformMode.Scale)
            {
                var pivotScreen = GetCamera().WorldToScreenPoint(_pivotWorld);
                _startDist = Vector2.Distance(_startMouseScreen, new Vector2(pivotScreen.x, pivotScreen.y));
                if (_startDist < 1f)
                    _startDist = 1f;
            }
        }

        private void UpdateMove()
        {
            if (_cfgSurfaceSnap.Value)
            {
                UpdateMoveSurfaceSnap();
                return;
            }

            Vector3 delta = transformer.ComputeMoveDelta(
                _startMousePlane,
                _startMouseAxis,
                _constraint,
                _space,
                _activeTarget,
                _pivotWorld);

            delta = transformer.ApplyMoveSnapping(delta, _constraint, _snapping);

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enablePos)
                    continue;

                go.transformTarget.position = InitialWorldPos(kvp.Key) + delta;
                go.changeAmount.pos = go.transformTarget.localPosition;
            }
        }

        private void UpdateMoveSurfaceSnap()
        {
            var cam = GetCamera();
            if (cam == null)
                return;

            Ray ray = cam.ScreenPointToRay(MouseWrapService.VirtualMousePosition);

            if (_meshRaycaster.Raycast(ray, 100f, _targets, out Vector3 hitPoint, out Vector3 hitNormal))
            {
                Vector3 delta = hitPoint - _pivotWorld;
                delta = transformer.ApplyConstraintMask(delta, _constraint, _space, _activeTarget);

                foreach (var kvp in _targets)
                {
                    var go = kvp.Value;
                    if (!go.enablePos)
                        continue;

                    go.transformTarget.position = InitialWorldPos(kvp.Key) + delta;
                    go.changeAmount.pos = go.transformTarget.localPosition;

                    if (_cfgSurfaceAlignNormal.Value && go.enableRot)
                    {
                        Quaternion initWorldRot = InitialWorldRot(kvp.Key);
                        Quaternion alignRot = Quaternion.FromToRotation(Vector3.up, hitNormal) *
                                              Quaternion.Euler(0f, initWorldRot.eulerAngles.y, 0f);
                        go.transformTarget.rotation = alignRot;
                        go.changeAmount.rot = go.transformTarget.localEulerAngles;
                    }
                }
            }
        }

        private void UpdateRotate()
        {
            if (_constraint == AxisConstraint.Free)
                UpdateRotateFree();
            else
                UpdateRotateConstrained();
        }

        private void UpdateRotateFree()
        {
            transformer.ComputeFreeRotateAngles(_startMouseScreen, out float angleX, out float angleY,
                _snapping);

            Camera cam = GetCamera();
            Vector3 camRight = cam.transform.right;
            Vector3 camUp = cam.transform.up;

            Quaternion rotation = Quaternion.AngleAxis(angleY, camUp) * Quaternion.AngleAxis(angleX, camRight);

            bool isIndividual = CfgPivotMode.Value == PivotMode.IndividualOrigins;

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enableRot)
                    continue;

                Quaternion initWorldRot = InitialWorldRot(kvp.Key);
                go.transformTarget.rotation = rotation * initWorldRot;
                go.changeAmount.rot = go.transformTarget.localEulerAngles;

                if (go.enablePos && !isIndividual)
                {
                    Vector3 initWorld = InitialWorldPos(kvp.Key);
                    go.transformTarget.position = rotation * (initWorld - _pivotWorld) + _pivotWorld;
                    go.changeAmount.pos = go.transformTarget.localPosition;
                }
            }
        }

        private void UpdateRotateConstrained()
        {
            var cam = GetCamera();
            Vector3 pivotScreen = cam.WorldToScreenPoint(_pivotWorld);
            var virtualMousePosition = MouseWrapService.VirtualMousePosition;
            Vector2 currentMouse = virtualMousePosition;
            
            float currentAngleRad = Mathf.Atan2(virtualMousePosition.y - pivotScreen.y, virtualMousePosition.x - pivotScreen.x);
            float deltaAngleRad = currentAngleRad - Mathf.Atan2(_startMouseScreen.y - pivotScreen.y, _startMouseScreen.x - pivotScreen.x);
            float deltaAngle = deltaAngleRad * Mathf.Rad2Deg;
            Vector3 axis = _rotationAxis != Vector3.zero ? _rotationAxis : GetConstraintAxisDir();
            if (_constraint == AxisConstraint.PlaneXY || _constraint == AxisConstraint.PlaneXZ ||
                _constraint == AxisConstraint.PlaneYZ)
                axis = _rotationAxis != Vector3.zero ? _rotationAxis : GetPlaneNormal();

            float sign = Vector3.Dot(axis, cam.transform.forward) > 0 ? 1f : -1f;
            deltaAngle *= sign;

            if (_snapping)
            {
                float snap = _cfgSnapAngle.Value;
                deltaAngle = Mathf.Round(deltaAngle / snap) * snap;
            }

            Quaternion rotation = Quaternion.AngleAxis(deltaAngle, axis);

            bool isIndividual = CfgPivotMode.Value == PivotMode.IndividualOrigins;

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enableRot) continue;

                Quaternion initWorldRot = InitialWorldRot(kvp.Key);
                go.transformTarget.rotation = rotation * initWorldRot;
                go.changeAmount.rot = go.transformTarget.localEulerAngles;

                if (go.enablePos && !isIndividual)
                {
                    Vector3 initWorld = InitialWorldPos(kvp.Key);
                    go.transformTarget.position = rotation * (initWorld - _pivotWorld) + _pivotWorld;
                    go.changeAmount.pos = go.transformTarget.localPosition;
                }
            }
        }

        private void UpdateScale()
        {
            float ratio = transformer.ComputeScaleRatio(_pivotWorld, _startDist, _snapping);

            Vector3 scaleFactor = ComputeScaleFactor(ratio);

            bool isIndividual = CfgPivotMode.Value == PivotMode.IndividualOrigins;

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enableScale)
                    continue;

                Vector3 initScale = _initScale[kvp.Key];
                go.transformTarget.localScale = Vector3.Scale(initScale, scaleFactor);
                go.changeAmount.scale = go.transformTarget.localScale;

                if (go.enablePos && !isIndividual)
                {
                    Vector3 initWorld = InitialWorldPos(kvp.Key);
                    Vector3 offset = initWorld - _pivotWorld;
                    go.transformTarget.position = _pivotWorld + Vector3.Scale(offset, scaleFactor);
                    go.changeAmount.pos = go.transformTarget.localPosition;
                }
            }
        }

        private Vector3 ComputeScaleFactor(float ratio)
        {
            if (_space == ConstraintSpace.Local && _activeTarget != null)
            {
                switch (_constraint)
                {
                    case AxisConstraint.Free:
                        return Vector3.one * ratio;
                    case AxisConstraint.AxisX:
                        return new Vector3(ratio, 1f, 1f);
                    case AxisConstraint.AxisY:
                        return new Vector3(1f, ratio, 1f);
                    case AxisConstraint.AxisZ:
                        return new Vector3(1f, 1f, ratio);
                    case AxisConstraint.PlaneXY:
                        return new Vector3(ratio, ratio, 1f);
                    case AxisConstraint.PlaneXZ:
                        return new Vector3(ratio, 1f, ratio);
                    case AxisConstraint.PlaneYZ:
                        return new Vector3(1f, ratio, ratio);
                    default:
                        return Vector3.one;
                }
            }

            Quaternion rot = _activeTarget != null ? _activeTarget.transformTarget.rotation : Quaternion.identity;

            switch (_constraint)
            {
                case AxisConstraint.Free:
                    return Vector3.one * ratio;
                case AxisConstraint.AxisX:
                {
                    Vector3 worldX = rot * Vector3.right;
                    float xFactor = Vector3.Dot(worldX, Vector3.right);
                    return new Vector3(1f + (ratio - 1f) * xFactor, 1f, 1f);
                }
                case AxisConstraint.AxisY:
                {
                    Vector3 worldY = rot * Vector3.up;
                    float yFactor = Vector3.Dot(worldY, Vector3.up);
                    return new Vector3(1f, 1f + (ratio - 1f) * yFactor, 1f);
                }
                case AxisConstraint.AxisZ:
                {
                    Vector3 worldZ = rot * Vector3.forward;
                    float zFactor = Vector3.Dot(worldZ, Vector3.forward);
                    return new Vector3(1f, 1f, 1f + (ratio - 1f) * zFactor);
                }
                case AxisConstraint.PlaneXY:
                {
                    Vector3 worldX = rot * Vector3.right;
                    Vector3 worldY = rot * Vector3.up;
                    float xFactor = Vector3.Dot(worldX, Vector3.right);
                    float yFactor = Vector3.Dot(worldY, Vector3.up);
                    return new Vector3(1f + (ratio - 1f) * xFactor, 1f + (ratio - 1f) * yFactor, 1f);
                }
                case AxisConstraint.PlaneXZ:
                {
                    Vector3 worldX = rot * Vector3.right;
                    Vector3 worldZ = rot * Vector3.forward;
                    float xFactor = Vector3.Dot(worldX, Vector3.right);
                    float zFactor = Vector3.Dot(worldZ, Vector3.forward);
                    return new Vector3(1f + (ratio - 1f) * xFactor, 1f, 1f + (ratio - 1f) * zFactor);
                }
                case AxisConstraint.PlaneYZ:
                {
                    Vector3 worldY = rot * Vector3.up;
                    Vector3 worldZ = rot * Vector3.forward;
                    float yFactor = Vector3.Dot(worldY, Vector3.up);
                    float zFactor = Vector3.Dot(worldZ, Vector3.forward);
                    return new Vector3(1f, 1f + (ratio - 1f) * yFactor, 1f + (ratio - 1f) * zFactor);
                }
                default:
                    return Vector3.one;
            }
        }

        private Camera GetCamera()
        {
            if (Singleton<Studio.Studio>.IsInstance())
                return Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera != null
                    ? Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera
                    : Camera.main;
            return Camera.main;
        }

        private Vector3 GetCameraForward()
        {
            var cam = GetCamera();
            return cam != null ? cam.transform.forward : Vector3.forward;
        }

        private GuideObject GetActiveTarget()
        {
            var mgr = Singleton<GuideObjectManager>.Instance;
            if (mgr.operationTarget != null)
                return mgr.operationTarget;
            if (mgr.selectObject != null)
                return mgr.selectObject;
            return _targets.Values.FirstOrDefault();
        }

        private Vector3 InitialWorldPos(int dicKey)
        {
            var go = _targets[dicKey];
            var parent = go.transformTarget.parent;
            if (parent != null)
                return parent.TransformPoint(_initPos[dicKey]);
            return _initPos[dicKey];
        }

        private Quaternion InitialWorldRot(int dicKey)
        {
            var go = _targets[dicKey];
            var parent = go.transformTarget.parent;
            Quaternion localRot = Quaternion.Euler(_initRot[dicKey]);
            if (parent != null)
                return parent.rotation * localRot;
            return localRot;
        }

        private Vector3 GetMouseWorldOnPlane(Vector3 planePoint, Vector3 planeNormal)
        {
            var cam = GetCamera();
            if (cam == null)
                return planePoint;

            Ray ray = cam.ScreenPointToRay(MouseWrapService.VirtualMousePosition);
            Plane plane = new Plane(planeNormal.normalized, planePoint);

            if (plane.Raycast(ray, out float enter))
                return ray.GetPoint(enter);

            return ray.GetPoint(100f);
        }

        private Vector3 GetMouseOnAxis(Vector3 axisOrigin, Vector3 axisDir)
        {
            var cam = GetCamera();
            if (cam == null)
                return axisOrigin;

            Ray mouseRay = cam.ScreenPointToRay(MouseWrapService.VirtualMousePosition);
            return ClosestPointOnLineToRay(axisOrigin, axisDir.normalized, mouseRay);
        }

        private Vector3 GetConstraintAxisDir()
        {
            return transformer.GetConstraintAxisDir(_constraint, _space, _activeTarget);
        }

        private Vector3 GetPlaneNormal()
        {
            return transformer.GetPlaneNormal(_constraint, _space, _activeTarget);
        }

        private static Vector3 ClosestPointOnLineToRay(Vector3 lineOrigin, Vector3 lineDir, Ray ray)
        {
            Vector3 w = lineOrigin - ray.origin;
            float a = Vector3.Dot(lineDir, lineDir);
            float b = Vector3.Dot(lineDir, ray.direction);
            float c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(lineDir, w);
            float e = Vector3.Dot(ray.direction, w);

            float denominator = a * c - b * b;
            if (Mathf.Abs(denominator) < 1e-8f)
                return lineOrigin;

            float t = (b * e - c * d) / denominator;
            return lineOrigin + lineDir * t;
        }

        private void OnCameraPostRender(Camera cam)
        {
            if (_mode == TransformMode.None)
                return;
            if (cam != GetCamera())
                return;

            Vector3? cachedAxis = _rotationAxis != Vector3.zero ? (Vector3?)_rotationAxis : null;
            Vector3? cachedNormal = null;
            if (_constraint == AxisConstraint.PlaneXY || _constraint == AxisConstraint.PlaneXZ ||
                _constraint == AxisConstraint.PlaneYZ)
                cachedNormal = cachedAxis;

            _guideRenderer.Render(_targets, _pivotWorld, _constraint, _space, _activeTarget, CfgPivotMode.Value,
                cachedAxis, cachedNormal);

            // Dashed pivot-to-cursor guide, Blender-style: uses the unbounded
            // virtual mouse position, so it keeps travelling straight past the
            // screen edge instead of snapping back when the cursor wraps.
            // Skipped in Move mode (G) - doesn't make sense there since the
            // pivot itself moves with the object.
            if (_mode != TransformMode.Move)
            {
                _guideRenderer.RenderCursorGuideLine(cam, _pivotWorld, MouseWrapService.VirtualMousePosition,
                    new Color(1f, 1f, 1f, 0.6f));
            }
        }

        private void OnGUI()
        {
            if (_mode == TransformMode.None)
                return;

            _uiDisplay.Render(_mode, _constraint, _space, _snapping, _cfgSurfaceSnap.Value);

            DrawCustomCursor();
        }

        /// <summary>
        /// Draws our own cursor icon (the OS cursor is hidden while a mode
        /// is active - see CursorService.SetForMode). Anchored at the real,
        /// screen-clamped mouse position (Input.mousePosition) so it always
        /// sits where the pointer visually is, even if the wrap-around
        /// "infinite drag" logic has the virtual position far outside the
        /// screen bounds. The rotation angle, however, is computed from the
        /// pivot to the unbounded virtual mouse position - the exact same
        /// angle the dashed guide line is drawn at - so the icon always
        /// points the same direction as that line, like the short hand of
        /// a clock locked to the long hand.
        /// </summary>
        private void DrawCustomCursor()
        {
            Vector2 mouseRaw = (Vector2)Input.mousePosition;
            Vector2 mouseGui = new Vector2(mouseRaw.x, Screen.height - mouseRaw.y);

            float angleDeg = 0f;

            if (_mode == TransformMode.Rotate || _mode == TransformMode.Scale)
            {
                Camera cam = GetCamera();
                if (cam != null)
                {
                    Vector3 pivotScreen3 = cam.WorldToScreenPoint(_pivotWorld);
                    if (pivotScreen3.z >= 0f)
                    {
                        Vector2 pivotGui = new Vector2(pivotScreen3.x, Screen.height - pivotScreen3.y);
                        Vector2 mouseVirtualGui = new Vector2(
                            MouseWrapService.VirtualMousePosition.x,
                            Screen.height - MouseWrapService.VirtualMousePosition.y);

                        Vector2 dir = mouseVirtualGui - pivotGui;
                        if (dir.sqrMagnitude > 0.0001f)
                            angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    }
                }
            }

            CursorService.Draw(_mode, mouseGui, angleDeg);
        }
    }
}
