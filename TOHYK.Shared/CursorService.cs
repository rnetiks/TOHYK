using System.IO;
using System.Reflection;
using UnityEngine;

namespace TOHYK
{
    /// <summary>
    /// Draws a custom on-screen cursor icon while a transform mode (Move/
    /// Rotate/Scale) is active, similar to Blender showing a distinct icon
    /// for G/S vs R.
    ///
    /// This used to be implemented by swapping the real OS hardware cursor
    /// (Cursor.SetCursor) and CPU-rotating a bitmap into it every frame.
    /// That approach is fundamentally unreliable for this use case: Windows/
    /// Unity cache the native cursor bitmap per Texture2D instance, so
    /// repeatedly mutating and reapplying the same (or a small ping-ponged
    /// set of) texture(s) makes the displayed icon randomly "stick" to a
    /// stale rotation instead of updating - exactly the "flips between two
    /// fixed angles" symptom, no matter how the update rate is tuned.
    ///
    /// Instead, we hide the real OS cursor while a mode is active and draw
    /// our own icon every OnGUI call, rotated with a GPU matrix
    /// (GUIUtility.RotateAroundPivot) around the mouse position. This
    /// sidesteps the native cursor entirely: no OS caching quirks, no CPU
    /// pixel rotation, no per-frame allocation.
    ///
    /// Source PNGs are clean 256x256 art (hard edges, no baked-in AA halo).
    /// At the small on-screen size these icons are drawn at, letting the GPU
    /// bilinear-filter straight from 256x256 down to ~24px isn't enough
    /// smoothing on its own (bilinear only samples a 2x2 texel neighborhood,
    /// which is a tiny fraction of a 256x256 source at that much
    /// minification) - it still looks aliased/hard. So instead we
    /// pre-downscale the source to (approximately) the actual display size
    /// ourselves with a box filter (see BuildScaledTexture/BoxDownsample
    /// below), and let Bilinear filtering do the final sub-pixel smoothing
    /// on top of that - this is what actually looks smooth.
    ///
    /// Both the on-screen size and the antialiasing (texture filtering) are
    /// configurable via BepInEx settings - see IconDisplaySize and
    /// SetAntialiasing() below, wired up from TOHYK.cs Awake(). Antialiasing
    /// on = Bilinear filtering (smooth); off = Point filtering (hard,
    /// pixelated edges).
    /// </summary>
    public static class CursorService
    {
        private const string MoveFileName = "cursor_move.png";
        private const string RotateFileName = "cursor_rotate.png";
        private const string ScaleFileName = "cursor_scale.png";

        private static float _iconDisplaySize = 35f;

        /// <summary>
        /// On-screen size (in GUI pixels) the cursor icon is drawn at.
        /// Bound to the "Cursor Size" BepInEx config entry in TOHYK.cs.
        /// Changing this rebuilds the textures (scaled from the source PNGs)
        /// at the new size on the next Draw() call.
        /// </summary>
        public static float IconDisplaySize
        {
            get => _iconDisplaySize;
            set => _iconDisplaySize = Mathf.Max(1f, value);
        }

        /// <summary>
        /// True = Bilinear filtering (smooth). False = Point/nearest-
        /// neighbor filtering (hard, pixelated edges, no smoothing at all).
        /// Bound to the "Cursor Antialiasing" BepInEx config entry in
        /// TOHYK.cs. Can be toggled live - already-loaded textures are
        /// updated immediately via SetAntialiasing().
        /// </summary>
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

        /// <summary>Call once from Awake() (or whenever the config value
        /// changes) to set whether cursor icons are smoothed (bilinear) or
        /// pixelated (point). Safe to call before or after the textures are
        /// built - updates live textures immediately if they already
        /// exist.</summary>
        public static void SetAntialiasing(bool enabled)
        {
            _antialiasing = enabled;

            FilterMode mode = enabled ? FilterMode.Bilinear : FilterMode.Point;
            if (_moveCursor != null) _moveCursor.filterMode = mode;
            if (_rotateCursor != null) _rotateCursor.filterMode = mode;
            if (_scaleCursor != null) _scaleCursor.filterMode = mode;
        }

