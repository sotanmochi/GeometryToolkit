using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace GeometryToolkit
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class WireframeGrid : MonoBehaviour
    {
        [SerializeField, Range(1, 100)] private int _size = 1;

        private MeshFilter _meshFilter;

        void Awake()
        {
            _size = (_size <= 0) ? 1 : _size;
            GenerateMesh();
        }

        void OnValidate()
        {
            if (Application.isEditor)
            {
                GenerateMesh();
            }
        }

        private void GenerateMesh()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            // Destroy old mesh
            if (_meshFilter.sharedMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_meshFilter.sharedMesh);
                }
                else
                {
                    DestroyImmediate(_meshFilter.sharedMesh);
                }
            }

            // Generate new mesh
            var mesh = new Mesh();
            mesh.name = "WireframeGrid";
            SetVertices(mesh, _size);
            SetIndices(mesh, _size);
            _meshFilter.sharedMesh = mesh;
        }

        private void SetVertices(Mesh mesh, int size)
        {
            var vertices = new List<Vector3>();
            var n = 2 * size + 1;

            for (var row = 0; row < n; row++)
            {
                var z = math.remap(0, n - 1, -size, size, row);

                for (var column = 0; column < n; column++)
                {
                    var x = math.remap(0, n - 1, -size, size, column);
                    vertices.Add(new Vector3(x, 0f, z));
                }
            }

            mesh.SetVertices(vertices);
        }

        private void SetIndices(Mesh mesh, int size)
        {
            var indices = new List<int>();
            var n = 2 * size + 1;

            // Row lines
            var index = 0;
            for (var row = 0; row < n; row++)
            {
                for (var column = 0; column < n - 1; column++)
                {
                    indices.Add(index);
                    indices.Add(index + 1);
                    index++;
                }
                index++;
            }

            // Column lines
            index = 0;
            for (var column = 0; column < n; column++)
            {
                for (var row = 0; row < n - 1; row++)
                {
                    indices.Add(index);
                    indices.Add(index + n);
                    index++;
                }
            }

            mesh.SetIndices(indices, MeshTopology.Lines, 0);
        }
    }
}
