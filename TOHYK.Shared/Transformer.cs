using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Studio;
using UnityEngine;

namespace TOHYK
{
    public class Transformer
    {
        private readonly ConfigEntry<float> _snapDistance;
        private readonly ConfigEntry<float> _snapAngle;
        private readonly ConfigEntry<float> _snapScale;
        private readonly ConfigEntry<float> _rotateSensitivity;

        private readonly Func<Camera> _getCamera;
        private readonly Func<Vector3> _getCameraForward;

        public Transformer(ConfigEntry<float> snapDistance,
            ConfigEntry<float> snapAngle,
            ConfigEntry<float> snapScale,
            ConfigEntry<float> rotateSensitivity,
            Func<Camera> getCamera,
            Func<Vector3> getCameraForward)
        {
            _snapDistance = snapDistance;
            _snapAngle = snapAngle;
            _snapScale = snapScale;
            _rotateSensitivity = rotateSensitivity;
            _getCamera = getCamera;
            _getCameraForward = getCameraForward;
        }

        public Vector3 ComputeMoveDelta(
            Vector3 refMousePlane,
            Vector3 refMouseAxis,
            AxisConstraint constraint,
            ConstraintSpace space,
            ITransformTarget activeTarget,
            Vector3 cachedAxisDir,
            Vector3 pivotWorld,
            Vector2 prevMouseScreen,
            bool precision = false)
        {
            if (MouseWrapService.VirtualMousePosition == prevMouseScreen)
                return Vector3.zero;

            Vector3 delta;
            switch (constraint)
            {
                case AxisConstraint.Free:
                    delta = ComputeFreeMoveDelta(refMousePlane, pivotWorld);
                    break;

                case AxisConstraint.AxisX:
                case AxisConstraint.AxisY:
                case AxisConstraint.AxisZ:
                    delta = ComputeAxisMoveDelta(refMouseAxis, pivotWorld, cachedAxisDir);
                    break;

                case AxisConstraint.PlaneXY:
                case AxisConstraint.PlaneXZ:
                case AxisConstraint.PlaneYZ:
                    delta = ComputePlaneMoveDelta(refMousePlane, pivotWorld, constraint, space, activeTarget);
                    break;

                default:
                    return Vector3.zero;
            }

            if (precision) delta *= PrecisionFactor;
            return delta;
        }

        private Vector3 ComputeFreeMoveDelta(Vector3 startMousePlane, Vector3 pivotWorld)
        {
            Vector3 planeNormal = _getCameraForward();
            Vector3 current = GetMouseWorldOnPlane(pivotWorld, planeNormal);
            return current - startMousePlane;
        }

        private Vector3 ComputeAxisMoveDelta(
            Vector3 startMouseAxis,
            Vector3 pivotWorld,
            Vector3 axisDir)
        {
            Vector3 currentProj = GetMouseOnAxis(pivotWorld, axisDir);
            return currentProj - startMouseAxis;
        }

        private Vector3 ComputePlaneMoveDelta(
            Vector3 startMousePlane,
            Vector3 pivotWorld,
            AxisConstraint constraint,
            ConstraintSpace space,
            ITransformTarget activeTarget)
        {
            Vector3 normal = GetPlaneNormal(constraint, space, activeTarget);
            Vector3 current = GetMouseWorldOnPlane(pivotWorld, normal);
            return current - startMousePlane;
        }

        private const float PrecisionFactor = 0.15f;

        internal const float FineSnapDivisor = 100f;

        public void ComputeFreeRotateAngles(Vector2 startMouseScreen, out float angleX, out float angleY, bool snapping, bool precision = false, bool fineSnap = false)
        {
            Vector2 currentMouse = MouseWrapService.VirtualMousePosition;
            Vector2 deltaPx = currentMouse - startMouseScreen;

            float sensitivity = _rotateSensitivity.Value * 0.5f;
            if (precision) sensitivity *= PrecisionFactor;

            angleX = deltaPx.y / Screen.height * 360f * sensitivity;
            angleY = -deltaPx.x / Screen.width * 360f * sensitivity;

            if (!snapping) return;

            float snap = _snapAngle.Value;
            if (fineSnap) snap /= FineSnapDivisor;
            angleX = Mathf.Round(angleX / snap) * snap;
            angleY = Mathf.Round(angleY / snap) * snap;
        }

