using BepInEx;
using BepInEx.Configuration;
using Studio;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using KKAPI;
using KKAPI.Utilities;
using UnityEngine;

namespace TOHYK
{
#if KKS
    [BepInProcess("CharaStudio")]
    [BepInProcess("KoikatsuSunshine")]
#else
    [BepInProcess("CharaStudio")]
#endif
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInPlugin(Guid, PluginName, Version)]
    public class TOHYK : BaseUnityPlugin
    {
        public const string Guid = "org.fox.TOHYK";
        public const string PluginName = "TOHYK";
        public const string Version = "1.3.0";
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

        private readonly Dictionary<int, ITransformTarget> _targets = new Dictionary<int, ITransformTarget>();
        private readonly Dictionary<int, Vector3> _initPos = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _initRot = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _initScale = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _initPivotOffset = new Dictionary<int, Vector3>();

        private Vector3 _pivotWorld;
        private ITransformTarget _activeTarget;

        private bool _isAccessoryMode;
        private bool _accessoryModeActive;

        private Vector2 _startMouseScreen;
        private Vector3 _startMousePlane;
        private Vector3 _startMouseAxis;
        private float _startDist;
        private Vector3 _rotationAxis;

        private Vector3 _cachedAxisDir = Vector3.forward;

        private Vector2 _rotatePrevMouseScreen;
        private float _rotateAccumAngle;

        private Vector3 _movePrevMousePlane;
        private Vector3 _movePrevMouseAxis;
        private Vector2 _movePrevMouseScreen;
        private Vector3 _moveAccumDelta;
        private float _scalePrevDist;
        private Vector2 _scalePrevMouseScreen;
        private float _scaleAccumRatio;

        private Vector3 _lastMoveDelta;
        private float _lastRotateAppliedAngle;

        private readonly StringBuilder _headerSb = new StringBuilder(96);

        private bool _precision;
        private bool _fineSnap;

        private bool _numericInputActive;
        private string _numericInputBuffer = "";

        private GameObject _guideObjectWorkplace;

        internal static ConfigEntry<PivotMode> CfgPivotMode;
        private ConfigEntry<bool> _cfgSurfaceSnap;
        private ConfigEntry<bool> _cfgSurfaceAlignNormal;
        private ConfigEntry<float> _cfgSnapAngle;
        internal static ConfigEntry<bool> InvertYZ;

        internal static ConfigEntry<float> CfgHudXOffset;
        internal static ConfigEntry<float> CfgHudBottomMargin;
        internal static ConfigEntry<float> CfgBadgeXOffset;
        internal static ConfigEntry<float> CfgBadgeBottomMargin;
        internal static ConfigEntry<int> CfgHudFontSize;
        internal static ConfigEntry<int> CfgBadgeFontSize;
        internal static ConfigEntry<float> CfgHudFadeDuration;

        private void Awake()
        {
            Log = Logger;
            _inputHandler = new InputHandler(Config);
            InputGuard.Initialize(new HarmonyLib.Harmony(Guid));

            CfgPivotMode = Config.Bind("Pivot", "Pivot Mode", PivotMode.MedianPoint, "Transform pivot point.");
            _cfgSurfaceSnap = Config.Bind("Snapping", "Surface Snap", false,
                "When enabled during Move, raycast from camera and snap to mesh surfaces and colliders.");
            _cfgSurfaceAlignNormal = Config.Bind("Snapping", "Align To Surface Normal", false,
                "When surface-snapping, align the object's up direction to the hit normal.");
            InvertYZ = Config.Bind("General", "Switch Y/Z", false, "Switch Y and Z buttons");

            CfgHudXOffset = Config.Bind("HUD", "Status Bar - Horizontal Position", 0f,
                new ConfigDescription(
                    "Moves the Move/Rotate/Scale status bar left/right, in pixels, starting from the center of the " +
                    "screen. Positive = right, negative = left.",
                    new AcceptableValueRange<float>(-800f, 800f)));
            CfgHudBottomMargin = Config.Bind("HUD", "Status Bar - Height From Bottom", 5f,
                new ConfigDescription(
                    "How far up from the bottom of the screen the Move/Rotate/Scale status bar sits, in pixels.",
                    new AcceptableValueRange<float>(0f, 400f)));
            CfgHudFontSize = Config.Bind("HUD", "Status Bar - Text Size", 13,
                new ConfigDescription(
                    "Text size of the Move/Rotate/Scale status bar. The box around it resizes to match.",
                    new AcceptableValueRange<int>(6, 40)));

            CfgBadgeXOffset = Config.Bind("HUD", "Enabled Badge - Horizontal Position", -394f,
                new ConfigDescription(
                    "Moves the \"TOHYK Enabled!\" badge left/right, in pixels, starting from the center of the " +
                    "screen. Positive = right, negative = left.",
                    new AcceptableValueRange<float>(-800f, 800f)));
            CfgBadgeBottomMargin = Config.Bind("HUD", "Enabled Badge - Height From Bottom", 80f,
                new ConfigDescription(
                    "How far up from the bottom of the screen the \"TOHYK Enabled!\" badge sits, in pixels. Set " +
                    "above the status bar's own height by default so the two don't overlap while dragging.",
                    new AcceptableValueRange<float>(0f, 400f)));
            CfgBadgeFontSize = Config.Bind("HUD", "Enabled Badge - Text Size", 12,
                new ConfigDescription(
                    "Text size of the \"TOHYK Enabled!\" badge. The box around it resizes to match.",
                    new AcceptableValueRange<int>(6, 40)));
            CfgHudFadeDuration = Config.Bind("HUD", "Fade Duration", 0.24f,
                new ConfigDescription(
                    "How long (seconds) both HUD pills take to fade in when they appear and fade out when they " +
                    "disappear. 0 makes them appear/disappear instantly.",
                    new AcceptableValueRange<float>(0f, 1f)));
#if KKS
            AccessoryModeService.CfgDebugAccMover = Config.Bind("Debug", "Log AccMover Integration", false,
                "Logs what TOHYK's AccMover bridge detects/reads each time you enter Move/Rotate/Scale on an " +
                "accessory - which detection strategy matched, how many slots AccMover reported selected, and " +
                "whether TOHYK ended up using the multi- or single-accessory path. Turn on if multi-select isn't " +
                "moving everything you expect, then check the BepInEx console.");
#endif

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
            InputGuard.Polling = true;
            try
            {
                UpdateInner();
            }
            finally
            {
                InputGuard.Polling = false;

                InputGuard.Active = _accessoryModeActive || _mode != TransformMode.None;
                InputGuard.NumericActive = _mode != TransformMode.None;
                InputGuard.BlockUiClicks = _mode != TransformMode.None;
            }
        }

        private void UpdateInner()
        {
            bool studioActive = Singleton<GuideObjectManager>.IsInstance();
#if KKS
            bool chaMakerAvailable = !studioActive && AccessoryModeService.IsChaMakerScene;
#else
            bool chaMakerAvailable = false;
#endif

            if (!studioActive && !chaMakerAvailable)
                return;

            MouseWrapService.Tick();

#if KKS
            if (chaMakerAvailable)
            {
                if (!_inputHandler.HasKeyboardFocus && _inputHandler.IsChaMakerTogglePressed)
                {
                    _accessoryModeActive = !_accessoryModeActive;
                    if (!_accessoryModeActive)
                    {
                        if (_mode != TransformMode.None)
                            Cancel();
                        _undoManager.ClearAccessoryUndo();
                    }
                }

                if (!_accessoryModeActive)
                    return;

                if (_mode == TransformMode.None && !AccessoryModeService.IsAccessoryPanelOpen)
                    return; 

                CheckCameraFocusAccessory();

                if (_mode == TransformMode.None && !_inputHandler.HasKeyboardFocus)
                {
                    if (_inputHandler.IsUndoPressed)
                    {
                        _undoManager.UndoAccessory();
                        return;
                    }

                    if (_inputHandler.IsRedoPressed)
                    {
                        _undoManager.RedoAccessory();
                        return;
                    }
                }
            }
#endif

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
            else if (studioActive)
            {
                CheckModeEntry();
            }
#if KKS
            else
            {
                CheckModeEntryAccessory();
            }
#endif
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
                    CfgPivotMode.Value = PivotMode.BoundingBoxCenter;
                    break;
                case PivotMode.BoundingBoxCenter:
                    CfgPivotMode.Value = PivotMode.AccessoryParent;
                    break;
                case PivotMode.AccessoryParent:
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

        private Vector3 GetLivePivot(IDictionary<int, ITransformTarget> targets, ITransformTarget activeTarget, PivotMode mode)
        {
            if (targets == null || targets.Count == 0)
                return Vector3.zero;

            switch (mode)
            {
                case PivotMode.ActiveElement:
                    if (activeTarget != null)
                        return InitialWorldPos(activeTarget.Key);
                    goto case PivotMode.MedianPoint;

                case PivotMode.IndividualOrigins:
                    goto case PivotMode.MedianPoint;

                case PivotMode.AccessoryParent:
                {
#if KKS
                    if (activeTarget is AccessoryTransformTarget accessoryTarget)
                    {
                        Transform bone = accessoryTarget.ParentBoneTransform;
                        if (bone != null)
                            return bone.position;
                    }
#endif
                    goto case PivotMode.MedianPoint;
                }

                case PivotMode.BoundingBoxCenter:
                {
                    Vector3? min = null, max = null;
                    foreach (var key in targets.Keys)
                    {
                        Vector3 c = InitialWorldPos(key);
                        min = min == null ? c : Vector3.Min(min.Value, c);
                        max = max == null ? c : Vector3.Max(max.Value, c);
                    }
                    return min == null ? Vector3.zero : (min.Value + max.Value) * 0.5f;
                }

                case PivotMode.MedianPoint:
                default:
                    Vector3 sum = Vector3.zero;
                    foreach (var key in targets.Keys)
                        sum += InitialWorldPos(key);
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

            var clearMode = _inputHandler.GetClearTransformInput();
            if (clearMode != null)
            {
                PerformClearTransformStudio(clearMode.Value, selected);
                return;
            }

            if (_inputHandler.IsMirrorPressed)
            {
                EnterMirrorMode(selected, null);
                return;
            }

            var newMode = _inputHandler.GetTransformModeInput();
            if (newMode == null)
                return;

            _targets.Clear();
            _initPos.Clear();
            _initRot.Clear();
            _initScale.Clear();
            _initPivotOffset.Clear();

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

                var wrapped = new StudioGuideTarget(go);
                _targets[wrapped.Key] = wrapped;
                _initPos[wrapped.Key] = wrapped.PosLocal;
                _initRot[wrapped.Key] = wrapped.RotLocal;
                _initScale[wrapped.Key] = wrapped.ScaleLocal;
                _initPivotOffset[wrapped.Key] = wrapped.PivotOffsetLocal;
            }

            if (_targets.Count == 0)
                return;

            _isAccessoryMode = false;
            _activeTarget = GetActiveTarget();
            BeginTransformOperation(newMode.Value);
            SetGuideObjectWorkplaceActive(false);
        }

        private void BeginTransformOperation(TransformMode newMode)
        {
            _pivotWorld = GetLivePivot(_targets, _activeTarget, CfgPivotMode.Value);

            MouseWrapService.BeginTracking();
            _startMouseScreen = MouseWrapService.VirtualMousePosition;
            _startMousePlane = GetMouseWorldOnPlane(_pivotWorld, GetCameraForward());
            _startDist = 0f;

            _rotatePrevMouseScreen = _startMouseScreen;
            _rotateAccumAngle = 0f;
            _moveAccumDelta = Vector3.zero;
            _scaleAccumRatio = 1f;

            if (newMode == TransformMode.Scale)
            {
                var pivotScreen = GetCamera().WorldToScreenPoint(_pivotWorld);
                _startDist = Vector2.Distance(_startMouseScreen, new Vector2(pivotScreen.x, pivotScreen.y));
                if (_startDist < 1f)
                    _startDist = 1f;
            }

            _mode = newMode;
            CursorService.SetForMode(_mode);
            _constraint = newMode == TransformMode.Rotate ? AxisConstraint.CameraForward : AxisConstraint.Free;
            _space = ConstraintSpace.Global;
            _snapping = false;
            _rotationAxis = Vector3.zero;
            _movePrevMousePlane = _startMousePlane;
            _movePrevMouseAxis = _startMouseAxis;
            _movePrevMouseScreen = _startMouseScreen;
            _scalePrevDist = _startDist;
            _scalePrevMouseScreen = _startMouseScreen;
            ClearNumericInput();

            if (!_isAccessoryMode && _mode == TransformMode.Move && _cfgSurfaceSnap.Value)
            {
                _meshRaycaster.BuildExcludeSet(_targets);
                _meshRaycaster.BuildCache(_targets);
            }
        }

        private void SwitchActiveTransformMode(TransformMode newMode)
        {
            ResetTargetsToInitialTransform();
            BeginTransformOperation(newMode);
        }

#if KKS
        private void CheckModeEntryAccessory()
        {
            if (_inputHandler.HasKeyboardFocus)
                return;

            var clearMode = _inputHandler.GetClearTransformInput();
            if (clearMode != null)
            {
                if (AccessoryModeService.TryGetActiveAccessoryTargets(out var clearTargets) && clearTargets.Count > 0)
                    PerformClearTransformAccessory(clearMode.Value, clearTargets);
                return;
            }

            if (_inputHandler.IsMirrorPressed)
            {
                if (AccessoryModeService.TryGetActiveAccessoryTargets(out var mirrorTargets) && mirrorTargets.Count > 0)
                    EnterMirrorMode(null, mirrorTargets.Cast<ITransformTarget>().ToList());
                return;
            }

            var newMode = _inputHandler.GetTransformModeInput();
            if (newMode == null)
                return;

            if (!AccessoryModeService.TryGetActiveAccessoryTargets(out var accessoryTargets) || accessoryTargets.Count == 0)
                return;

            _targets.Clear();
            _initPos.Clear();
            _initRot.Clear();
            _initScale.Clear();
            _initPivotOffset.Clear();

            foreach (var accessoryTarget in accessoryTargets)
            {
                _targets[accessoryTarget.Key] = accessoryTarget;
                _initPos[accessoryTarget.Key] = accessoryTarget.PosLocal;
                _initRot[accessoryTarget.Key] = accessoryTarget.RotLocal;
                _initScale[accessoryTarget.Key] = accessoryTarget.ScaleLocal;
                _initPivotOffset[accessoryTarget.Key] = accessoryTarget.PivotOffsetLocal;
            }

            AccessoryModeService.TryGetActiveAccessoryTarget(out var panelTarget);
            var activeTarget = accessoryTargets.Find(t => panelTarget != null && t.Key == panelTarget.Key)
                                ?? accessoryTargets[0];

            _isAccessoryMode = true;
            _activeTarget = activeTarget;
            BeginTransformOperation(newMode.Value);
        }

        private CameraControl_Ver2 _chaMakerCamCtrl;

        private CameraControl_Ver2 GetChaMakerCameraControl()
        {
            if (_chaMakerCamCtrl == null)
                _chaMakerCamCtrl = UnityEngine.Object.FindObjectOfType<CameraControl_Ver2>();
            return _chaMakerCamCtrl;
        }

        private void CheckCameraFocusAccessory()
        {
            if (_inputHandler.HasKeyboardFocus || _numericInputActive)
                return;

            if (!_inputHandler.IsFocusCameraPressed)
                return;

            List<Vector3> points = new List<Vector3>();

            if (_mode != TransformMode.None && _isAccessoryMode && _targets.Count > 0)
            {
                foreach (var target in _targets.Values)
                    points.Add(target.transformTarget.position);
            }
            else if (AccessoryModeService.TryGetActiveAccessoryTargets(out var freshTargets) && freshTargets.Count > 0)
            {
                foreach (var target in freshTargets)
                    points.Add(target.transformTarget.position);
            }

            if (points.Count == 0)
                return;

            Vector3 focusPoint = Vector3.zero;
            foreach (var p in points)
                focusPoint += p;
            focusPoint /= points.Count;

            var camCtrl = GetChaMakerCameraControl();
            if (camCtrl != null)
                camCtrl.TargetPos = focusPoint;
        }
#endif

        private void Confirm()
        {
            if (_numericInputActive && _mode == TransformMode.Scale)
            {
                ApplyScaleRatio(ParseNumericBuffer());
            }

            _constraint = AxisConstraint.Free;

            ClearNumericInput();

            if (_isAccessoryMode)
            {
                var changes = _targets.Values.Select(target =>
                    new UndoManager.AccessoryChange(target, _initPos[target.Key], _initRot[target.Key], _initScale[target.Key]));
                _undoManager.PushAccessoryUndo(changes);

                foreach (var target in _targets.Values)
                    target.Commit();
            }
            else
            {
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
            }

            _mode = TransformMode.None;
            CursorService.Reset();
            _rotationAxis = Vector3.zero;
            _meshRaycaster.Clear();
            MouseWrapService.EndTracking();
            if (!_isAccessoryMode)
                SetGuideObjectWorkplaceActive(true);
        }

        private void Cancel()
        {
            _constraint = AxisConstraint.Free;
            ClearNumericInput();
            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                switch (_mode)
                {
                    case TransformMode.Move:
                        go.PosLocal = _initPos[kvp.Key];
                        break;
                    case TransformMode.Rotate:
                        go.RotLocal = _initRot[kvp.Key];
                        if (go.enablePos && go.PosLocal != _initPos[kvp.Key])
                            go.PosLocal = _initPos[kvp.Key];
                        break;
                    case TransformMode.Scale:
                        go.ScaleLocal = _initScale[kvp.Key];
                        go.PosLocal = _initPos[kvp.Key];
                        break;
                }
            }

            _mode = TransformMode.None;
            CursorService.Reset();
            _rotationAxis = Vector3.zero;
            _meshRaycaster.Clear();
            MouseWrapService.EndTracking();
            if (!_isAccessoryMode)
                SetGuideObjectWorkplaceActive(true);
        }

        private void PerformClearTransformStudio(TransformMode mode, GuideObject[] selected)
        {
            if (selected == null || selected.Length == 0)
                return;

            _targets.Clear();
            _initPos.Clear();
            _initRot.Clear();
            _initScale.Clear();
            _initPivotOffset.Clear();

            foreach (var go in selected)
            {
                bool valid;
                switch (mode)
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

                var wrapped = new StudioGuideTarget(go);
                _targets[wrapped.Key] = wrapped;
                _initPos[wrapped.Key] = wrapped.PosLocal;
                _initRot[wrapped.Key] = wrapped.RotLocal;
                _initScale[wrapped.Key] = wrapped.ScaleLocal;
                _initPivotOffset[wrapped.Key] = wrapped.PivotOffsetLocal;
            }

            if (_targets.Count == 0)
                return;

            _isAccessoryMode = false;
            ApplyClearTransformAndConfirm(mode);
        }

#if KKS
        private void PerformClearTransformAccessory(TransformMode mode, List<AccessoryTransformTarget> accessoryTargets)
        {
            _targets.Clear();
            _initPos.Clear();
            _initRot.Clear();
            _initScale.Clear();
            _initPivotOffset.Clear();

            foreach (var accessoryTarget in accessoryTargets)
            {
                bool valid;
                switch (mode)
                {
                    case TransformMode.Move:
                        valid = accessoryTarget.enablePos;
                        break;
                    case TransformMode.Rotate:
                        valid = accessoryTarget.enableRot;
                        break;
                    case TransformMode.Scale:
                        valid = accessoryTarget.enableScale;
                        break;
                    default:
                        valid = false;
                        break;
                }

                if (!valid)
                    continue;

                _targets[accessoryTarget.Key] = accessoryTarget;
                _initPos[accessoryTarget.Key] = accessoryTarget.PosLocal;
                _initRot[accessoryTarget.Key] = accessoryTarget.RotLocal;
                _initScale[accessoryTarget.Key] = accessoryTarget.ScaleLocal;
                _initPivotOffset[accessoryTarget.Key] = accessoryTarget.PivotOffsetLocal;
            }

            if (_targets.Count == 0)
                return;

            _isAccessoryMode = true;
            ApplyClearTransformAndConfirm(mode);
        }
#endif

        private void ApplyClearTransformAndConfirm(TransformMode mode)
        {
            foreach (var kvp in _targets)
            {
                var target = kvp.Value;
                switch (mode)
                {
                    case TransformMode.Move:
                        if (target.enablePos)
                            target.PosLocal = Vector3.zero;
                        break;
                    case TransformMode.Rotate:
                        if (target.enableRot)
                            target.RotLocal = Vector3.zero;
                        break;
                    case TransformMode.Scale:
                        if (target.enableScale)
                            target.ScaleLocal = Vector3.one;
                        break;
                }

                if (mode != TransformMode.Move && target.enablePos)
                    target.PosLocal = _initPos[kvp.Key];
            }

            _mode = mode;
            Confirm();
        }



        private void EnterMirrorMode(GuideObject[] selectedStudio, List<ITransformTarget> accessoryTargets)
        {
            _targets.Clear();
            _initPos.Clear();
            _initRot.Clear();
            _initScale.Clear();
            _initPivotOffset.Clear();

            _isAccessoryMode = accessoryTargets != null;

            if (_isAccessoryMode)
            {
                foreach (var accessoryTarget in accessoryTargets)
                {
                    _targets[accessoryTarget.Key] = accessoryTarget;
                    _initPos[accessoryTarget.Key] = accessoryTarget.PosLocal;
                    _initRot[accessoryTarget.Key] = accessoryTarget.RotLocal;
                    _initScale[accessoryTarget.Key] = accessoryTarget.ScaleLocal;
                    _initPivotOffset[accessoryTarget.Key] = accessoryTarget.PivotOffsetLocal;
                }
            }
            else
            {
                if (selectedStudio == null)
                    return;

                foreach (var go in selectedStudio)
                {
                    var wrapped = new StudioGuideTarget(go);
                    _targets[wrapped.Key] = wrapped;
                    _initPos[wrapped.Key] = wrapped.PosLocal;
                    _initRot[wrapped.Key] = wrapped.RotLocal;
                    _initScale[wrapped.Key] = wrapped.ScaleLocal;
                    _initPivotOffset[wrapped.Key] = wrapped.PivotOffsetLocal;
                }
            }

            if (_targets.Count == 0)
                return;

#if KKS
            if (_isAccessoryMode)
            {
                AccessoryModeService.TryGetActiveAccessoryTarget(out var panelTarget);
                _activeTarget = accessoryTargets.Find(t => panelTarget != null && t.Key == panelTarget.Key)
                                 ?? accessoryTargets[0];
            }
            else
#endif
            {
                _activeTarget = GetActiveTarget();
            }

            _pivotWorld = GetLivePivot(_targets, _activeTarget, CfgPivotMode.Value);
            _mode = TransformMode.Mirror;
            _constraint = AxisConstraint.Free;
            _space = ConstraintSpace.Global;
            CursorService.SetForMode(_mode);
        }

        private void UpdateMirrorMode()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelMirror();
                return;
            }

            if (_targets.Count == 0)
            {
                CancelMirror();
                return;
            }

            _pivotWorld = GetLivePivot(_targets, _activeTarget, CfgPivotMode.Value);

            var axisInput = _inputHandler.GetAxisInput();
            if (axisInput == AxisInput.None)
                return;

            ConstraintSpace space = _isAccessoryMode
                ? ConstraintSpace.Local
                : (axisInput.IsShiftHeld ? ConstraintSpace.Local : ConstraintSpace.Global);
            Vector3 normal = transformer.GetConstraintAxisDir(axisInput.Constraint, space, _activeTarget);
            Vector3 pivot = _pivotWorld;

            string oldParentKey = null;
            string newParentKey = null;
            int? reparentedKey = null;
            Transform reparentOldBone = null;
            Transform reparentNewBone = null;

