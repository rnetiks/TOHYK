using BepInEx.Configuration;
using System;
using UnityEngine;

namespace TOHYK
{
    public class UIDisplay
    {
        public void Render(TransformMode mode, AxisConstraint constraint, ConstraintSpace space, bool snapping, bool surfaceSnap)
        {
            string modeStr = GetModeString(mode);
            string constraintStr = GetConstraintString(constraint, space);
            string pivotStr = GetPivotString(TOHYK.CfgPivotMode.Value);
            string snapStr = snapping ? " [SNAP]" : "";
            string surfaceStr = mode == TransformMode.Move && surfaceSnap ? " [SURFACE]" : "";

            string text = $"{modeStr},  {constraintStr},  Pivot: {pivotStr}{snapStr}{surfaceStr}";

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft,
                normal = { textColor = Color.white }
            };

            var shadowStyle = new GUIStyle(style)
            {
                normal = { textColor = Color.black }
            };

            float padding = 800f;
            var calcSize = style.CalcSize(new GUIContent(text));
            Rect rect = new Rect(Screen.width / 2f - calcSize.x / 2, Screen.height - 40, 600, 30);
            Rect shadowRect = new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height);

            GUI.Label(shadowRect, text, shadowStyle);
            GUI.Label(rect, text, style);
        }

        private static string GetModeString(TransformMode mode)
        {
            switch (mode)
            {
                case TransformMode.Move:
                    return "MOVE";
                case TransformMode.Rotate:
                    return "ROTATE";
                case TransformMode.Scale:
                    return "SCALE";
                default:
                    return "";
            }
        }

        private static string GetConstraintString(AxisConstraint constraint, ConstraintSpace space)
        {
            switch (constraint)
            {
                case AxisConstraint.Free:
                    return "Free";
                case AxisConstraint.AxisX:
                    return $"{space} X";
                case AxisConstraint.AxisY:
                    return $"{space} Y";
                case AxisConstraint.AxisZ:
                    return $"{space} Z";
                case AxisConstraint.PlaneXY:
                    return $"{space} XY";
                case AxisConstraint.PlaneXZ:
                    return $"{space} XZ";
                case AxisConstraint.PlaneYZ:
                    return $"{space} YZ";
                default:
                    return "";
            }
        }

        private static string GetPivotString(PivotMode mode)
        {
            switch (mode)
            {
                case PivotMode.MedianPoint:
                    return "Median";
                case PivotMode.ActiveElement:
                    return "Active";
                case PivotMode.IndividualOrigins:
                    return "Individual";
                default:
                    return "";
            }
        }
    }
}