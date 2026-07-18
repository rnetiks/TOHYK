using KKAPI.Utilities;
using UnityEngine;

namespace TOHYK
{
    public static class CursorService
    {
        private const string MoveFileName = "cursor_move.png";
        private const string RotateFileName = "cursor_rotate.png";
        private const string ScaleFileName = "cursor_scale.png";

        private static float _iconDisplaySize = 35f;

        public static float IconDisplaySize
        {
            get => _iconDisplaySize;
            set => _iconDisplaySize = Mathf.Max(1f, value);
        }

        private static bool _antialiasing = true;

        private static byte[] _moveBytes;
        private static byte[] _rotateBytes;
        private static byte[] _scaleBytes;
        private static bool _bytesLoaded;

        private static Texture2D _moveCursor;
        private static Texture2D _rotateCursor;
        private static Texture2D _scaleCursor;
        private static int _builtForSize = -1;

        private static TransformMode _mode = TransformMode.None;

        public static void SetAntialiasing(bool enabled)
        {
            _antialiasing = enabled;

            FilterMode mode = enabled ? FilterMode.Bilinear : FilterMode.Point;
            if (_moveCursor != null) _moveCursor.filterMode = mode;
            if (_rotateCursor != null) _rotateCursor.filterMode = mode;
            if (_scaleCursor != null) _scaleCursor.filterMode = mode;
        }

        public static void SetForMode(TransformMode mode)
        {
            EnsureBytesLoaded();
            EnsureTexturesBuilt();
            _mode = mode;
            Cursor.visible = mode == TransformMode.None;
        }

        public static void Reset()
        {
            _mode = TransformMode.None;
            Cursor.visible = true;
        }

        public static void Draw(TransformMode mode, Vector2 mouseGuiPos, float angleDeg)
        {
            if (mode == TransformMode.None)
                return;

            EnsureTexturesBuilt();

            Texture2D tex;
            switch (mode)
            {
                case TransformMode.Move:
                    tex = _moveCursor;
                    break;
                case TransformMode.Rotate:
                    tex = _rotateCursor;
                    break;
                case TransformMode.Scale:
                    tex = _scaleCursor;
                    break;
                default:
                    tex = null;
                    break;
            }

            if (tex == null)
                return;

            float size = IconDisplaySize;
            float pixelX = Mathf.Round(mouseGuiPos.x - size / 2f);
            float pixelY = Mathf.Round(mouseGuiPos.y - size / 2f);
            var rect = new Rect(pixelX, pixelY, size, size);

            Matrix4x4 prevMatrix = GUI.matrix;
            if (mode == TransformMode.Rotate || mode == TransformMode.Scale)
                GUIUtility.RotateAroundPivot(angleDeg, mouseGuiPos);

            GUI.DrawTexture(rect, tex);

            GUI.matrix = prevMatrix;
        }

        private static void EnsureBytesLoaded()
        {
            if (_bytesLoaded)
                return;

            _bytesLoaded = true;

            _moveBytes = ResourceUtils.GetEmbeddedResource(MoveFileName, null);
            _rotateBytes = ResourceUtils.GetEmbeddedResource(RotateFileName, null);
            _scaleBytes = ResourceUtils.GetEmbeddedResource(ScaleFileName, null);
        }

        private static void EnsureTexturesBuilt()
        {
            int targetSize = Mathf.Max(1, Mathf.RoundToInt(IconDisplaySize));
            if (targetSize == _builtForSize)
                return;

            EnsureBytesLoaded();

            FilterMode filterMode = _antialiasing ? FilterMode.Bilinear : FilterMode.Point;

            DestroyIfNotNull(ref _moveCursor);
            DestroyIfNotNull(ref _rotateCursor);
            DestroyIfNotNull(ref _scaleCursor);

            _moveCursor = BuildScaledTexture(_moveBytes, targetSize, filterMode);
            _rotateCursor = BuildScaledTexture(_rotateBytes, targetSize, filterMode);
            _scaleCursor = BuildScaledTexture(_scaleBytes, targetSize, filterMode);

            _builtForSize = targetSize;
        }

        private static void DestroyIfNotNull(ref Texture2D tex)
        {
            if (tex != null)
            {
                Object.Destroy(tex);
                tex = null;
            }
        }

        private static Texture2D BuildScaledTexture(byte[] pngBytes, int targetSize, FilterMode filterMode)
        {
            if (pngBytes == null)
                return null;

            var srcTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            srcTex.LoadImage(pngBytes); 

            Color32[] srcPixels = srcTex.GetPixels32();
            int srcW = srcTex.width;
            int srcH = srcTex.height;
            Object.Destroy(srcTex);

            Color32[] dstPixels = BoxDownsample(srcPixels, srcW, srcH, targetSize, targetSize);

            var tex = new Texture2D(targetSize, targetSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(dstPixels);
            tex.Apply(false, false);
            tex.filterMode = filterMode;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private static Color32[] BoxDownsample(Color32[] src, int srcW, int srcH, int dstW, int dstH)
        {
            var dst = new Color32[dstW * dstH];
            float scaleX = (float)srcW / dstW;
            float scaleY = (float)srcH / dstH;

            for (int y = 0; y < dstH; y++)
            {
                int sy0 = Mathf.FloorToInt(y * scaleY);
                int sy1 = Mathf.Min(srcH - 1, Mathf.FloorToInt((y + 1) * scaleY - 1));
                sy1 = Mathf.Max(sy0, sy1);

                for (int x = 0; x < dstW; x++)
                {
                    int sx0 = Mathf.FloorToInt(x * scaleX);
                    int sx1 = Mathf.Min(srcW - 1, Mathf.FloorToInt((x + 1) * scaleX - 1));
                    sx1 = Mathf.Max(sx0, sx1);

                    long rSum = 0, gSum = 0, bSum = 0, aSum = 0;
                    int count = 0;

                    for (int sy = sy0; sy <= sy1; sy++)
                    {
                        for (int sx = sx0; sx <= sx1; sx++)
                        {
                            Color32 c = src[sy * srcW + sx];
                            rSum += c.r * c.a;
                            gSum += c.g * c.a;
                            bSum += c.b * c.a;
                            aSum += c.a;
                            count++;
                        }
                    }

                    byte outA = (byte)(aSum / count);
                    byte outR, outG, outB;
                    if (aSum > 0)
                    {
                        outR = (byte)(rSum / aSum);
                        outG = (byte)(gSum / aSum);
                        outB = (byte)(bSum / aSum);
                    }
                    else
                    {
                        outR = outG = outB = 0;
                    }

                    dst[y * dstW + x] = new Color32(outR, outG, outB, outA);
                }
            }

            return dst;
        }
    }
}
