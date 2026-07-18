using UnityEngine;

namespace TOHYK
{
    public interface ITransformTarget
    {
        int Key { get; }

        Transform transformTarget { get; }

        bool enablePos { get; }
        bool enableRot { get; }
        bool enableScale { get; }

        Vector3 PosLocal { get; set; }
        Vector3 RotLocal { get; set; }
        Vector3 ScaleLocal { get; set; }

        Vector3 PivotOffsetLocal { get; }

        void Commit();
    }
}