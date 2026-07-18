#if KKS
using System.Collections.Generic;
using ChaCustom;
using UnityEngine;

namespace TOHYK
{
    public static class AccessoryModeService
    {
        private static CustomAcsMoveWindow _moveWindow;
        private static float _nextLookup;

        public static bool IsChaMakerScene => Singleton<CustomBase>.IsInstance();

        private static CustomAcsMoveWindow MoveWindow
        {
            get
            {
                if (_moveWindow == null && Time.unscaledTime >= _nextLookup)
                {
                    _moveWindow = Object.FindObjectOfType<CustomAcsMoveWindow>();
                    _nextLookup = Time.unscaledTime + 1f;
                }
                return _moveWindow;
            }
        }

        public static bool IsAccessoryPanelOpen
        {
            get
            {
                var win = MoveWindow;
                return win != null && win.gameObject.activeInHierarchy;
            }
        }

        public static bool TryGetActiveAccessoryTarget(out AccessoryTransformTarget target)
        {
            target = null;

            var win = MoveWindow;
            if (win == null || !win.gameObject.activeInHierarchy)
                return false;

            if (!Singleton<CustomBase>.IsInstance())
                return false;

            ChaControl chaCtrl = Singleton<CustomBase>.Instance.chaCtrl;
            if (chaCtrl == null)
                return false;

            int slotNo = win.nSlotNo;
            int correctNo = win.correctNo;

            GameObject moveNode = chaCtrl.objAcsMove[slotNo, correctNo];
            if (moveNode == null)
                return false;

            target = new AccessoryTransformTarget(chaCtrl, slotNo, correctNo, moveNode.transform);
            return true;
        }

        private static readonly List<int> AccMoverSlotsBuffer = new List<int>();

        public static BepInEx.Configuration.ConfigEntry<bool> CfgDebugAccMover;

        private static void DebugLog(string message)
        {
            if (CfgDebugAccMover != null && CfgDebugAccMover.Value)
                TOHYK.Log?.LogInfo("[TOHYK/AccMover] " + message);
        }

        public static bool TryGetActiveAccessoryTargets(out List<AccessoryTransformTarget> targets)
        {
            targets = null;

            var win = MoveWindow;
            if (win == null || !win.gameObject.activeInHierarchy)
            {
                DebugLog("Move window not open - nothing to attach to.");
                return false;
            }

            if (!Singleton<CustomBase>.IsInstance())
                return false;

            ChaControl chaCtrl = Singleton<CustomBase>.Instance.chaCtrl;
            if (chaCtrl == null)
                return false;

            int correctNo = win.correctNo;

            DebugLog($"AccMoverBridge.IsUsable = {AccMoverBridge.IsUsable}");

            if (AccMoverBridge.IsUsable && AccMoverBridge.TryGetSelectedSlots(AccMoverSlotsBuffer))
            {
                DebugLog($"AccMover reports {AccMoverSlotsBuffer.Count} selected slot(s): " +
                          string.Join(",", AccMoverSlotsBuffer));
            }
            else
            {
                DebugLog("AccMoverBridge.TryGetSelectedSlots returned false (not usable, or nothing selected).");
            }

            if (AccMoverBridge.IsUsable && AccMoverSlotsBuffer.Count > 1)
            {
                var multi = new List<AccessoryTransformTarget>(AccMoverSlotsBuffer.Count);
                foreach (int slotNo in AccMoverSlotsBuffer)
                {
                    if (slotNo < 0)
                        continue;

                    GameObject moveNode = chaCtrl.objAcsMove[slotNo, correctNo];
                    if (moveNode == null)
                    {
                        DebugLog($"slot {slotNo} (correctNo {correctNo}): objAcsMove is null, skipping it.");
                        continue;
                    }

                    multi.Add(new AccessoryTransformTarget(chaCtrl, slotNo, correctNo, moveNode.transform));
                }

                if (multi.Count > 0)
                {
                    DebugLog($"Using AccMover multi-selection: {multi.Count} target(s) built (correctNo {correctNo}).");
                    targets = multi;
                    return true;
                }

                DebugLog("AccMover selection produced zero usable targets - falling back to single-accessory.");
            }

            if (!TryGetActiveAccessoryTarget(out var single))
                return false;

            DebugLog($"Using single-accessory path: slot {win.nSlotNo}, correctNo {correctNo}.");
            targets = new List<AccessoryTransformTarget> { single };
            return true;
        }

        public static void RefreshMoveWindowUI()
        {
            _moveWindow?.UpdateCustomUI();
        }

        private static System.Reflection.FieldInfo _cvsAccessoryField;

        public static void RefreshParentUI(int slotNo)
        {
            var win = MoveWindow;
            if (win == null)
                return;

            if (_cvsAccessoryField == null)
            {
                _cvsAccessoryField = typeof(CustomAcsMoveWindow).GetField("cvsAccessory",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }

            if (!(_cvsAccessoryField?.GetValue(win) is CvsAccessory[] cvsAccessoryArray))
                return;

            if (slotNo < 0 || slotNo >= cvsAccessoryArray.Length)
                return;

            cvsAccessoryArray[slotNo]?.UpdateAccessoryParentInfo();
        }
    }
}
#endif