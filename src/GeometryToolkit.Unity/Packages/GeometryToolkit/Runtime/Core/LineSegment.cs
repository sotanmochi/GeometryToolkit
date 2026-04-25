using System;

namespace GeometryToolkit.Core
{
    public readonly struct LineSegment : IEquatable<LineSegment>
    {
        public readonly int PointId1;
        public readonly int PointId2;

        public LineSegment(int pointId1, int pointId2)
        {
            PointId1 = pointId1;
            PointId2 = pointId2;
        }

        public bool Equals(LineSegment other)
        {
            return (PointId1 == other.PointId1 && PointId2 == other.PointId2) ||
                   (PointId1 == other.PointId2 && PointId2 == other.PointId1);
        }

        public override bool Equals(object obj)
        {
            return obj is LineSegment other && Equals(other);
        }

        public override int GetHashCode()
        {
            var max = Math.Max(PointId1, PointId2);
            var min = Math.Min(PointId1, PointId2);
            return HashCode.Combine(min, max);
        }
    }
}
