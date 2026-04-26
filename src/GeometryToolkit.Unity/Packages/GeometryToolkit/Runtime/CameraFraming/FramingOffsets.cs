using System;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// Four frustum-plane offsets in the Object Bounding Frustum local frame.
    /// </summary>
    public readonly struct FramingOffsets : IEquatable<FramingOffsets>
    {
        public readonly float Left;
        public readonly float Right;
        public readonly float Bottom;
        public readonly float Top;

        public FramingOffsets(float left, float right, float bottom, float top)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
        }

        public static FramingOffsets FromTuple((float left, float right, float bottom, float top) offsets) =>
            new(offsets.left, offsets.right, offsets.bottom, offsets.top);

        public void Deconstruct(out float left, out float right, out float bottom, out float top)
        {
            left = Left;
            right = Right;
            bottom = Bottom;
            top = Top;
        }

        public bool Equals(FramingOffsets other) =>
            Left == other.Left &&
            Right == other.Right &&
            Bottom == other.Bottom &&
            Top == other.Top;

        public override bool Equals(object obj) => obj is FramingOffsets other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Left, Right, Bottom, Top);
    }
}
