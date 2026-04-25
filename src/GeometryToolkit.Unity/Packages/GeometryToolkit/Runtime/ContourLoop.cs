using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit
{
    public sealed class ContourLoop
    {
        private readonly List<Vector3> _points;

        private float _perimeter = 0f;
        private float _signedArea = 0f;

        /// <summary>
        /// The ordered list of points that define the contour loop.
        /// The first point equals the last point, indicating the loop is closed.
        /// </summary>
        public IReadOnlyList<Vector3> Points => _points;

        /// <summary>
        /// The perimeter length of the contour loop.
        /// This value is calculated only when the loop is closed.
        /// </summary>
        public float Perimeter => _perimeter;

        /// <summary>
        /// The signed area of the contour loop:
        /// - A positive value indicates that the centroid lies on the back-face (inside) of the target mesh.
        /// - A negative value indicates that the centroid lies on the front-face (outside) of the target mesh.
        /// </summary>
        public float SignedArea => _signedArea;

        public ContourLoop(int capacity = 0)
        {
            _points = new List<Vector3>(capacity);
        }

        public void AddPoint(Vector3 point)
        {
            _points.Add(point);
        }

        public void UpdatePerimeter()
        {
            var isClosedLoop = _points.Count > 3 && Vector3Utils.GetHashCode(_points[0]) == Vector3Utils.GetHashCode(_points[^1]);
            if (!isClosedLoop) return;

            var perimeter = 0f;
            for (var i = 0; i < _points.Count - 1; i++)
            {
                perimeter += Vector3.Distance(_points[i], _points[i + 1]);
            }

            _perimeter = perimeter;
        }

        public void UpdateSignedArea(Vector3 cuttingPlaneNormal)
        {
            var isClosedLoop = _points.Count > 3 && Vector3Utils.GetHashCode(_points[0]) == Vector3Utils.GetHashCode(_points[^1]);
            if (!isClosedLoop) return;

            var pointCount = _points.Count - 1; // Exclude the last point as it is a duplicate of the first point.

            var centroid = Vector3.zero;
            for (var i = 0; i < pointCount; i++)
            {
                centroid += _points[i];
            }
            centroid /= pointCount;

            var signedArea = 0f;
            for (var i = 0; i < pointCount; i++)
            {
                var p1 = _points[i];
                var p2 = _points[i + 1];
                var crossProduct = Vector3.Cross(p1 - centroid, p2 - centroid);
                signedArea += 0.5f * Vector3.Dot(crossProduct, cuttingPlaneNormal);
            }

            _signedArea = signedArea;
        }
    }
}
