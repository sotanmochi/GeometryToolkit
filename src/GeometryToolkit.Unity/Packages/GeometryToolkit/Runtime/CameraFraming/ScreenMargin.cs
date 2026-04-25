using System;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// Target screen-space margins for auto-framing. Each value is interpreted as either pixels or
    /// a fraction of the screen size depending on <see cref="IsPercentage"/>.
    /// </summary>
    public readonly struct ScreenMargin : IEquatable<ScreenMargin>
    {
        public readonly float Left;
        public readonly float Right;
        public readonly float Bottom;
        public readonly float Top;
        public readonly bool IsPercentage;

        public ScreenMargin(float left, float right, float bottom, float top, bool isPercentage = false)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
            IsPercentage = isPercentage;
        }

        public static ScreenMargin Pixels(float left, float right, float bottom, float top) =>
            new(left, right, bottom, top, isPercentage: false);

        public static ScreenMargin Percentage(float left, float right, float bottom, float top) =>
            new(left, right, bottom, top, isPercentage: true);

        public static ScreenMargin Uniform(float allSides, bool isPercentage = false) =>
            new(allSides, allSides, allSides, allSides, isPercentage);

        /// <summary>
        /// Convert margins to NDC bounds for the given screen size.
        /// </summary>
        /// <returns>n_l, n_r in [-1, 1] horizontally and n_b, n_t in [-1, 1] vertically.</returns>
        public (float nLeft, float nRight, float nBottom, float nTop) ToNdcBounds(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
            if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));

            float ml = IsPercentage ? Left * screenWidth : Left;
            float mr = IsPercentage ? Right * screenWidth : Right;
            float mb = IsPercentage ? Bottom * screenHeight : Bottom;
            float mt = IsPercentage ? Top * screenHeight : Top;

            return (
                nLeft: 2f * ml / screenWidth - 1f,
                nRight: 1f - 2f * mr / screenWidth,
                nBottom: 2f * mb / screenHeight - 1f,
                nTop: 1f - 2f * mt / screenHeight
            );
        }

        public bool Equals(ScreenMargin other) =>
            Left == other.Left && Right == other.Right && Bottom == other.Bottom && Top == other.Top &&
            IsPercentage == other.IsPercentage;

        public override bool Equals(object obj) => obj is ScreenMargin other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Left, Right, Bottom, Top, IsPercentage);
    }
}
