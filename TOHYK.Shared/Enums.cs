namespace TOHYK
{
    public enum TransformMode
    {
        None,
        Move,
        Rotate,
        Scale,

        Mirror
    }

    public enum AxisConstraint
    {
        Free,
        AxisX,
        AxisY,
        AxisZ,
        PlaneXY,
        PlaneXZ,
        PlaneYZ,
        CameraForward
    }

    public enum ConstraintSpace
    {
        Global,
        Local
    }

    public enum PivotMode
    {
        MedianPoint,
        ActiveElement,
        IndividualOrigins,

        BoundingBoxCenter,

        AccessoryParent,
    }
}
