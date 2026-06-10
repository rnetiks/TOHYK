using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace TOHYK
{
    public class GuideRenderer
    {
        private readonly Material _glMat;

        public GuideRenderer(Material glMat)
        {
            _glMat = glMat;
        }

        public void Render(Dictionary<int, GuideObject> targets,
            Vector3 pivotWorld,
            AxisConstraint constraint,
            ConstraintSpace space,
            GuideObject activeTarget,
            PivotMode pivotMode,
            Vector3? cachedAxis = null,
            Vector3? cachedNormal = null)
        {
            if (_glMat == null)
                return;

            _glMat.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            if (pivotMode == PivotMode.IndividualOrigins)
            {
                foreach (var go in targets.Values)
                    DrawCrossAt(go.transformTarget.position, 0.03f, new Color(1f, 1f, 1f, 0.5f));
            }

            DrawCrossAt(pivotWorld, 0.05f, Color.white);

            if (constraint != AxisConstraint.Free)
                DrawConstraintVisual(constraint, space, activeTarget, pivotWorld, cachedAxis, cachedNormal);

            GL.PopMatrix();
        }

        public void DrawCrossAt(Vector3 pos, float size, Color color)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);

            GL.Vertex(pos + Vector3.right * size);
            GL.Vertex(pos - Vector3.right * size);
            GL.Vertex(pos + Vector3.up * size);
            GL.Vertex(pos - Vector3.up * size);
            GL.Vertex(pos + Vector3.forward * size);
            GL.Vertex(pos - Vector3.forward * size);

            GL.End();
        }

        public void DrawConstraintVisual(AxisConstraint constraint, ConstraintSpace space, GuideObject activeTarget, Vector3 pivotWorld, Vector3? cachedAxis = null, Vector3? cachedNormal = null)
        {
            float lineLength = 1000f;

            switch (constraint)
            {
                case AxisConstraint.AxisX:
                case AxisConstraint.AxisY:
                case AxisConstraint.AxisZ:
                    {
                        Vector3 dir = cachedAxis ?? GetConstraintAxisDir(constraint, space, activeTarget);
                        Color col = GetAxisColor(constraint);

                        GL.Begin(GL.LINES);
                        GL.Color(col);
                        GL.Vertex(pivotWorld - dir * lineLength);
                        GL.Vertex(pivotWorld + dir * lineLength);
                        GL.End();
                        break;
                    }

                case AxisConstraint.PlaneXY:
                case AxisConstraint.PlaneXZ:
                case AxisConstraint.PlaneYZ:
                {
                    Vector3 dir1 = Vector3.zero;
                    Vector3 dir2 = Vector3.zero;
                    Color c1;
                    Color c2;

                    switch (constraint)
                    {
                        case AxisConstraint.PlaneXY:
                            dir1 = Vector3.right;
                            dir2 = Vector3.up;
                            c1 = GetAxisColor(AxisConstraint.AxisX);
                            c2 = GetAxisColor(AxisConstraint.AxisY);
                            break;
                        case AxisConstraint.PlaneXZ:
                            dir1 = Vector3.right;
                            dir2 = Vector3.forward;
                            c1 = GetAxisColor(AxisConstraint.AxisX);
                            c2 = GetAxisColor(AxisConstraint.AxisZ);
                            break;
                        default:
                            dir1 = Vector3.up;
                            dir2 = Vector3.forward;
                            c1 = GetAxisColor(AxisConstraint.AxisY);
                            c2 = GetAxisColor(AxisConstraint.AxisZ);
                            break;
                    }

                    if (space == ConstraintSpace.Local && activeTarget != null)
                    {
                        dir1 = activeTarget.transformTarget.TransformDirection(dir1);
                        dir2 = activeTarget.transformTarget.TransformDirection(dir2);
                    }

                    GL.Begin(GL.LINES);

                    GL.Color(c1);
                    GL.Vertex(pivotWorld - dir1 * lineLength);
                    GL.Vertex(pivotWorld + dir1 * lineLength);

                    GL.Color(c2);
                    GL.Vertex(pivotWorld - dir2 * lineLength);
                    GL.Vertex(pivotWorld + dir2 * lineLength);

                    GL.End();
                    break;
                }
            }
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
                default:
                    return Vector3.forward;
            }

            if (space == ConstraintSpace.Local && activeTarget != null)
                dir = activeTarget.transformTarget.TransformDirection(dir);

            return dir.normalized;
        }

        public static Color GetAxisColor(AxisConstraint axis)
        {
            switch (axis)
            {
                case AxisConstraint.AxisX:
                    return new Color(1f, 0.2f, 0.2f, 0.9f);
                case AxisConstraint.AxisY:
                    return new Color(0.2f, 1f, 0.2f, 0.9f);
                case AxisConstraint.AxisZ:
                    return new Color(0.3f, 0.3f, 1f, 0.9f);
                default:
                    return Color.white;
            }
        }
    }
}