#if KKS
            if (axisInput.IsAltHeld && _isAccessoryMode && _activeTarget is AccessoryTransformTarget accTarget)
            {
                oldParentKey = accTarget.CurrentParentKey;
                string swapped = AccessoryTransformTarget.GetReverseParentKey(oldParentKey);

                if (swapped != null
                    && accTarget.TryResolveBone(oldParentKey, out Transform oldBone)
                    && accTarget.TryResolveBone(swapped, out Transform newBone)
                    && accTarget.SetParentKey(swapped))
                {
                    newParentKey = swapped;
                    reparentedKey = accTarget.Key;
                    reparentOldBone = oldBone;
                    reparentNewBone = newBone;
                }
                else
                {
                    oldParentKey = null;
                }
            }
#endif

            ApplyMirrorToTargets(pivot, normal, axisInput.Constraint, reparentedKey, reparentOldBone, reparentNewBone);
            FinishMirror(oldParentKey, newParentKey);
        }

        private static int MirrorAxisIndex(AxisConstraint constraint)
        {
            switch (constraint)
            {
                case AxisConstraint.AxisY: return 1;
                case AxisConstraint.AxisZ: return 2;
                default: return 0;
            }
        }



        private static void MirrorLocal(Vector3 pos, Vector3 rotEuler, int axisIndex,
            out Vector3 newPos, out Vector3 newRotEuler)
        {
            newPos = pos;
            newPos[axisIndex] = -newPos[axisIndex];

            Matrix4x4 m = Matrix4x4.Rotate(Quaternion.Euler(rotEuler));

            Matrix4x4 f = Matrix4x4.identity;
            f[axisIndex, axisIndex] = -1f;

            Matrix4x4 mirrored = f * m * f;

            Vector3 forward = mirrored.GetColumn(2);
            Vector3 up = mirrored.GetColumn(1);
            newRotEuler = Quaternion.LookRotation(forward, up).eulerAngles;
        }

        private void ApplyMirrorToTargets(Vector3 pivotWorld, Vector3 mirrorNormalWorld, AxisConstraint axisConstraint, int? reparentedKey = null,
            Transform reparentOldBone = null, Transform reparentNewBone = null)
        {
            foreach (var kvp in _targets)
            {
                var target = kvp.Value;
                if (!target.enablePos && !target.enableRot && !target.enableScale)
                    continue;

                if (reparentedKey.HasValue && kvp.Key == reparentedKey.Value)
                {
                    int swapAxisIndex = MirrorAxisIndex(axisConstraint);
                    MirrorLocal(_initPos[kvp.Key], _initRot[kvp.Key], swapAxisIndex,
                        out Vector3 swappedLocalPos, out Vector3 swappedLocalRot);

                    if (target.enablePos)
                        target.PosLocal = swappedLocalPos;
                    if (target.enableRot)
                        target.RotLocal = swappedLocalRot;
                    if (target.enableScale)
                        target.ScaleLocal = _initScale[kvp.Key];

                    continue;
                }

                if (_isAccessoryMode)
                {
                    int axisIndex = MirrorAxisIndex(axisConstraint);
                    MirrorLocal(_initPos[kvp.Key], _initRot[kvp.Key], axisIndex,
                        out Vector3 mirroredLocalPos, out Vector3 mirroredLocalRot);

                    if (target.enablePos)
                        target.PosLocal = mirroredLocalPos;
                    if (target.enableRot)
                        target.RotLocal = mirroredLocalRot;
                    if (target.enableScale)
                        target.ScaleLocal = _initScale[kvp.Key];

                    continue;
                }

                Vector3 worldPos = InitialWorldPos(kvp.Key);
                Quaternion worldRot = InitialWorldRot(kvp.Key);
                Vector3 worldScale = _initScale[kvp.Key];

                Transformer.MirrorAcrossPlane(worldPos, worldRot, worldScale, pivotWorld, mirrorNormalWorld,
                    out Vector3 newWorldPos, out Quaternion newWorldRot, out Vector3 newWorldScale);

                Transform parent = target.transformTarget.parent;

                if (target.enablePos)
                    target.PosLocal = parent != null ? parent.InverseTransformPoint(newWorldPos) : newWorldPos;

                if (target.enableRot)
                {
                    Quaternion localRot = parent != null
                        ? Quaternion.Inverse(parent.rotation) * newWorldRot
                        : newWorldRot;
                    target.RotLocal = localRot.eulerAngles;
                }

                if (target.enableScale)
                    target.ScaleLocal = newWorldScale;
            }
        }

        private void FinishMirror(string oldParentKey = null, string newParentKey = null)
        {
            if (_isAccessoryMode)
            {
                var changes = _targets.Values.Select(target =>
                    new UndoManager.AccessoryChange(target, _initPos[target.Key], _initRot[target.Key], _initScale[target.Key],
                        oldParentKey, newParentKey));
                _undoManager.PushAccessoryUndo(changes);

                foreach (var target in _targets.Values)
                    target.Commit();
            }
            else
            {
                _undoManager.PushMirrorUndo(_targets, _initPos, _initRot, _initScale);
            }

            _mode = TransformMode.None;
            CursorService.Reset();
            MouseWrapService.EndTracking();
            if (!_isAccessoryMode)
                SetGuideObjectWorkplaceActive(true);
        }

        private void CancelMirror()
        {
            _targets.Clear();
            _mode = TransformMode.None;
            CursorService.Reset();
            MouseWrapService.EndTracking();
            if (!_isAccessoryMode)
                SetGuideObjectWorkplaceActive(true);
        }

        private void UpdateActiveMode()
        {
            if (_mode == TransformMode.Mirror)
            {
                UpdateMirrorMode();
                return;
            }

            var transformKeyInput = _inputHandler.GetTransformModeInput();

            if (_mode == TransformMode.Rotate && transformKeyInput == TransformMode.Rotate)
            {
                _constraint = _constraint == AxisConstraint.CameraForward
                    ? AxisConstraint.Free
                    : AxisConstraint.CameraForward;

                _space = ConstraintSpace.Global;
                _rotationAxis = Vector3.zero;
                RefreshMouseReferences();
            }
            else if (transformKeyInput != null && transformKeyInput != _mode)
            {
                SwitchActiveTransformMode(transformKeyInput.Value);
                return;
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
            _precision = _inputHandler.IsPrecision;
            _fineSnap = _inputHandler.IsFineSnap;

            if (_targets.Count == 0)
            {
                Cancel();
                return;
            }

            if (_targets.Count > 0)
                _pivotWorld = GetLivePivot(_targets, _activeTarget, CfgPivotMode.Value);

            HandleNumericInput();

            if (_numericInputActive)
            {
                ApplyNumericTransform();
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

        private void ClearNumericInput()
        {
            _numericInputActive = false;
            _numericInputBuffer = "";
        }

        private void HandleNumericInput()
        {
            if (!NumericEntrySupported())
                return;

            string typed = Input.inputString;
            if (!string.IsNullOrEmpty(typed))
            {
                bool wasActive = _numericInputActive;

                foreach (char c in typed)
                {
                    if (c >= '0' && c <= '9')
                    {
                        _numericInputActive = true;
                        _numericInputBuffer += c;
                    }
                    else if (c == '-')
                    {
                        _numericInputActive = true;
                        _numericInputBuffer = _numericInputBuffer.StartsWith("-")
                            ? _numericInputBuffer.Substring(1)
                            : "-" + _numericInputBuffer;
                    }
                    else if ((c == '.' || c == ',') && !_numericInputBuffer.Contains("."))
                    {
                        _numericInputActive = true;
                        _numericInputBuffer += ".";
                    }
                }

                if (!wasActive && _numericInputActive)
                {
                    ResetTargetsToInitialTransform();
                    _pivotWorld = GetLivePivot(_targets, _activeTarget, CfgPivotMode.Value);
                }
            }

            if (_numericInputActive && _numericInputBuffer.Length > 0 && Input.GetKeyDown(KeyCode.Backspace))
            {
                _numericInputBuffer = _numericInputBuffer.Substring(0, _numericInputBuffer.Length - 1);
                if (_numericInputBuffer.Length == 0)
                {
                    ResyncMouseBaselinesToCurrentValue();
                    _numericInputActive = false;
                }
            }
        }

        private bool NumericEntrySupported()
        {
            switch (_mode)
            {
                case TransformMode.Rotate:
                case TransformMode.Scale:
                    return true;
                case TransformMode.Move:
                    return _constraint == AxisConstraint.AxisX ||
                           _constraint == AxisConstraint.AxisY ||
                           _constraint == AxisConstraint.AxisZ;
                default:
                    return false;
            }
        }

        private float ParseNumericBuffer()
        {
            if (string.IsNullOrEmpty(_numericInputBuffer) || _numericInputBuffer == "-" || _numericInputBuffer == ".")
                return 0f;

            float.TryParse(_numericInputBuffer, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result);
            return result;
        }

        private void ApplyNumericTransform()
        {
            float value = ParseNumericBuffer();

            switch (_mode)
            {
                case TransformMode.Move:
                    ApplyNumericMove(value);
                    break;
                case TransformMode.Rotate:
                    ApplyNumericRotate(value);
                    break;
                case TransformMode.Scale:
                    ApplyNumericScale(value);
                    break;
            }
        }

        private void ApplyNumericMove(float distance)
        {
            Vector3 axisDir = GetConstraintAxisDir();
            _moveAccumDelta = axisDir * distance;

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enablePos)
                    continue;

                go.transformTarget.position = InitialWorldPos(kvp.Key) + _moveAccumDelta;
                go.PosLocal = go.transformTarget.localPosition;
            }
        }

        private void ApplyNumericRotate(float degrees)
        {
            Vector3 axis = _rotationAxis != Vector3.zero ? _rotationAxis : GetConstraintAxisDir();
            if (_constraint == AxisConstraint.Free || _constraint == AxisConstraint.CameraForward)
                axis = GetCameraForward();

            _rotateAccumAngle = degrees;

            Quaternion rotation = Quaternion.AngleAxis(degrees, axis);
            bool isIndividual = CfgPivotMode.Value == PivotMode.IndividualOrigins;

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enableRot)
                    continue;

                Quaternion initWorldRot = InitialWorldRot(kvp.Key);
                go.transformTarget.rotation = rotation * initWorldRot;
                go.RotLocal = go.transformTarget.localEulerAngles;

                if (go.enablePos && !isIndividual)
                {
                    Vector3 initWorld = InitialWorldPos(kvp.Key);
                    go.transformTarget.position = rotation * (initWorld - _pivotWorld) + _pivotWorld;
                    go.PosLocal = go.transformTarget.localPosition;
                }
            }
        }

        private void ApplyNumericScale(float ratio)
        {
            if (ratio == 0f)
                return;

            ApplyScaleRatio(ratio);
        }

        private void ApplyTargetScale(ITransformTarget go, int key, float ratio, bool isIndividual)
        {
            Vector3 initScale = _initScale[key];
            Vector3 meshScaleFactor = ComputeMeshScaleFactor(ratio, go);
            Vector3 newScale = Vector3.Scale(initScale, meshScaleFactor);
            go.transformTarget.localScale = newScale;
            go.ScaleLocal = newScale;

            if (!go.enablePos)
                return;

            Vector3 desiredPositionWorld;
            if (isIndividual)
            {
                desiredPositionWorld = InitialWorldPos(key);
            }
            else
            {
                Vector3 offsetFromPivot = InitialWorldPos(key) - _pivotWorld;
                desiredPositionWorld = _pivotWorld + ScalePivotOffset(offsetFromPivot, ratio);
            }

            go.transformTarget.position = desiredPositionWorld;
            go.PosLocal = go.transformTarget.localPosition;
        }

        private void ApplyScaleRatio(float ratio)
        {
            _scaleAccumRatio = ratio;

            bool isIndividual = CfgPivotMode.Value == PivotMode.IndividualOrigins;

            foreach (var kvp in _targets)
            {
                if (!kvp.Value.enableScale)
                    continue;

                ApplyTargetScale(kvp.Value, kvp.Key, ratio, isIndividual);
            }
        }

        private void ResyncMouseBaselinesToCurrentValue()
        {
            switch (_mode)
            {
                case TransformMode.Move:
                    _movePrevMousePlane = GetMouseWorldOnPlane(_pivotWorld, GetPlaneNormal());
                    _movePrevMouseAxis = GetMouseOnAxis(_pivotWorld,
                        _rotationAxis != Vector3.zero ? _rotationAxis : GetConstraintAxisDir());
                    _movePrevMouseScreen = MouseWrapService.VirtualMousePosition;
                    break;

                case TransformMode.Rotate:
                    _rotatePrevMouseScreen = MouseWrapService.VirtualMousePosition;
                    break;

                case TransformMode.Scale:
                {
                    var cam = GetCamera();
                    if (cam == null)
                        break;

                    Vector3 pivotScreen3 = cam.WorldToScreenPoint(_pivotWorld);
                    _scalePrevDist = Vector2.Distance(MouseWrapService.VirtualMousePosition,
                        new Vector2(pivotScreen3.x, pivotScreen3.y));
                    if (_scalePrevDist < 1f)
                        _scalePrevDist = 1f;
                    _scalePrevMouseScreen = MouseWrapService.VirtualMousePosition;
                    break;
                }
            }
        }

        private string GetNumericDisplayText()
        {
            if (!_numericInputActive)
                return null;

            string value = _numericInputBuffer.Length == 0 || _numericInputBuffer == "-"
                ? "0"
                : _numericInputBuffer;
            string suffix = _mode == TransformMode.Rotate ? "\u00B0" : "";
            string axisLabel = GetConstraintAxisLabel();

            return string.IsNullOrEmpty(axisLabel) ? $"{value}{suffix}" : $"{axisLabel}: {value}{suffix}";
        }

        private string GetLiveTransformText()
        {
            string typed = GetNumericDisplayText();
            if (typed != null)
                return typed;

            switch (_mode)
            {
                case TransformMode.Move:
                    return GetMoveHeaderText();
                case TransformMode.Rotate:
                    return GetRotateHeaderText();
                case TransformMode.Scale:
                    return GetScaleHeaderText();
                default:
                    return null;
            }
        }

        private static void AppendFixed(StringBuilder sb, float value, int decimals, int minIntDigits = 1)
        {
            if (value < 0f)
            {
                sb.Append('-');
                value = -value;
            }

            int scale = 1;
            for (int i = 0; i < decimals; i++)
                scale *= 10;

            long scaled = (long)(value * scale + 0.5f);
            long intPart = scaled / scale;
            long fracPart = scaled % scale;

            int intDigits = 1;
            for (long t = intPart; t >= 10; t /= 10)
                intDigits++;
            for (int i = intDigits; i < minIntDigits; i++)
                sb.Append('0');

            sb.Append(intPart);

            if (decimals <= 0)
                return;

            sb.Append('.');

            long fracDivisor = scale / 10;
            while (fracDivisor > 0)
            {
                sb.Append(fracPart / fracDivisor % 10);
                fracDivisor /= 10;
            }
        }

        private string GetMoveHeaderText()
        {
            Vector3 d = _lastMoveDelta;
            var sb = _headerSb;
            sb.Length = 0;

            if (_constraint == AxisConstraint.AxisX || _constraint == AxisConstraint.AxisY ||
                _constraint == AxisConstraint.AxisZ)
            {
                Vector3 axisDir = _rotationAxis != Vector3.zero ? _rotationAxis : GetConstraintAxisDir();
                float signed = Vector3.Dot(d, axisDir.normalized);
                string spaceWord = _space == ConstraintSpace.Local ? "local" : "global";
                char axisLetter = _constraint == AxisConstraint.AxisX ? 'X' :
                    _constraint == AxisConstraint.AxisY ? 'Y' : 'Z';

                sb.Append("D: ");
                AppendFixed(sb, signed, 4, 2);
                sb.Append(" m along ").Append(spaceWord).Append(' ').Append(axisLetter);
                return sb.ToString();
            }

            sb.Append("Dx: ");
            AppendFixed(sb, d.x, 4, 2);
            sb.Append(" m  Dy: ");
            AppendFixed(sb, d.y, 4, 2);
            sb.Append(" m  Dz: ");
            AppendFixed(sb, d.z, 4, 2);
            sb.Append(" m  (");
            AppendFixed(sb, d.magnitude, 4, 2);
            sb.Append(" m)");
            return sb.ToString();
        }

        private string GetRotateHeaderText()
        {
            if (_constraint == AxisConstraint.Free)
                return null;

            string spaceWord = _space == ConstraintSpace.Local ? "local" : "global";
            string axisWord;
            switch (_constraint)
            {
                case AxisConstraint.AxisX:
                    axisWord = "X";
                    break;
                case AxisConstraint.AxisY:
                    axisWord = "Y";
                    break;
                case AxisConstraint.AxisZ:
                    axisWord = "Z";
                    break;
                case AxisConstraint.PlaneXY:
                    axisWord = "XY plane";
                    break;
                case AxisConstraint.PlaneXZ:
                    axisWord = "XZ plane";
                    break;
                case AxisConstraint.PlaneYZ:
                    axisWord = "YZ plane";
                    break;
                case AxisConstraint.CameraForward:
                    axisWord = "view axis";
                    break;
                default:
                    axisWord = "";
                    break;
            }

            var sb = _headerSb;
            sb.Length = 0;
            sb.Append("Rotation: ");
            AppendFixed(sb, _lastRotateAppliedAngle, 2, 2);
            sb.Append('\u00B0').Append(" along ").Append(spaceWord).Append(' ').Append(axisWord);
            return sb.ToString();
        }

        private string GetScaleHeaderText()
        {
            ITransformTarget reference = _activeTarget;
            if (reference == null)
            {
                foreach (var kvp in _targets)
                {
                    reference = kvp.Value;
                    break;
                }
            }

            if (reference == null)
                return null;

            float ratio = transformer.ApplyScaleSnapping(_scaleAccumRatio, _snapping, _fineSnap);

            Vector3 factor = ComputeMeshScaleFactor(ratio, reference);

            var sb = _headerSb;
            sb.Length = 0;
            sb.Append("Scale X: ");
            AppendFixed(sb, factor.x, 4, 2);
            sb.Append("  Y: ");
            AppendFixed(sb, factor.y, 4, 2);
            sb.Append("  Z: ");
            AppendFixed(sb, factor.z, 4, 2);
            return sb.ToString();
        }

        private string GetConstraintAxisLabel()
        {
            switch (_constraint)
            {
                case AxisConstraint.AxisX:
                    return _space == ConstraintSpace.Local ? "X (local)" : "X";
                case AxisConstraint.AxisY:
                    return _space == ConstraintSpace.Local ? "Y (local)" : "Y";
                case AxisConstraint.AxisZ:
                    return _space == ConstraintSpace.Local ? "Z (local)" : "Z";
                default:
                    return "";
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
            ClearNumericInput();
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
            _rotatePrevMouseScreen = _startMouseScreen;
            _rotateAccumAngle = 0f;
            _moveAccumDelta = Vector3.zero;
            _scaleAccumRatio = 1f;

            ResetTargetsToInitialTransform();

            _pivotWorld = GetLivePivot(_targets, _activeTarget, CfgPivotMode.Value);

            RefreshConstraintDirectionCache();

            if (_rotationAxis == Vector3.zero && _space == ConstraintSpace.Local && _constraint != AxisConstraint.Free)
                _rotationAxis = GetConstraintAxisDir();

            _startMousePlane = GetMouseWorldOnPlane(_pivotWorld, GetPlaneNormal());

            _startMouseAxis = GetMouseOnAxis(_pivotWorld,
                _rotationAxis != Vector3.zero ? _rotationAxis : GetConstraintAxisDir());

            _movePrevMousePlane = _startMousePlane;
            _movePrevMouseAxis = _startMouseAxis;
            _movePrevMouseScreen = _startMouseScreen;

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

            _scalePrevDist = _startDist;
            _scalePrevMouseScreen = _startMouseScreen;
        }

        private void ResetTargetsToInitialTransform()
        {
            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                switch (_mode)
                {
                    case TransformMode.Move:
                        go.PosLocal = _initPos[kvp.Key];
                        go.transformTarget.localPosition = _initPos[kvp.Key];
                        break;
                    case TransformMode.Rotate:
                        go.RotLocal = _initRot[kvp.Key];
                        go.transformTarget.localEulerAngles = _initRot[kvp.Key];
                        break;
                    case TransformMode.Scale:
                        go.ScaleLocal = _initScale[kvp.Key];
                        go.transformTarget.localScale = _initScale[kvp.Key];
                        break;
                }
            }
        }

        private void UpdateMove()
        {
            if (_cfgSurfaceSnap.Value)
            {
                UpdateMoveSurfaceSnap();
                return;
            }

            Vector3 frameDelta = transformer.ComputeMoveDelta(
                _movePrevMousePlane,
                _movePrevMouseAxis,
                _constraint,
                _space,
                _activeTarget,
                _cachedAxisDir,
                _pivotWorld,
                _movePrevMouseScreen,
                _precision);
            _moveAccumDelta += frameDelta;

            _movePrevMousePlane = GetMouseWorldOnPlane(_pivotWorld, GetPlaneNormal());
            _movePrevMouseAxis = GetMouseOnAxis(_pivotWorld,
                _rotationAxis != Vector3.zero ? _rotationAxis : GetConstraintAxisDir());
            _movePrevMouseScreen = MouseWrapService.VirtualMousePosition;

            Vector3 delta = transformer.ApplyMoveSnapping(_moveAccumDelta, _constraint, _snapping, _fineSnap);
            _lastMoveDelta = delta;

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enablePos)
                    continue;

                go.transformTarget.position = InitialWorldPos(kvp.Key) + delta;
                go.PosLocal = go.transformTarget.localPosition;
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
                _lastMoveDelta = delta;

                foreach (var kvp in _targets)
                {
                    var go = kvp.Value;
                    if (!go.enablePos)
                        continue;

                    go.transformTarget.position = InitialWorldPos(kvp.Key) + delta;
                    go.PosLocal = go.transformTarget.localPosition;

                    if (_cfgSurfaceAlignNormal.Value && go.enableRot)
                    {
                        Quaternion initWorldRot = InitialWorldRot(kvp.Key);
                        Quaternion alignRot = Quaternion.FromToRotation(Vector3.up, hitNormal) *
                                              Quaternion.Euler(0f, initWorldRot.eulerAngles.y, 0f);
                        go.transformTarget.rotation = alignRot;
                        go.RotLocal = go.transformTarget.localEulerAngles;
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
                _snapping, _precision, _fineSnap);

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
                go.RotLocal = go.transformTarget.localEulerAngles;

                if (go.enablePos && !isIndividual)
                {
                    Vector3 initWorld = InitialWorldPos(kvp.Key);
                    go.transformTarget.position = rotation * (initWorld - _pivotWorld) + _pivotWorld;
                    go.PosLocal = go.transformTarget.localPosition;
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

            float frameDelta = transformer.ComputeConstrainedRotateAngleDelta(
                _rotatePrevMouseScreen, _pivotWorld, axis, _precision);
            _rotatePrevMouseScreen = MouseWrapService.VirtualMousePosition;
            _rotateAccumAngle += frameDelta;

            float appliedAngle = _rotateAccumAngle;
            if (_snapping)
            {
                float snap = _cfgSnapAngle.Value;
                if (_fineSnap) snap /= Transformer.FineSnapDivisor;
                appliedAngle = Mathf.Round(appliedAngle / snap) * snap;
            }

            Quaternion rotation = Quaternion.AngleAxis(appliedAngle, axis);
            _lastRotateAppliedAngle = appliedAngle;

            bool isIndividual = CfgPivotMode.Value == PivotMode.IndividualOrigins;

            foreach (var kvp in _targets)
            {
                var go = kvp.Value;
                if (!go.enableRot) continue;

                Quaternion initWorldRot = InitialWorldRot(kvp.Key);
                go.transformTarget.rotation = rotation * initWorldRot;
                go.RotLocal = go.transformTarget.localEulerAngles;

                if (go.enablePos && !isIndividual)
                {
                    Vector3 initWorld = InitialWorldPos(kvp.Key);
                    go.transformTarget.position = rotation * (initWorld - _pivotWorld) + _pivotWorld;
                    go.PosLocal = go.transformTarget.localPosition;
                }
            }
        }

        private void UpdateScale()
        {
            var cam = GetCamera();
            Vector2 currentMouse = MouseWrapService.VirtualMousePosition;
            Vector3 pivotScreen3 = cam.WorldToScreenPoint(_pivotWorld);
            float currentDist = Vector2.Distance(currentMouse, new Vector2(pivotScreen3.x, pivotScreen3.y));

            float frameRatio = transformer.ComputeScaleFrameRatio(_pivotWorld, _scalePrevDist, _scalePrevMouseScreen, _precision);
            _scaleAccumRatio *= frameRatio;
            _scalePrevDist = currentDist;
            _scalePrevMouseScreen = currentMouse;

            float ratio = transformer.ApplyScaleSnapping(_scaleAccumRatio, _snapping, _fineSnap);

            bool isIndividual = CfgPivotMode.Value == PivotMode.IndividualOrigins;

            foreach (var kvp in _targets)
            {
                if (!kvp.Value.enableScale)
                    continue;

                ApplyTargetScale(kvp.Value, kvp.Key, ratio, isIndividual);
            }
        }

        private Vector3 GetAxisScaleMask(float ratio)
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

        private Vector3 ComputeMeshScaleFactor(float ratio, ITransformTarget target)
        {
            if (_space == ConstraintSpace.Local)
                return GetAxisScaleMask(ratio);

            if (_constraint == AxisConstraint.Free)
                return Vector3.one * ratio;

            Quaternion rot = target != null ? target.transformTarget.rotation : Quaternion.identity;

            Vector3 worldAxisA;
            Vector3? worldAxisB = null;
            switch (_constraint)
            {
                case AxisConstraint.AxisX:
                    worldAxisA = Vector3.right;
                    break;
                case AxisConstraint.AxisY:
                    worldAxisA = Vector3.up;
                    break;
                case AxisConstraint.AxisZ:
                    worldAxisA = Vector3.forward;
                    break;
                case AxisConstraint.PlaneXY:
                    worldAxisA = Vector3.right;
                    worldAxisB = Vector3.up;
                    break;
                case AxisConstraint.PlaneXZ:
                    worldAxisA = Vector3.right;
                    worldAxisB = Vector3.forward;
                    break;
                case AxisConstraint.PlaneYZ:
                    worldAxisA = Vector3.up;
                    worldAxisB = Vector3.forward;
                    break;
                default:
                    return Vector3.one;
            }

            Vector3 localRight = rot * Vector3.right;
            Vector3 localUp = rot * Vector3.up;
            Vector3 localForward = rot * Vector3.forward;

            float xWeight = AxisAlignmentWeight(localRight, worldAxisA, worldAxisB);
            float yWeight = AxisAlignmentWeight(localUp, worldAxisA, worldAxisB);
            float zWeight = AxisAlignmentWeight(localForward, worldAxisA, worldAxisB);

            return new Vector3(
                1f + (ratio - 1f) * xWeight,
                1f + (ratio - 1f) * yWeight,
                1f + (ratio - 1f) * zWeight);
        }

        private static float AxisAlignmentWeight(Vector3 localAxisWorld, Vector3 worldAxisA, Vector3? worldAxisB)
        {
            float dotA = Vector3.Dot(localAxisWorld, worldAxisA);
            float weight = dotA * dotA;

            if (worldAxisB.HasValue)
            {
                float dotB = Vector3.Dot(localAxisWorld, worldAxisB.Value);
                weight += dotB * dotB;
            }

            return Mathf.Clamp01(weight);
        }

        private Vector3 ScalePivotOffset(Vector3 offset, float ratio)
        {
            Vector3 mask = GetAxisScaleMask(ratio);

            if (_space == ConstraintSpace.Local && _activeTarget != null)
            {
                Quaternion rot = _activeTarget.transformTarget.rotation;
                Vector3 localOffset = Quaternion.Inverse(rot) * offset;
                localOffset = Vector3.Scale(localOffset, mask);
                return rot * localOffset;
            }

            return Vector3.Scale(offset, mask);
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

        private ITransformTarget GetActiveTarget()
        {
            var mgr = Singleton<GuideObjectManager>.Instance;
            GuideObject rawActive = mgr.operationTarget ?? mgr.selectObject;
            if (rawActive != null && _targets.TryGetValue(rawActive.dicKey, out var wrapped))
                return wrapped;
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
            return _cachedAxisDir;
        }

        private Vector3 GetPlaneNormal()
        {
            return transformer.GetPlaneNormal(_constraint, _space, _activeTarget);
        }

        private void RefreshConstraintDirectionCache()
        {
            _cachedAxisDir = transformer.GetConstraintAxisDir(_constraint, _space, _activeTarget);
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

            if (_mode != TransformMode.Move && _mode != TransformMode.Mirror)
            {
                _guideRenderer.RenderCursorGuideLine(cam, _pivotWorld, MouseWrapService.VirtualMousePosition,
                    new Color(1f, 1f, 1f, 0.6f));
            }
        }

        private void OnGUI()
        {
            _uiDisplay.RenderAccessoryModeIndicator(_accessoryModeActive);

            bool transformActive = _mode != TransformMode.None;
            _uiDisplay.Render(transformActive, _mode, _constraint, _space, _snapping, _cfgSurfaceSnap.Value,
                _isAccessoryMode, _precision, _fineSnap, GetLiveTransformText());

            if (!transformActive)
                return;

            DrawCustomCursor();
        }

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
