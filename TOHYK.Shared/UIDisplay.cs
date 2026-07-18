using System.Text;
using UnityEngine;

namespace TOHYK
{
    public class UIDisplay
    {
        #region Shape - baked once, reused forever

        private const float SquircleExponent = 5f;

        private const int CapWidth = 32;
        private const int CapHeight = CapWidth * 3;

        private const int Supersample = 4;

        private static readonly Color BackgroundColor = new Color(0.13f, 0.13f, 0.13f, 0.93f);
        private static readonly Color TextColor = new Color(0.96f, 0.96f, 0.96f, 1f);

        private static Texture2D _capTexture;
        private static Texture2D _fillTexture;

        private static Texture2D CapTexture
        {
            get
            {
                if (_capTexture == null)
                    _capTexture = BuildCapTexture();
                return _capTexture;
            }
        }

        private static Texture2D FillTexture
        {
            get
            {
                if (_fillTexture == null)
                    _fillTexture = BuildFillTexture();
                return _fillTexture;
            }
        }

        private static Texture2D BuildFillTexture()
        {
            var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return tex;
        }

        private static Texture2D BuildCapTexture()
        {
            var tex = new Texture2D(CapWidth, CapHeight, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            const float radius = CapWidth;

            for (int y = 0; y < CapHeight; y++)
            {
                for (int x = 0; x < CapWidth; x++)
                {
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, SampleCapAlpha(x, y, radius)));
                }
            }

            tex.Apply();
            return tex;
        }

        private static float SampleCapAlpha(int px, int py, float radius)
        {
            float covered = 0f;

            for (int sy = 0; sy < Supersample; sy++)
            {
                float y = py + (sy + 0.5f) / Supersample;

                float cy = -1f;
                if (y < radius) cy = radius;
                else if (y >= CapHeight - radius) cy = CapHeight - radius;

                for (int sx = 0; sx < Supersample; sx++)
                {
                    float x = px + (sx + 0.5f) / Supersample;

                    if (cy < 0f)
                    {
                        covered += 1f;
                        continue;
                    }

                    float u = Mathf.Abs(x - radius) / radius;
                    float v = Mathf.Abs(y - cy) / radius;
                    float sum = Mathf.Pow(u, SquircleExponent) + Mathf.Pow(v, SquircleExponent);

                    if (sum <= 1f)
                        covered += 1f;
                }
            }

            return covered / (Supersample * Supersample);
        }

        #endregion

        #region Text styles - cached per font size, never rebuilt per frame

        private const int BasePadFontSize = 14;
        private const int BasePadHorizontal = 18;
        private const int BasePadVertical = 7;

        private GUIStyle _statusBarStyle;
        private int _statusBarStyleFontSize = -1;
        private readonly GUIContent _statusBarContent = new GUIContent();

        private GUIStyle _badgeStyle;
        private int _badgeStyleFontSize = -1;
        private readonly GUIContent _badgeContent = new GUIContent();

