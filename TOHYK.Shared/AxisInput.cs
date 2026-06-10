namespace TOHYK
{
    public readonly struct AxisInput
    {
        public static readonly AxisInput None = new AxisInput(AxisConstraint.Free, false);

        public AxisConstraint Constraint { get; }
        public bool IsShiftHeld { get; }

        public AxisInput(AxisConstraint constraint, bool isShiftHeld)
        {
            Constraint = constraint;
            IsShiftHeld = isShiftHeld;
        }

        public static bool operator ==(AxisInput left, AxisInput right) => left.Constraint == right.Constraint && left.IsShiftHeld == right.IsShiftHeld;
        public static bool operator !=(AxisInput left, AxisInput right) => !(left == right);

        public override bool Equals(object obj) => obj is AxisInput other && this == other;
        public override int GetHashCode() => Constraint.GetHashCode() ^ (IsShiftHeld ? 1 : 0);
    }
}