        public float ComputeConstrainedRotateAngleDelta(
            Vector2 prevMouseScreen,
            Vector3 pivotWorld,
            Vector3 axis,
            bool precision)
        {
            var cam = _getCamera();
            if (cam == null)
                return 0f;

            Vector2 currentMouse = MouseWrapService.VirtualMousePosition;
            if (currentMouse == prevMouseScreen)
                return 0f;

            Vector3 pivotScreen3 = cam.WorldToScreenPoint(pivotWorld);
            Vector2 pivotScreen = new Vector2(pivotScreen3.x, pivotScreen3.y);

            Vector3 axisNorm = axis.normalized;

            float prevDeg = Mathf.Atan2(prevMouseScreen.y - pivotScreen.y, prevMouseScreen.x - pivotScreen.x) * Mathf.Rad2Deg;
            float currentDeg = Mathf.Atan2(currentMouse.y - pivotScreen.y, currentMouse.x - pivotScreen.x) * Mathf.Rad2Deg;
            float deltaAngle = Mathf.DeltaAngle(prevDeg, currentDeg);

            float sign = Vector3.Dot(axisNorm, cam.transform.forward) > 0 ? 1f : -1f;
            deltaAngle *= sign;

            if (precision) deltaAngle *= PrecisionFactor;

            return deltaAngle;
        }

        public float ComputeScaleFrameRatio(Vector3 pivotWorld, float prevDist, Vector2 prevMouseScreen, bool precision = false)
        {
            var cam = _getCamera();
            Vector2 currentMouse = MouseWrapService.VirtualMousePosition;
            if (currentMouse == prevMouseScreen)
                return 1f;

            Vector3 pivotScreen = cam.WorldToScreenPoint(pivotWorld);
            Vector2 pivotScreen2D = new Vector2(pivotScreen.x, pivotScreen.y);
            float currentDist = Vector2.Distance(currentMouse, pivotScreen2D);

            if (prevDist < 1f) prevDist = 1f;
            float frameRatio = currentDist / prevDist;

            if (precision) frameRatio = 1f + (frameRatio - 1f) * PrecisionFactor;

            return frameRatio;
        }

        public float ApplyScaleSnapping(float ratio, bool snapping, bool fineSnap = false)
        {
            if (!snapping)
                return ratio;

            float snap = _snapScale.Value;
            if (fineSnap) snap /= FineSnapDivisor;
            ratio = Mathf.Round(ratio / snap) * snap;
            if (ratio < snap)
                ratio = snap;

            return ratio;
        }

        public Vector3 ApplyMoveSnapping(Vector3 delta, AxisConstraint constraint, bool snapping, bool fineSnap = false)
        {
            if (!snapping)
                return delta;

            float snap = _snapDistance.Value;
            if (fineSnap) snap /= FineSnapDivisor;

            if (constraint == AxisConstraint.AxisX || constraint == AxisConstraint.AxisY || constraint == AxisConstraint.AxisZ)
            {
                Vector3 axisDir = Vector3.right;
                if (constraint == AxisConstraint.AxisY) axisDir = Vector3.up;
                else if (constraint == AxisConstraint.AxisZ) axisDir = Vector3.forward;

                float mag = Vector3.Dot(delta, axisDir);
                mag = Mathf.Round(mag / snap) * snap;
                return axisDir * mag;
            }

            return new Vector3(
                Mathf.Round(delta.x / snap) * snap,
                Mathf.Round(delta.y / snap) * snap,
                Mathf.Round(delta.z / snap) * snap);
        }

