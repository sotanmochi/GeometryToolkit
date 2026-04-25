using System;
using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit
{
    public sealed class MeshCutData
    {
        private readonly Dictionary<int, int> _pointHashToIndexMap = new();
        private readonly List<Vector3> _intersectionPoints = new();
        private readonly HashSet<LineSegment> _intersectionLines = new();

        public Plane CuttingPlane { get; }
        public IReadOnlyList<Vector3> IntersectionPoints => _intersectionPoints;
        public HashSet<LineSegment> IntersectionLines => _intersectionLines;

        public MeshCutData(Plane cuttingPlane)
        {
            CuttingPlane = cuttingPlane;
        }

        public int AddIntersectionPoint(Vector3 point)
        {
            var hashCode = Vector3Utils.GetHashCode(point);

            if (!_pointHashToIndexMap.TryGetValue(hashCode, out var index))
            {
                _intersectionPoints.Add(point);
                index = _intersectionPoints.Count - 1;
                _pointHashToIndexMap.Add(hashCode, index);
            }

            return index;
        }

        public void AddIntersectionLine(LineSegment line)
        {
            if (line.PointId1 < 0 || line.PointId1 >= _intersectionPoints.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(line.PointId1), "The value must be valid index in the point list.");
            }
            if (line.PointId2 < 0 || line.PointId2 >= _intersectionPoints.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(line.PointId2), "The value must be valid index in the point list.");
            }

            if (line.PointId1 == line.PointId2) return; // Avoid to add lines between the same point.
            _intersectionLines.Add(line);
        }
    }

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

    public static class Vector3Utils
    {
        public const float Epsilon = 0.00001f;

        private const float QuantizationFactor = 100000; // 1f / Epsilon

        public static int GetHashCode(Vector3 v)
        {
            int x = (int)(v.x * QuantizationFactor);
            int y = (int)(v.y * QuantizationFactor);
            int z = (int)(v.z * QuantizationFactor);
            return HashCode.Combine(x, y, z);
        }
    }
}
