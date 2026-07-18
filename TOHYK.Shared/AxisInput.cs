namespace TOHYK
{
    public readonly struct AxisInput
    {
        public static readonly AxisInput None = new AxisInput(AxisConstraint.Free, false, false);

        public AxisConstraint Constraint { get; }
        public bool IsShiftHeld { get; }

        public bool IsAltHeld { get; }

        public AxisInput(AxisConstraint constraint, bool isShiftHeld, bool isAltHeld = false)
        {
            Constraint = constraint;
            IsShiftHeld = isShiftHeld;
            IsAltHeld = isAltHeld;
        }

        public static bool operator ==(AxisInput left, AxisInput right) =>
            left.Constraint == right.Constraint && left.IsShiftHeld == right.IsShiftHeld && left.IsAltHeld == right.IsAltHeld;
        public static bool operator !=(AxisInput left, AxisInput right) => !(left == right);

        public override bool Equals(object obj) => obj is AxisInput other && this == other;
        public override int GetHashCode() => Constraint.GetHashCode() ^ (IsShiftHeld ? 1 : 0) ^ (IsAltHeld ? 2 : 0);
    }
}
