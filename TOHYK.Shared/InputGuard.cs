using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TOHYK
{
    internal static class InputGuard
    {
        private static readonly HashSet<KeyCode> OwnedKeys = new HashSet<KeyCode>
        {
            KeyCode.G, KeyCode.R, KeyCode.S,
            KeyCode.X, KeyCode.Y, KeyCode.Z,
            KeyCode.P, KeyCode.C, KeyCode.M,
            KeyCode.Escape,
            KeyCode.F, KeyCode.KeypadPeriod
        };

        private static readonly HashSet<KeyCode> NumericKeys = new HashSet<KeyCode>
        {
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4,
            KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
            KeyCode.KeypadPeriod, KeyCode.KeypadMinus, KeyCode.KeypadPlus,
            KeyCode.Period, KeyCode.Comma, KeyCode.Minus,
            KeyCode.Backspace
        };

        private static Harmony _harmony;
        private static bool _patched;
        private static bool _active;
        private static bool _numericActive;
        private static bool _uiPatched;
        private static bool _blockUiClicks;

        public static bool Polling;

        public static bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                UpdatePatchState();
            }
        }

        public static bool NumericActive
        {
            get => _numericActive;
            set
            {
                if (_numericActive == value) return;
                _numericActive = value;
                UpdatePatchState();
            }
        }

        public static bool BlockUiClicks
        {
            get => _blockUiClicks;
            set
            {
                if (_blockUiClicks == value) return;
                _blockUiClicks = value;
                UpdateUiPatchState();
            }
        }

        private static bool ShouldSuppress(KeyCode key)
        {
            if (Polling)
                return false;
            if (_active && OwnedKeys.Contains(key))
                return true;
            if (_numericActive && NumericKeys.Contains(key))
                return true;
            return false;
        }

        public static void Initialize(Harmony harmony)
        {
            _harmony = harmony;

            UpdatePatchState();
            UpdateUiPatchState();
        }

        private static void UpdatePatchState()
        {
            bool shouldBePatched = _active || _numericActive;
            if (shouldBePatched == _patched)
                return;

            if (shouldBePatched)
                Patch();
            else
                Unpatch();
        }

        private static void Patch()
        {
            if (_harmony == null || _patched)
                return;

            _harmony.Patch(
                AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) }),
                prefix: new HarmonyMethod(typeof(InputGuard), nameof(GetKeyDownPrefix)));

            _harmony.Patch(
                AccessTools.Method(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) }),
                prefix: new HarmonyMethod(typeof(InputGuard), nameof(GetKeyPrefix)));

            _harmony.Patch(
                AccessTools.Method(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) }),
                prefix: new HarmonyMethod(typeof(InputGuard), nameof(GetKeyUpPrefix)));

            _patched = true;
        }

        private static void Unpatch()
        {
            if (_harmony == null || !_patched)
                return;

            _harmony.Unpatch(
                AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) }),
                HarmonyPatchType.Prefix, _harmony.Id);

            _harmony.Unpatch(
                AccessTools.Method(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) }),
                HarmonyPatchType.Prefix, _harmony.Id);

            _harmony.Unpatch(
                AccessTools.Method(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) }),
                HarmonyPatchType.Prefix, _harmony.Id);

            _patched = false;
        }

        private static void UpdateUiPatchState()
        {
            if (_blockUiClicks == _uiPatched)
                return;

            if (_blockUiClicks)
                PatchUi();
            else
                UnpatchUi();
        }

        private static void PatchUi()
        {
            if (_harmony == null || _uiPatched)
                return;

            _harmony.Patch(
                AccessTools.Method(typeof(GraphicRaycaster), nameof(GraphicRaycaster.Raycast),
                    new[] { typeof(PointerEventData), typeof(List<RaycastResult>) }),
                postfix: new HarmonyMethod(typeof(InputGuard), nameof(RaycastPostfix)));

            _uiPatched = true;
        }

        private static void UnpatchUi()
        {
            if (_harmony == null || !_uiPatched)
                return;

            _harmony.Unpatch(
                AccessTools.Method(typeof(GraphicRaycaster), nameof(GraphicRaycaster.Raycast),
                    new[] { typeof(PointerEventData), typeof(List<RaycastResult>) }),
                HarmonyPatchType.Postfix, _harmony.Id);

            _uiPatched = false;
        }

        private static void RaycastPostfix(List<RaycastResult> resultAppendList)
        {
            if (_blockUiClicks)
                resultAppendList.Clear();
        }

        private static bool GetKeyDownPrefix(KeyCode key, ref bool __result)
        {
            if (!ShouldSuppress(key)) return true;
            __result = false;
            return false;
        }

        private static bool GetKeyPrefix(KeyCode key, ref bool __result)
        {
            if (!ShouldSuppress(key)) return true;
            __result = false;
            return false;
        }

        private static bool GetKeyUpPrefix(KeyCode key, ref bool __result)
        {
            if (!ShouldSuppress(key)) return true;
            __result = false;
            return false;
        }
    }
}