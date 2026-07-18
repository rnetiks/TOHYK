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
        private readonly ConfigEntry<KeyboardShortcut> _keyChaMakerToggle;
        private readonly ConfigEntry<KeyboardShortcut> _keyUndo;
        private readonly ConfigEntry<KeyboardShortcut> _keyRedo;
        private readonly ConfigEntry<KeyboardShortcut> _keyClearPosition;
        private readonly ConfigEntry<KeyboardShortcut> _keyClearRotation;
        private readonly ConfigEntry<KeyboardShortcut> _keyClearScale;
        private readonly ConfigEntry<KeyboardShortcut> _keyMirror;
        private readonly ConfigEntry<KeyboardShortcut> _keyFocusCamera;

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
                "Press to cycle through pivot modes (Center / Last Selected / Each Own Origin / Box Center / Parent Bone).");
            _keySnapCycle = config.Bind("Hotkeys", "Toggle Snap",
                new KeyboardShortcut(KeyCode.C));
            _keyChaMakerToggle = config.Bind("Hotkeys", "Toggle Chara Maker Mode",
                new KeyboardShortcut(KeyCode.Tab),
                "In the Chara Maker (accessory adjust panel open), press to toggle TOHYK on/off for that accessory. " +
                "Kept as an explicit toggle instead of always-on so it doesn't steal Tab (or whatever you rebind this " +
                "to) from other UI while you're not actively using TOHYK.");
            _keyUndo = config.Bind("Hotkeys", "Undo",
                new KeyboardShortcut(KeyCode.Z, KeyCode.LeftControl),
                "Undo the last confirmed accessory move/rotate/scale while in the Chara Maker, same as Ctrl+Z in Studio.");
            _keyRedo = config.Bind("Hotkeys", "Redo",
                new KeyboardShortcut(KeyCode.Y, KeyCode.LeftControl),
                "Redo the last undone accessory move/rotate/scale while in the Chara Maker, same as Ctrl+Y in Studio.");

            _keyClearPosition = config.Bind("Hotkeys", "Clear Position",
                new KeyboardShortcut(KeyCode.G, KeyCode.LeftAlt),
                "Blender-style \"clear transform\": instantly resets the selection's local position to zero, same as Alt+G in Blender.");
            _keyClearRotation = config.Bind("Hotkeys", "Clear Rotation",
                new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt),
                "Blender-style \"clear transform\": instantly resets the selection's local rotation to identity, same as Alt+R in Blender.");
            _keyClearScale = config.Bind("Hotkeys", "Clear Scale",
                new KeyboardShortcut(KeyCode.S, KeyCode.LeftAlt),
                "Blender-style \"clear transform\": instantly resets the selection's local scale to one, same as Alt+S in Blender.");

            _keyMirror = config.Bind("Hotkeys", "Mirror",
                new KeyboardShortcut(KeyCode.M, KeyCode.LeftControl),
                "Blender-style Ctrl+M: press, then X/Y/Z (hold Shift for Local space) to instantly flip the " +
                "selection's position/rotation/scale across the pivot along that axis - handy for cloning a " +
                "paired accessory (earrings, shoulder pads) as its mirror image instead of eyeballing it by hand. " +
                "Escape or right-click cancels before an axis is chosen.");

            _keyFocusCamera = config.Bind("Hotkeys", "Focus Camera On Selection",
                new KeyboardShortcut(KeyCode.F),
                "Chara Maker accessory mode only (while TOHYK is toggled on with Tab): recenters the Studio-style " +
                "camera on the currently selected accessory (or the average position of several, if multi-selected " +
                "via AccMover), same idea as pressing F on a selected object in Studio. The numpad '.' key " +
                "(KeyCode.KeypadPeriod, Blender's own 'View Selected' key) always does the same thing alongside " +
                "whatever this is rebound to.");
        }

        public bool HasKeyboardFocus => GUIUtility.keyboardControl != 0;

        public bool IsPivotCyclePressed => _keyPivotCycle.Value.IsDown();
        public bool IsSnapTogglePressed => _keySnapCycle.Value.IsDown();
        public bool IsChaMakerTogglePressed => _keyChaMakerToggle.Value.IsDown();
        public bool IsUndoPressed => _keyUndo.Value.IsDown();
        public bool IsRedoPressed => _keyRedo.Value.IsDown();
        public bool IsMirrorPressed => _keyMirror.Value.IsDown();

        public bool IsFocusCameraPressed =>
            _keyFocusCamera.Value.IsDown() || UnityInput.Current.GetKeyDown(KeyCode.KeypadPeriod);

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

        public TransformMode? GetClearTransformInput()
        {
            if (_keyClearPosition.Value.IsDown())
                return TransformMode.Move;
            if (_keyClearRotation.Value.IsDown())
                return TransformMode.Rotate;
            if (_keyClearScale.Value.IsDown())
                return TransformMode.Scale;
            return null;
        }

        public AxisInput GetAxisInput()
        {
            if (SpecialIsKeyDown(_keyAxisX.Value, out _, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt))
                return BuildAxisInput(AxisConstraint.AxisX);

            if (SpecialIsKeyDown(_keyAxisY.Value, out _, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt))
                return BuildAxisInput(TOHYK.InvertYZ.Value ? AxisConstraint.AxisZ : AxisConstraint.AxisY);

            if (SpecialIsKeyDown(_keyAxisZ.Value, out _, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt))
                return BuildAxisInput(TOHYK.InvertYZ.Value ? AxisConstraint.AxisY : AxisConstraint.AxisZ);

            return AxisInput.None;
        }

        private AxisInput BuildAxisInput(AxisConstraint constraint)
        {
            bool shift = UnityInput.Current.GetKey(KeyCode.LeftShift) || UnityInput.Current.GetKey(KeyCode.RightShift);
            bool alt = UnityInput.Current.GetKey(KeyCode.LeftAlt) || UnityInput.Current.GetKey(KeyCode.RightAlt);
            return new AxisInput(constraint, shift, alt);
        }

        public bool IsSnapping => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        public bool IsFineSnap => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public bool IsPrecision => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        
        private bool SpecialIsKeyDown(KeyboardShortcut shortcut, out bool extraKeyHeld,
            params KeyCode[] ignoredKeys)
        {
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
