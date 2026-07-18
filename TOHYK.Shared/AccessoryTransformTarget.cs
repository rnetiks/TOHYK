#if KKS
using UnityEngine;

namespace TOHYK
{
    public class AccessoryTransformTarget : ITransformTarget
    {
        private readonly ChaControl _chaCtrl;
        private readonly int _slotNo;
        private readonly int _correctNo;
        private readonly Transform _transform;

        public AccessoryTransformTarget(ChaControl chaCtrl, int slotNo, int correctNo, Transform transform)
        {
            _chaCtrl = chaCtrl;
            _slotNo = slotNo;
            _correctNo = correctNo;
            _transform = transform;
        }

        public int Key => 1000 + _slotNo * 2 + _correctNo;

        public Transform transformTarget => _transform;

        public bool enablePos => true;
        public bool enableRot => true;
        public bool enableScale => true;

        public Vector3 PosLocal
        {
            get => _transform.localPosition;
            set => _transform.localPosition = value;
        }

        public Vector3 RotLocal
        {
            get => _transform.localEulerAngles;
            set => _transform.localEulerAngles = value;
        }

        public Vector3 ScaleLocal
        {
            get => _transform.localScale;
            set => _transform.localScale = value;
        }

        public string CurrentParentKey => _chaCtrl.nowCoordinate.accessory.parts[_slotNo].parentKey;

        public bool TryResolveBone(string key, out Transform bone)
        {
            bone = null;
            if (string.IsNullOrEmpty(key) || key == "none")
                return false;

            ChaReference.RefObjKey refKey;
            try
            {
                refKey = (ChaReference.RefObjKey)System.Enum.Parse(typeof(ChaReference.RefObjKey), key);
            }
            catch (System.ArgumentException)
            {
                return false;
            }

            GameObject boneObj = _chaCtrl.GetReferenceInfo(refKey);
            if (boneObj == null)
                return false;

            bone = boneObj.transform;
            return true;
        }

        public bool SetParentKey(string key)
        {
            if (!TryResolveBone(key, out Transform bone))
                return false;

            GameObject acsRoot = _chaCtrl.objAccessory[_slotNo];
            if (acsRoot == null)
                return false;

            acsRoot.transform.SetParent(bone, false);

            var nowParts = _chaCtrl.nowCoordinate.accessory.parts[_slotNo];
            nowParts.parentKey = key;
            nowParts.partsOfHead = ChaAccessoryDefine.CheckPartsOfHead(key);

            var setParts = _chaCtrl.chaFile.coordinate[_chaCtrl.chaFile.status.coordinateType].accessory.parts[_slotNo];
            setParts.parentKey = key;
            setParts.partsOfHead = nowParts.partsOfHead;

            AccessoryModeService.RefreshParentUI(_slotNo);

            return true;
        }

        public Vector3 CharacterCenterWorld => _chaCtrl.transform.position;

        public static string GetReverseParentKey(string parentKey)
        {
            if (string.IsNullOrEmpty(parentKey))
                return null;

            string reverse = ChaAccessoryDefine.GetReverseParent(parentKey);
            return string.IsNullOrEmpty(reverse) ? null : reverse;
        }

        public Vector3 PivotOffsetLocal => PivotGeometryUtils.GetBoundsCenterOffsetLocal(_transform);

        public Transform ParentBoneTransform => TryResolveBone(CurrentParentKey, out Transform bone) ? bone : null;

        public void Commit()
        {
            Vector3 pos = _transform.localPosition * 100f;
            Vector3 rot = _transform.localEulerAngles;
            Vector3 scl = _transform.localScale;

            _chaCtrl.SetAccessoryPos(_slotNo, _correctNo, pos.x, false, 1);
            _chaCtrl.SetAccessoryPos(_slotNo, _correctNo, pos.y, false, 2);
            _chaCtrl.SetAccessoryPos(_slotNo, _correctNo, pos.z, false, 4);

            _chaCtrl.SetAccessoryRot(_slotNo, _correctNo, rot.x, false, 1);
            _chaCtrl.SetAccessoryRot(_slotNo, _correctNo, rot.y, false, 2);
            _chaCtrl.SetAccessoryRot(_slotNo, _correctNo, rot.z, false, 4);

            _chaCtrl.SetAccessoryScl(_slotNo, _correctNo, scl.x, false, 1);
            _chaCtrl.SetAccessoryScl(_slotNo, _correctNo, scl.y, false, 2);
            _chaCtrl.SetAccessoryScl(_slotNo, _correctNo, scl.z, false, 4);

            var setAccessory = _chaCtrl.chaFile.coordinate[_chaCtrl.chaFile.status.coordinateType].accessory;
            var nowAccessory = _chaCtrl.nowCoordinate.accessory;
            setAccessory.parts[_slotNo].addMove[_correctNo, 0] = nowAccessory.parts[_slotNo].addMove[_correctNo, 0];
            setAccessory.parts[_slotNo].addMove[_correctNo, 1] = nowAccessory.parts[_slotNo].addMove[_correctNo, 1];
            setAccessory.parts[_slotNo].addMove[_correctNo, 2] = nowAccessory.parts[_slotNo].addMove[_correctNo, 2];

            _chaCtrl.UpdateAccessoryMoveFromInfo(_slotNo);

            AccessoryModeService.RefreshMoveWindowUI();
        }
    }
}
#endif