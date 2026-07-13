using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TOHYK
{
    public static class MouseWrapService
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        // How close (in pixels) to the window edge before we trigger a warp.
        private const float EdgeMargin = 2f;

        // Where the cursor lands on the opposite side after a warp (small
        // inset so we don't immediately re-trigger the same check).
        private const float WarpInset = 4f;

        private static IntPtr _hwnd = IntPtr.Zero;
        private static bool _tracking;
        private static Vector2 _lastRaw;
        private static Vector2 _virtualMouse;
        private static CursorLockMode _previousLockState;

        /// <summary>
        /// The unbounded mouse position to use for all transform math while a
        /// mode is active. Falls back to the raw Input.mousePosition when
        /// tracking hasn't been started (shouldn't normally be read in that case).
        /// </summary>
        public static Vector2 VirtualMousePosition => _tracking ? _virtualMouse : (Vector2)Input.mousePosition;

        /// <summary>Call when entering a transform mode (Move/Rotate/Scale) or
        /// whenever mouse references are reset (RefreshMouseReferences).</summary>
        public static void BeginTracking()
        {
            bool wasTracking = _tracking;

            _tracking = true;
            _lastRaw = Input.mousePosition;
            _virtualMouse = _lastRaw;

            ResolveWindowHandle();

            if (!wasTracking)
            {
                _previousLockState = Cursor.lockState;
                // Physically confine the OS cursor to the game window so it
                // can never wander onto a second monitor. This does NOT hide
                // the cursor (unlike CursorLockMode.Locked).
                Cursor.lockState = CursorLockMode.Confined;
            }
        }

        /// <summary>Call when leaving a transform mode (Confirm/Cancel).</summary>
        public static void EndTracking()
        {
            if (_tracking)
                Cursor.lockState = _previousLockState;

            _tracking = false;
        }

        /// <summary>Call once per frame, before any code reads VirtualMousePosition.</summary>
        public static void Tick()
        {
            if (!_tracking)
                return;

            Vector2 raw = Input.mousePosition;
            Vector2 delta = raw - _lastRaw;
            _virtualMouse += delta;
            _lastRaw = raw;

            TryWarpIfNearEdge(raw);
        }

        private static void ResolveWindowHandle()
        {
            // Try to (re-)resolve every time tracking begins - cheap, and
            // means a failed first attempt (e.g. called too early) doesn't
            // permanently disable the warp for the rest of the session.
            try
            {
                IntPtr candidate = Process.GetCurrentProcess().MainWindowHandle;
                if (candidate != IntPtr.Zero)
                {
                    _hwnd = candidate;
                    return;
                }
            }
            catch
            {
                // ignore, fall through to foreground-window fallback
            }

            // Fallback: MainWindowHandle can come back zero in some BepInEx/
            // IL2CPP setups. If the currently focused window belongs to this
            // process, use that instead.
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out uint pid);
                    if (pid == (uint)Process.GetCurrentProcess().Id)
                        _hwnd = fg;
                }
            }
            catch
            {
                // leave _hwnd as-is
            }
        }

        private static void TryWarpIfNearEdge(Vector2 raw)
        {
            if (_hwnd == IntPtr.Zero)
                return;

            float w = Screen.width;
            float h = Screen.height;

            float newX = raw.x;
            float newY = raw.y;
            bool needsWarp = false;

            if (raw.x <= EdgeMargin)
            {
                newX = w - WarpInset;
                needsWarp = true;
            }
            else if (raw.x >= w - EdgeMargin)
            {
                newX = WarpInset;
                needsWarp = true;
            }

            if (raw.y <= EdgeMargin)
            {
                newY = h - WarpInset;
                needsWarp = true;
            }
            else if (raw.y >= h - EdgeMargin)
            {
                newY = WarpInset;
                needsWarp = true;
            }

            if (!needsWarp)
                return;

            var origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(_hwnd, ref origin))
                return;

            // Unity's mouse Y is bottom-up; Win32 screen coords are top-down.
            int screenX = origin.X + Mathf.RoundToInt(newX);
            int screenY = origin.Y + (int)h - Mathf.RoundToInt(newY);

            if (!SetCursorPos(screenX, screenY))
                return;

            // Re-sync the delta baseline to the warped position so the jump
            // itself never shows up as movement next frame.
            _lastRaw = new Vector2(newX, newY);
        }
    }
}
