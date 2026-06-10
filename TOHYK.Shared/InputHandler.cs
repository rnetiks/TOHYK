using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace TOHYK
{
    public class InputHandler
    {
        private readonly ConfigEntry<KeyboardShortcut> _keyMove;
        private readonly ConfigEntry<KeyboardShortcut> _keyRotate;
        private readonly ConfigEntry<KeyboardShortcut> _keyScale;
        private readonly ConfigEntry<KeyboardShortcut> _keyAxisX;
        private readonly ConfigEntry<KeyboardShortcut> _keyAxisY;
        private readonly ConfigEntry<KeyboardShortcut> _keyAxisZ;
        private readonly ConfigEntry<KeyboardShortcut> _keyPivotCycle;
        private readonly ConfigEntry<KeyboardShortcut> _keySnapCycle;

        public InputHandler(ConfigFile config)
        {
            _keyMove = config.Bind("Hotkeys", "Move",
                new KeyboardShortcut(KeyCode.G),
                "Press to enter Move mode.");
            _keyRotate = config.Bind("Hotkeys", "Rotate",
                new KeyboardShortcut(KeyCode.R),
                "Press to enter Rotate mode.");
            _keyScale = config.Bind("Hotkeys", "Scale",
                new KeyboardShortcut(KeyCode.S), "Press to enter Scale mode.");
            _keyAxisX = config.Bind("Hotkeys", "X Axis",
                new KeyboardShortcut(KeyCode.X));
            _keyAxisY = config.Bind("Hotkeys", "Y Axis",
                new KeyboardShortcut(KeyCode.Y));
            _keyAxisZ = config.Bind("Hotkeys", "Z Axis",
                new KeyboardShortcut(KeyCode.Z));
            _keyPivotCycle = config.Bind("Hotkeys", "Cycle Pivot",
                new KeyboardShortcut(KeyCode.P),
                "Press to cycle through pivot modes (Median / Active / Individual).");
            _keySnapCycle = config.Bind("Hotkeys", "Toggle Snap",
                new KeyboardShortcut(KeyCode.C));
        }

        public bool HasKeyboardFocus => GUIUtility.keyboardControl != 0;

        public bool IsPivotCyclePressed => _keyPivotCycle.Value.IsDown();
        public bool IsSnapTogglePressed => _keySnapCycle.Value.IsDown();

        public TransformMode? GetTransformModeInput()
        {
            if (_keyMove.Value.IsDown())
                return TransformMode.Move;
            if (_keyRotate.Value.IsDown())
                return TransformMode.Rotate;
            if (_keyScale.Value.IsDown())
                return TransformMode.Scale;
            return null;
        }

        public AxisInput GetAxisInput()
        {
            if (SpecialIsKeyDown(_keyAxisX.Value, out bool shift, KeyCode.LeftShift, KeyCode.RightShift))
                return new AxisInput(AxisConstraint.AxisX, shift);

            if (SpecialIsKeyDown(_keyAxisY.Value, out shift, KeyCode.LeftShift, KeyCode.RightShift))
                return new AxisInput(TOHYK.InvertYZ.Value ? AxisConstraint.AxisZ : AxisConstraint.AxisY, shift);

            if (SpecialIsKeyDown(_keyAxisZ.Value, out shift, KeyCode.LeftShift, KeyCode.RightShift))
                return new AxisInput(TOHYK.InvertYZ.Value ? AxisConstraint.AxisY : AxisConstraint.AxisZ, shift);

            return AxisInput.None;
        }

        public bool IsSnapping => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        
        private bool SpecialIsKeyDown(KeyboardShortcut shortcut, out bool extraKeyHeld,
            params KeyCode[] ignoredKeys)
        {
            // Literally fucking required for it to still access shift while pressing the key
            extraKeyHeld = false;
            if (shortcut.MainKey == KeyCode.None)
                return false;

            if (!UnityInput.Current.GetKeyDown(shortcut.MainKey))
                return false;

            foreach (var mod in shortcut.Modifiers)
            {
                if (!UnityInput.Current.GetKey(mod))
                    return false;
            }

            extraKeyHeld = ignoredKeys.Any(k => UnityInput.Current.GetKey(k));

            var shortcutKeys = new HashSet<KeyCode>(shortcut.Modifiers) { shortcut.MainKey };

            var allowedKeys = new HashSet<KeyCode>(shortcutKeys);
            foreach (var k in ignoredKeys)
                allowedKeys.Add(k);

            var allKeys = UnityInput.Current.SupportedKeyCodes;
            foreach (var key in allKeys)
            {
                if (key == KeyCode.Mouse0 || key == KeyCode.Mouse1 || key == KeyCode.Mouse2 ||
                    key == KeyCode.Mouse3 || key == KeyCode.Mouse4 || key == KeyCode.Mouse5 ||
                    key == KeyCode.Mouse6 || key == KeyCode.None)
                    continue;

                if (UnityInput.Current.GetKey(key) && !allowedKeys.Contains(key))
                    return false;
            }

            return true;
        }
    }
}