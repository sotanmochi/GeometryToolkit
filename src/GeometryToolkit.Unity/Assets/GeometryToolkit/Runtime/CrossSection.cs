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
        public float SignedArea { get; private set; }
        public float Perimeter { get; private set; }

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
            var crossSection = new CrossSection(contour.Points.Count + 1, 3 * (contour.Points.Count - 1))
            {
                Perimeter = contour.Perimeter,
                SignedArea = contour.SignedArea
            };

            // Calculate centroid
            var centroid = Vector3.zero;
            for (var i = 0; i < contour.Points.Count; i++) // Avoid allocation caused by foreach loop.
            {
                centroid += contour.Points[i];
            }
            centroid /= contour.Points.Count;

            // Set vertices
            crossSection.Centroid = centroid;
            crossSection.AddVertex(Vector3.zero); // Centroid
            for (var i = 0; i < contour.Points.Count; i++) // Avoid allocation caused by foreach loop.
            {
                var relativePosition = contour.Points[i] - centroid;
                crossSection.AddVertex(relativePosition);
            }

            // Set indices
            for (int i = 0; i < contour.Points.Count - 1; i++) // Avoid allocation caused by foreach loop.
            {
                crossSection.AddTriangle(0, i + 1, i + 2); // (Centroid, Current point, Next point)
            }

            return crossSection;
        }
    }
}
