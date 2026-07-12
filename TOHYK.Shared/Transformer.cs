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
            Vector3 startMousePlane,
            Vector3 startMouseAxis,
            AxisConstraint constraint,
            ConstraintSpace space,
            GuideObject activeTarget,
            Vector3 pivotWorld)
        {
            switch (constraint)
            {
                case AxisConstraint.Free:
                    return ComputeFreeMoveDelta(startMousePlane, pivotWorld);

                case AxisConstraint.AxisX:
                case AxisConstraint.AxisY:
                case AxisConstraint.AxisZ:
                    return ComputeAxisMoveDelta(startMouseAxis, pivotWorld, constraint, space, activeTarget);

                case AxisConstraint.PlaneXY:
                case AxisConstraint.PlaneXZ:
                case AxisConstraint.PlaneYZ:
                    return ComputePlaneMoveDelta(startMousePlane, pivotWorld, constraint, space, activeTarget);

                default:
                    return Vector3.zero;
            }
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
            AxisConstraint constraint,
            ConstraintSpace space,
            GuideObject activeTarget)
        {
            Vector3 axisDir = GetConstraintAxisDir(constraint, space, activeTarget);
            Vector3 currentProj = GetMouseOnAxis(pivotWorld, axisDir);
            return currentProj - startMouseAxis;
        }

        private Vector3 ComputePlaneMoveDelta(
            Vector3 startMousePlane,
            Vector3 pivotWorld,
            AxisConstraint constraint,
            ConstraintSpace space,
            GuideObject activeTarget)
        {
            Vector3 normal = GetPlaneNormal(constraint, space, activeTarget);
            Vector3 current = GetMouseWorldOnPlane(pivotWorld, normal);
            return current - startMousePlane;
        }

        public void ComputeFreeRotateAngles(Vector2 startMouseScreen, out float angleX, out float angleY, bool snapping)
        {
            Vector2 currentMouse = MouseWrapService.VirtualMousePosition;
            Vector2 deltaPx = currentMouse - startMouseScreen;

            float sensitivity = _rotateSensitivity.Value * 0.5f;
            angleX = deltaPx.y / Screen.height * 360f * sensitivity;
            angleY = -deltaPx.x / Screen.width * 360f * sensitivity;

            if (!snapping) return;
            
            float snap = _snapAngle.Value;
            angleX = Mathf.Round(angleX / snap) * snap;
            angleY = Mathf.Round(angleY / snap) * snap;
        }

        /// <summary>
        /// Angle for axis/plane-constrained rotation (R+X/Y/Z etc). Unlike the
        /// old atan2-around-pivot approach, this maps the mouse's linear
        /// travel along the on-screen tangent of the rotation axis to angle,
        /// the same "infinite drag" philosophy used by Move. This keeps
        /// rotation speed consistent regardless of where the pivot sits on
        /// screen or of tracing a literal circle - dragging straight in the
        /// tangent direction now always produces steady rotation.
        ///
        /// Exception: if the axis points almost straight at/away from the
        /// camera there is no usable on-screen tangent (its projection
        /// degenerates to a point), so we fall back to sweeping the angle
        /// around the pivot's screen position, which is the correct "spin
        /// the dial" gesture for that specific viewing angle.
        /// </summary>
        public float ComputeConstrainedRotateAngle(
            Vector2 startMouseScreen,
            Vector3 pivotWorld,
            Vector3 axis,
            bool snapping)
        {
            var cam = _getCamera();
            if (cam == null)
                return 0f;

            Vector2 currentMouse = MouseWrapService.VirtualMousePosition;

            Vector3 pivotScreen3 = cam.WorldToScreenPoint(pivotWorld);
            Vector2 pivotScreen = new Vector2(pivotScreen3.x, pivotScreen3.y);

            Vector3 axisNorm = axis.normalized;
            float facing = Mathf.Abs(Vector3.Dot(axisNorm, cam.transform.forward));

            float deltaAngle;

            if (facing > 0.98f)
            {
                float startDeg = Mathf.Atan2(startMouseScreen.y - pivotScreen.y, startMouseScreen.x - pivotScreen.x) * Mathf.Rad2Deg;
                float currentDeg = Mathf.Atan2(currentMouse.y - pivotScreen.y, currentMouse.x - pivotScreen.x) * Mathf.Rad2Deg;
                deltaAngle = Mathf.DeltaAngle(startDeg, currentDeg);
            }
            else
            {
                Vector3 axisScreen3 = cam.WorldToScreenPoint(pivotWorld + axisNorm);
                Vector2 axisScreenDir = new Vector2(axisScreen3.x, axisScreen3.y) - pivotScreen;

                if (axisScreenDir.sqrMagnitude < 1e-6f)
                    axisScreenDir = Vector2.right;
                axisScreenDir.Normalize();

                // Perpendicular to the axis' own screen projection = the
                // direction the mouse should travel for a pure rotation.
                Vector2 tangent = new Vector2(-axisScreenDir.y, axisScreenDir.x);

                Vector2 deltaPx = currentMouse - startMouseScreen;
                float travel = Vector2.Dot(deltaPx, tangent);

                float sensitivity = _rotateSensitivity.Value * 0.5f;
                deltaAngle = travel / Screen.height * 360f * sensitivity;
            }

            float sign = Vector3.Dot(axisNorm, cam.transform.forward) > 0 ? 1f : -1f;
            deltaAngle *= sign;

            if (snapping)
            {
                float snap = _snapAngle.Value;
                deltaAngle = Mathf.Round(deltaAngle / snap) * snap;
            }

            return deltaAngle;
        }

        public float ComputeScaleRatio(Vector3 pivotWorld, float startDist, bool snapping)
        {
            var cam = _getCamera();
            Vector2 currentMouse = MouseWrapService.VirtualMousePosition;

            Vector3 pivotScreen = cam.WorldToScreenPoint(pivotWorld);
            Vector2 pivotScreen2D = new Vector2(pivotScreen.x, pivotScreen.y);
            float currentDist = Vector2.Distance(currentMouse, pivotScreen2D);
            float ratio = currentDist / startDist;

            if (snapping)
            {
                float snap = _snapScale.Value;
                ratio = Mathf.Round(ratio / snap) * snap;
                if (ratio < snap)
                    ratio = snap;
            }

            return ratio;
        }

        public Vector3 ApplyMoveSnapping(Vector3 delta, AxisConstraint constraint, bool snapping)
        {
            if (!snapping)
                return delta;

            float snap = _snapDistance.Value;

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

        public Vector3 ApplyConstraintMask(Vector3 delta, AxisConstraint constraint, ConstraintSpace space, GuideObject activeTarget)
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

        public Vector3 GetConstraintAxisDir(AxisConstraint constraint, ConstraintSpace space, GuideObject activeTarget)
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

        public Vector3 GetPlaneNormal(AxisConstraint constraint, ConstraintSpace space, GuideObject activeTarget)
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
