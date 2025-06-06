using System;
using UnityEngine;

namespace GeometryToolkit
{
    public sealed class MeshDataBuffer
    {
        private readonly Vector3[] _vertices;
        private readonly int[] _triangles;

        public Vector3[] Vertices => _vertices;
        public int[] Triangles => _triangles;
        public Matrix4x4 LocalToWorldMatrix { get; private set; } = Matrix4x4.identity;

        public MeshDataBuffer(int vertexBufferSize, int triangleBufferSize)
        {
            _vertices = new Vector3[vertexBufferSize];
            _triangles = new int[triangleBufferSize];
        }

        public static MeshDataBuffer Create(Mesh mesh, Transform meshTransform)
        {
            if (mesh == null || meshTransform == null)
            {
                throw new ArgumentException("The mesh or meshTransform is null.");
            }

            var vertices = mesh.vertices;
            var triangles = mesh.triangles;

            var target = new MeshDataBuffer(vertices.Length, triangles.Length);
            target.LocalToWorldMatrix = meshTransform.localToWorldMatrix;
            Array.Copy(triangles, target.Triangles, triangles.Length);
            Array.Copy(vertices, target.Vertices, vertices.Length);

            return target;
        }
    }
}