        /// <summary>Call when entering/leaving a transform mode (Move/Rotate/
        /// Scale/None). Hides the real OS cursor for any active mode - our
        /// own icon is what gets drawn instead, via Draw() from OnGUI.</summary>
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

        /// <summary>
        /// Call every OnGUI while a mode is active.
        /// mouseGuiPos: mouse position in IMGUI space (y-down, origin
        /// top-left) - e.g. new Vector2(screenPos.x, Screen.height - screenPos.y),
        /// using the real/wrapped screen position (Input.mousePosition), NOT
        /// the unbounded "virtual" drag position - the icon should always
        /// sit where the OS pointer visually is on screen.
        /// angleDeg: on-screen angle (degrees) from the pivot to the mouse,
        /// computed the same way as the dashed guide line, in the SAME
        /// (GUI) coordinate space as mouseGuiPos. Ignored for Move (Move's
        /// icon never rotates - the pivot moves with the object instead).
        /// </summary>
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

            _moveBytes = LoadEmbeddedBytes(MoveFileName);
            _rotateBytes = LoadEmbeddedBytes(RotateFileName);
            _scaleBytes = LoadEmbeddedBytes(ScaleFileName);
        }

        /// <summary>(Re)builds the three cursor textures, scaled down from
        /// the 256x256 source PNGs to the current IconDisplaySize with a box
        /// filter, whenever the size has changed since the last build. This
        /// is what makes Bilinear filtering actually look smooth - see the
        /// class summary above.</summary>
        private static void EnsureTexturesBuilt()
        {
            int targetSize = Mathf.Max(1, Mathf.RoundToInt(IconDisplaySize));
            if (targetSize == _builtForSize)
                return;

            EnsureBytesLoaded();

            FilterMode filterMode = _antialiasing ? FilterMode.Bilinear : FilterMode.Point;

            // Old textures (built for the previous size) are about to be
            // replaced - destroy them first, otherwise every time the user
            // tweaks "Cursor Size" in the config manager we'd leak a set of
            // GPU textures that nothing references anymore.
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

        private static byte[] LoadEmbeddedBytes(string fileName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                // The full logical resource name is <RootNamespace>.<FolderPath
                // with dots>.<FileName>, but RootNamespace differs between the
                // KK/KKS head projects (and isn't "TOHYK.Shared" - that's only
                // the shared project's own Import_RootNamespace, not what ends
                // up in the compiled assembly). Rather than hard-coding a
                // prefix that could silently mismatch per-target, match on
                // whichever embedded resource name ends with our file name.
                string resourceName = null;
                foreach (string candidate in asm.GetManifestResourceNames())
                {
                    if (candidate.EndsWith("." + fileName))
                    {
                        resourceName = candidate;
                        break;
                    }
                }

                if (resourceName == null)
                {
                    Debug.LogWarning($"[TOHYK] Cursor icon not found as an embedded resource ending in '{fileName}'. " +
                                      "Make sure Resources/*.png is marked as EmbeddedResource in the .projitems/.csproj. " +
                                      "Falling back to the default cursor for that mode.");
                    return null;
                }

                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    byte[] bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    return bytes;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TOHYK] Failed to load cursor icon '{fileName}': {e.Message}");
                return null;
            }
        }

        private static Texture2D BuildScaledTexture(byte[] pngBytes, int targetSize, FilterMode filterMode)
        {
            if (pngBytes == null)
                return null;

            var srcTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            srcTex.LoadImage(pngBytes); // resizes to the PNG's actual dimensions

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

        /// <summary>
        /// Box-filter resize (handles both down- and up-scaling). Uses
        /// premultiplied-alpha averaging so fully-transparent (alpha=0)
        /// source pixels - which are black in the PNG - don't drag the
        /// averaged color of edge pixels toward black. Without this, edges
        /// get a dark/grey fringe once shrunk, which is exactly the kind of
        /// "dirty" antialiasing we're trying to avoid.
        /// </summary>
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
                            // Premultiply by alpha before summing so
                            // transparent black pixels contribute nothing.
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