        public Vector3 ApplyConstraintMask(Vector3 delta, AxisConstraint constraint, ConstraintSpace space, ITransformTarget activeTarget)
        {
            if (constraint == AxisConstraint.Free)
                return delta;

            bool local = space == ConstraintSpace.Local && activeTarget != null;
            Transform t = local ? activeTarget.transformTarget : null;

            if (local)
                delta = t.InverseTransformDirection(delta);

            switch (constraint)
            {
                case AxisConstraint.AxisX:
                    delta = new Vector3(delta.x, 0f, 0f);
                    break;
                case AxisConstraint.AxisY:
                    delta = new Vector3(0f, delta.y, 0f);
                    break;
                case AxisConstraint.AxisZ:
                    delta = new Vector3(0f, 0f, delta.z);
                    break;
                case AxisConstraint.PlaneXY:
                    delta = new Vector3(delta.x, delta.y, 0f);
                    break;
                case AxisConstraint.PlaneXZ:
                    delta = new Vector3(delta.x, 0f, delta.z);
                    break;
                case AxisConstraint.PlaneYZ:
                    delta = new Vector3(0f, delta.y, delta.z);
                    break;
            }

            if (local)
                delta = t.TransformDirection(delta);

            return delta;
        }

        public Vector3 GetConstraintAxisDir(AxisConstraint constraint, ConstraintSpace space, ITransformTarget activeTarget)
        {
            Vector3 dir;
            switch (constraint)
            {
                case AxisConstraint.AxisX:
                    dir = Vector3.right;
                    break;
                case AxisConstraint.AxisY:
                    dir = Vector3.up;
                    break;
                case AxisConstraint.AxisZ:
                    dir = Vector3.forward;
                    break;
                case AxisConstraint.CameraForward:
                default:
                    return _getCameraForward();
            }

            if (space == ConstraintSpace.Local && activeTarget != null)
                dir = activeTarget.transformTarget.TransformDirection(dir);

            return dir.normalized;
        }

        public Vector3 GetPlaneNormal(AxisConstraint constraint, ConstraintSpace space, ITransformTarget activeTarget)
        {
            Vector3 normal;
            switch (constraint)
            {
                case AxisConstraint.PlaneXY:
                    normal = Vector3.forward;
                    break;
                case AxisConstraint.PlaneXZ:
                    normal = Vector3.up;
                    break;
                case AxisConstraint.PlaneYZ:
                    normal = Vector3.right;
                    break;
                case AxisConstraint.CameraForward:
                default:
                    return _getCameraForward();
            }

            if (space == ConstraintSpace.Local && activeTarget != null)
                normal = activeTarget.transformTarget.TransformDirection(normal);

            return normal.normalized;
        }

        private Vector3 GetMouseWorldOnPlane(Vector3 planePoint, Vector3 planeNormal)
        {
            var cam = _getCamera();
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
            var cam = _getCamera();
            if (cam == null)
                return axisOrigin;

            Ray mouseRay = cam.ScreenPointToRay(MouseWrapService.VirtualMousePosition);
            return ClosestPointOnLineToRay(axisOrigin, axisDir.normalized, mouseRay);
        }

        public static void MirrorAcrossPlane(
            Vector3 worldPos,
            Quaternion worldRot,
            Vector3 worldScale,
            Vector3 pivotWorld,
            Vector3 planeNormalWorld,
            out Vector3 newWorldPos,
            out Quaternion newWorldRot,
            out Vector3 newWorldScale)
        {
            Vector3 n = planeNormalWorld.normalized;

            newWorldPos = worldPos - 2f * Vector3.Dot(worldPos - pivotWorld, n) * n;

            Vector3 fwd = worldRot * Vector3.forward;
            Vector3 up = worldRot * Vector3.up;

            Vector3 rFwd = fwd - 2f * Vector3.Dot(fwd, n) * n;
            Vector3 rUp = up - 2f * Vector3.Dot(up, n) * n;

            newWorldRot = Quaternion.LookRotation(rFwd, rUp);

            newWorldScale = new Vector3(
                Mathf.Abs(worldScale.x),
                Mathf.Abs(worldScale.y),
                Mathf.Abs(worldScale.z));
        }

        private static Vector3 ClosestPointOnLineToRay(Vector3 lineOrigin, Vector3 lineDir, Ray ray)
        {
            Vector3 w = lineOrigin - ray.origin;
            float a = Vector3.Dot(lineDir, lineDir);
            float b = Vector3.Dot(lineDir, ray.direction);
            float c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(lineDir, w);
            float e = Vector3.Dot(ray.direction, w);

            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 1e-8f)
                return lineOrigin;

            float t = (b * e - c * d) / denom;
            return lineOrigin + lineDir * t;
        }
    }
}
