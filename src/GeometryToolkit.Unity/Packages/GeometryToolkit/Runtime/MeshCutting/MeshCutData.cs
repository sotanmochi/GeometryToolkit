using System;
using System.Collections.Generic;
using UnityEngine;
using GeometryToolkit.Core;

namespace GeometryToolkit.MeshCutting
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
}
