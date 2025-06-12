using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit
{
    public sealed class CrossSection
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new();

        public List<Vector3> Vertices => _vertices;
        public List<int> Triangles => _triangles;
        public Vector3 Centroid { get; private set; }

        /// <summary>
        /// The perimeter length of the cross-section.
        /// </summary>
        public float Perimeter { get; private set; }

        /// <summary>
        /// The signed area of the cross-section:
        /// - A positive value indicates that the centroid lies on the back-face (inside) of the target mesh.
        /// - A negative value indicates that the centroid lies on the front-face (outside) of the target mesh.
        /// </summary>
        public float SignedArea { get; private set; }

        private CrossSection(int vertexCapacity = 0, int triangleCapacity = 0)
        {
            _vertices = new List<Vector3>(vertexCapacity);
            _triangles = new List<int>(triangleCapacity);
        }

        private void AddVertex(Vector3 vertex)
        {
            _vertices.Add(vertex);
        }

        private void AddTriangle(int index1, int index2, int index3)
        {
            _triangles.Add(index1);
            _triangles.Add(index2);
            _triangles.Add(index3);
        }

        public static CrossSection Generate(ContourLoop contour)
        {
            var isClosedLoop = contour.Points.Count > 3 && 
                               Vector3Utils.GetHashCode(contour.Points[0]) == Vector3Utils.GetHashCode(contour.Points[^1]);
            if (!isClosedLoop)
            {
                return new CrossSection();
            }

            var pointCount = contour.Points.Count - 1; // Exclude the last point as it is a duplicate of the first point.

            var crossSection = new CrossSection(pointCount + 1, 3 * (pointCount - 1))
            {
                Perimeter = contour.Perimeter,
                SignedArea = contour.SignedArea
            };

            // Calculate centroid
            var centroid = Vector3.zero;
            for (var i = 0; i < pointCount; i++) // Avoid allocation caused by foreach loop.
            {
                centroid += contour.Points[i];
            }
            centroid /= pointCount;

            // Set vertices
            crossSection.Centroid = centroid;
            crossSection.AddVertex(Vector3.zero); // Centroid
            for (var i = 0; i < pointCount; i++) // Avoid allocation caused by foreach loop.
            {
                var relativePosition = contour.Points[i] - centroid;
                crossSection.AddVertex(relativePosition);
            }

            // Set indices
            var centroidIndex = 0; // The first vertex is the centroid.
            for (var i = 1; i <= pointCount; i++) // Avoid allocation caused by foreach loop.
            {
                var nextPoint = i % pointCount + 1; // The index of the first contour point is 1.
                crossSection.AddTriangle(centroidIndex, i, nextPoint);
            }

            return crossSection;
        }
    }
}
