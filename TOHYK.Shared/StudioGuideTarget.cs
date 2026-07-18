using Studio;
using UnityEngine;

namespace TOHYK
{
    public class StudioGuideTarget : ITransformTarget
    {
        public readonly GuideObject Raw;

        public StudioGuideTarget(GuideObject go)
        {
            Raw = go;
        }

        public int Key => Raw.dicKey;
        public Transform transformTarget => Raw.transformTarget;

        public bool enablePos => Raw.enablePos;
        public bool enableRot => Raw.enableRot;
        public bool enableScale => Raw.enableScale;

        public Vector3 PosLocal
        {
            get => Raw.transformTarget.localPosition;
            set
            {
                Raw.changeAmount.pos = value;
                Raw.transformTarget.localPosition = value;
            }
        }

        public Vector3 RotLocal
        {
            get => Raw.transformTarget.localEulerAngles;
            set
            {
                Raw.changeAmount.rot = value;
                Raw.transformTarget.localEulerAngles = value;
            }
        }

        public Vector3 ScaleLocal
        {
            get => Raw.transformTarget.localScale;
            set
            {
                Raw.changeAmount.scale = value;
                Raw.transformTarget.localScale = value;
            }
        }

        public Vector3 PivotOffsetLocal => PivotGeometryUtils.GetBoundsCenterOffsetLocal(Raw.transformTarget);

        public void Commit()
        {
        }
    }
}