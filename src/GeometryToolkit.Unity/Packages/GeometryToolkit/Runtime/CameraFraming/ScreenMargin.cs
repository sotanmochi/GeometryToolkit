using System;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// Unit for interpreting <see cref="ScreenMargin"/> values.
    /// </summary>
    public enum ScreenMarginUnit
    {
        /// <summary>Margin as a percent (0-100) of the screen size on the corresponding axis.</summary>
        Percentage,
        /// <summary>Margin in pixels on the corresponding axis.</summary>
        Pixels,
    }

    /// <summary>
    /// Target screen-space margins for auto-framing. Each value is interpreted according to
    /// <see cref="Unit"/>: a percent (0-100) of the screen size, or pixels.
    /// </summary>
    public readonly struct ScreenMargin : IEquatable<ScreenMargin>
    {
        public readonly float Left;
        public readonly float Right;
        public readonly float Bottom;
        public readonly float Top;
        public readonly ScreenMarginUnit Unit;

        public ScreenMargin(float left, float right, float bottom, float top, ScreenMarginUnit unit)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
            Unit = unit;
        }

        /// <summary>
        /// Margin as a percent (0-100) of the screen size on each side.
        /// </summary>
        public static ScreenMargin Percentage(float left, float right, float bottom, float top) =>
            new(left, right, bottom, top, ScreenMarginUnit.Percentage);

        /// <summary>
        /// Margin in pixels on each side.
        /// </summary>
        public static ScreenMargin Pixels(float left, float right, float bottom, float top) =>
            new(left, right, bottom, top, ScreenMarginUnit.Pixels);

        public static ScreenMargin Uniform(float allSides, ScreenMarginUnit unit) =>
            new(allSides, allSides, allSides, allSides, unit);

        /// <summary>
        /// Convert margins to Normalized Device Coordinates (NDC) bounds for the given screen size.
        /// </summary>
        /// <returns>n_l, n_r in [-1, 1] horizontally and n_b, n_t in [-1, 1] vertically.</returns>
        public (float nLeft, float nRight, float nBottom, float nTop) ToNdcBounds(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
            if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));

            float ml, mr, mb, mt;
            switch (Unit)
            {
                case ScreenMarginUnit.Percentage:
                    ml = Left * 0.01f * screenWidth;
                    mr = Right * 0.01f * screenWidth;
                    mb = Bottom * 0.01f * screenHeight;
                    mt = Top * 0.01f * screenHeight;
                    break;
                case ScreenMarginUnit.Pixels:
                    ml = Left;
                    mr = Right;
                    mb = Bottom;
                    mt = Top;
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Unsupported {nameof(ScreenMarginUnit)}: {Unit}");
            }

            return (
                nLeft: 2f * ml / screenWidth - 1f,
                nRight: 1f - 2f * mr / screenWidth,
                nBottom: 2f * mb / screenHeight - 1f,
                nTop: 1f - 2f * mt / screenHeight
            );
        }

        public bool Equals(ScreenMargin other) =>
            Left == other.Left && Right == other.Right && Bottom == other.Bottom && Top == other.Top &&
            Unit == other.Unit;

        public override bool Equals(object obj) => obj is ScreenMargin other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Left, Right, Bottom, Top, Unit);
    }
}