        private static GUIStyle BuildTextStyle(int fontSize)
        {
            float scale = fontSize / (float)BasePadFontSize;
            int padH = Mathf.Max(4, Mathf.RoundToInt(BasePadHorizontal * scale));
            int padV = Mathf.Max(2, Mathf.RoundToInt(BasePadVertical * scale));

            return new GUIStyle
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                padding = new RectOffset(padH, padH, padV, padV),
                normal = { textColor = TextColor }
            };
        }

        private GUIStyle GetStatusBarStyle(int fontSize)
        {
            if (_statusBarStyle == null || _statusBarStyleFontSize != fontSize)
            {
                _statusBarStyle = BuildTextStyle(fontSize);
                _statusBarStyleFontSize = fontSize;
            }

            return _statusBarStyle;
        }

        private GUIStyle GetBadgeStyle(int fontSize)
        {
            if (_badgeStyle == null || _badgeStyleFontSize != fontSize)
            {
                _badgeStyle = BuildTextStyle(fontSize);
                _badgeStyleFontSize = fontSize;
            }

            return _badgeStyle;
        }

        #endregion

        #region Pill drawing

        private static void DrawPill(Rect rect, GUIStyle textStyle, GUIContent content, string text, float alpha)
        {
            content.text = text;

            float capRenderWidth = Mathf.Clamp(rect.height * 0.4f, 6f, rect.width / 2f - 1f);

            int leftEdge = Mathf.RoundToInt(rect.x);
            int pillWidth = Mathf.RoundToInt(rect.width);
            int rightEdge = leftEdge + pillWidth;
            int capWidthInt = Mathf.Clamp(Mathf.RoundToInt(capRenderWidth), 1, pillWidth / 2);
            int fillStart = leftEdge + capWidthInt;
            int fillEnd = rightEdge - capWidthInt;
            int fillWidth = Mathf.Max(0, fillEnd - fillStart);

            Color prevColor = GUI.color;

            GUI.color = new Color(BackgroundColor.r, BackgroundColor.g, BackgroundColor.b, BackgroundColor.a * alpha);

            GUI.DrawTexture(new Rect(leftEdge, rect.y, capWidthInt, rect.height), CapTexture);

            if (fillWidth > 0)
                GUI.DrawTexture(new Rect(fillStart, rect.y, fillWidth, rect.height), FillTexture);

            GUI.DrawTextureWithTexCoords(
                new Rect(fillEnd, rect.y, capWidthInt, rect.height),
                CapTexture, new Rect(1f, 0f, -1f, 1f));

            GUI.color = new Color(TextColor.r, TextColor.g, TextColor.b, TextColor.a * alpha);
            GUI.Label(rect, content, textStyle);

            GUI.color = prevColor;
        }

        private static Rect BuildPillRect(Vector2 size, float xOffset, float bottomMargin)
        {
            float x = Screen.width / 2f - size.x / 2f + xOffset;
            float y = Screen.height - size.y - bottomMargin;
            return new Rect(x, y, size.x, size.y);
        }

        #endregion

        #region Status bar (Move/Rotate/Scale live readout)

        private string _statusBarCachedText;
        private Vector2 _statusBarCachedSize;
        private float _statusBarAlpha;
        private string _statusBarFadeText;
        private Vector2 _statusBarFadeSize;

        public void Render(bool active, TransformMode mode, AxisConstraint constraint, ConstraintSpace space,
            bool snapping, bool surfaceSnap, bool isAccessoryMode, bool precision, bool fineSnap, string numericText = null)
        {
            float duration = Mathf.Max(0.001f, TOHYK.CfgHudFadeDuration.Value);
            _statusBarAlpha = Mathf.MoveTowards(_statusBarAlpha, active ? 1f : 0f, Time.unscaledDeltaTime / duration);

            if (_statusBarAlpha <= 0f)
                return; 

            var style = GetStatusBarStyle(TOHYK.CfgHudFontSize.Value);
            string text;
            Vector2 size;

            if (active)
            {
                text = BuildStatusBarText(mode, constraint, space, snapping, surfaceSnap, isAccessoryMode,
                    precision, fineSnap, numericText);

                if (_statusBarCachedText == null || _statusBarCachedText != text)
                {
                    _statusBarContent.text = text;
                    _statusBarCachedSize = style.CalcSize(_statusBarContent);
                    _statusBarCachedText = text;
                }

                size = _statusBarCachedSize;

                _statusBarFadeText = text;
                _statusBarFadeSize = size;
            }
            else
            {
                if (_statusBarFadeText == null)
                    return;

                text = _statusBarFadeText;
                size = _statusBarFadeSize;
            }

            Rect rect = BuildPillRect(size, TOHYK.CfgHudXOffset.Value, TOHYK.CfgHudBottomMargin.Value);
            DrawPill(rect, style, _statusBarContent, text, _statusBarAlpha);
        }

        private const string Separator = "   •   ";

        private static string BuildStatusBarText(TransformMode mode, AxisConstraint constraint, ConstraintSpace space,
            bool snapping, bool surfaceSnap, bool isAccessoryMode, bool precision, bool fineSnap, string numericText)
        {
            if (mode == TransformMode.Mirror)
            {
                string mirrorText = "<b>Mirror</b>" + Separator + "Axis: X / Y / Z";
                if (isAccessoryMode)
                    mirrorText += Separator + "Hold Alt: Also Swap Parent Bone";
                else
                    mirrorText += Separator + "Hold Shift: Local Space";
                return mirrorText;
            }

            string modeStr = GetModeString(mode);
            string constraintStr = GetConstraintString(constraint, space);
            string pivotStr = GetPivotString(TOHYK.CfgPivotMode.Value);

            var sb = new StringBuilder();
            sb.Append("<b>").Append(modeStr).Append("</b>").Append(Separator)
              .Append(constraintStr).Append(Separator)
              .Append("Pivot: ").Append(pivotStr);

            if (snapping)
                sb.Append(Separator).Append("<b>Snap</b>");

            if (snapping && fineSnap)
                sb.Append(Separator).Append("<b>Fine Snap</b>");

            if (precision)
                sb.Append(Separator).Append("<b>Precision</b>");

            if (mode == TransformMode.Move && surfaceSnap)
                sb.Append(Separator).Append("<b>Surface</b>");

            if (!string.IsNullOrEmpty(numericText))
                sb.Append(Separator).Append("<b>").Append(numericText).Append("</b>");

            return sb.ToString();
        }

        #endregion

        #region Accessory badge ("TOHYK Enabled!")

        private string _badgeCachedText;
        private PivotMode _badgeCachedTextPivot = (PivotMode)(-1);
        private Vector2 _badgeCachedSize;
        private int _badgeCachedSizeFontSize = -1;
        private float _badgeAlpha;

        public void RenderAccessoryModeIndicator(bool active)
        {
            float duration = Mathf.Max(0.001f, TOHYK.CfgHudFadeDuration.Value);
            _badgeAlpha = Mathf.MoveTowards(_badgeAlpha, active ? 1f : 0f, Time.unscaledDeltaTime / duration);

            if (_badgeAlpha <= 0f)
                return;

            PivotMode pivot = TOHYK.CfgPivotMode.Value;

            if (_badgeCachedText == null || _badgeCachedTextPivot != pivot)
            {
                _badgeCachedText = $"<b>TOHYK Enabled</b>{Separator}Pivot: {GetPivotString(pivot)}";
                _badgeCachedTextPivot = pivot;
            }

            int fontSize = TOHYK.CfgBadgeFontSize.Value;
            var style = GetBadgeStyle(fontSize);

            if (_badgeCachedSizeFontSize != fontSize)
            {
                string sizeReference = $"<b>TOHYK Enabled</b>{Separator}Pivot: {GetPivotString(PivotMode.IndividualOrigins)}";
                _badgeCachedSize = style.CalcSize(new GUIContent(sizeReference));
                _badgeCachedSizeFontSize = fontSize;
            }

            Rect rect = BuildPillRect(_badgeCachedSize, TOHYK.CfgBadgeXOffset.Value, TOHYK.CfgBadgeBottomMargin.Value);
            DrawPill(rect, style, _badgeContent, _badgeCachedText, _badgeAlpha);
        }

        #endregion

        #region Label text helpers

        private static string GetModeString(TransformMode mode)
        {
            switch (mode)
            {
                case TransformMode.Move:
                    return "Move";
                case TransformMode.Rotate:
                    return "Rotate";
                case TransformMode.Scale:
                    return "Scale";
                case TransformMode.Mirror:
                    return "Mirror";
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
                case AxisConstraint.CameraForward:
                    return "View Axis";
                default:
                    return "";
            }
        }

        private static string GetPivotString(PivotMode mode)
        {
            switch (mode)
            {
                case PivotMode.MedianPoint:
                    return "Center";
                case PivotMode.ActiveElement:
                    return "Last Selected";
                case PivotMode.IndividualOrigins:
                    return "Each Own Origin";
                case PivotMode.BoundingBoxCenter:
                    return "Box Center";
                case PivotMode.AccessoryParent:
                    return "Parent Bone";
                default:
                    return "";
            }
        }

        #endregion
    }
}
