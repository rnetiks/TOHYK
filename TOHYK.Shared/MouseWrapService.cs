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

        private const float EdgeMargin = 2f;

        private const float WarpInset = 4f;

        private static IntPtr _hwnd = IntPtr.Zero;
        private static bool _tracking;
        private static Vector2 _lastRaw;
        private static Vector2 _virtualMouse;
        private static CursorLockMode _previousLockState;

        public static Vector2 VirtualMousePosition => _tracking ? _virtualMouse : (Vector2)Input.mousePosition;

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
                Cursor.lockState = CursorLockMode.Confined;
            }
        }

        public static void EndTracking()
        {
            if (_tracking)
                Cursor.lockState = _previousLockState;

            _tracking = false;
        }

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
            }

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

            int screenX = origin.X + Mathf.RoundToInt(newX);
            int screenY = origin.Y + (int)h - Mathf.RoundToInt(newY);

            if (!SetCursorPos(screenX, screenY))
                return;

            _lastRaw = new Vector2(newX, newY);
        }
    }
}
