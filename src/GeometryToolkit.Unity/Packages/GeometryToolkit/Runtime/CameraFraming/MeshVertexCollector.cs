using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// Default <see cref="IMeshVertexCollector"/> implementation: reads vertex positions on the CPU
    /// using <see cref="Mesh.AcquireReadOnlyMeshData(Mesh)"/> and writes them into a
    /// <see cref="NativeArray{T}"/> output, avoiding the per-frame array allocation that
    /// <c>mesh.vertices</c> would cause.
    ///
    /// Supports both <see cref="MeshRenderer"/> (uses the shared mesh) and
    /// <see cref="SkinnedMeshRenderer"/> (snapshots the current pose via
    /// <see cref="SkinnedMeshRenderer.BakeMesh(Mesh, bool)"/> with <c>useScale: false</c>).
    /// Other renderer types are silently skipped.
    /// </summary>
    public sealed class MeshVertexCollector : IMeshVertexCollector
    {
        private NativeArray<Vector3> _localVertexBuffer;
        private Mesh _bakeMeshBuffer;

        /// <summary>
        /// Release the internal NativeArray buffer and bake-target Mesh.
        /// </summary>
        public void Dispose()
        {
            if (_localVertexBuffer.IsCreated) _localVertexBuffer.Dispose();
            if (_bakeMeshBuffer != null)
            {
                Object.DestroyImmediate(_bakeMeshBuffer);
                _bakeMeshBuffer = null;
            }
        }

        /// <inheritdoc />
        public int GetTotalVertexCount(IReadOnlyList<Renderer> renderers)
        {
            if (renderers == null) return 0;
            int total = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                total += GetSourceVertexCount(renderers[i]);
            }
            return total;
        }

        /// <inheritdoc />
        public int WriteWorldVertices(IReadOnlyList<Renderer> renderers, NativeArray<Vector3> output)
        {
            if (renderers == null) return 0;

            int writeIndex = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;

                var mesh = ResolveMeshForCurrentPose(renderer);
                if (mesh == null) continue;

                int vertexCount = mesh.vertexCount;
                if (vertexCount == 0) continue;

                EnsureLocalBufferCapacity(vertexCount);
                using (var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var slice = _localVertexBuffer.GetSubArray(0, vertexCount);
                    meshDataArray[0].GetVertices(slice);
                }

                var localToWorld = renderer.transform.localToWorldMatrix;
                for (int j = 0; j < vertexCount; j++)
                {
                    output[writeIndex + j] = localToWorld.MultiplyPoint3x4(_localVertexBuffer[j]);
                }
                writeIndex += vertexCount;
            }
            return writeIndex;
        }

        private static int GetSourceVertexCount(Renderer renderer)
        {
            if (renderer is MeshRenderer meshRenderer)
            {
                var sharedMesh = meshRenderer.GetComponent<MeshFilter>()?.sharedMesh;
                return sharedMesh != null ? sharedMesh.vertexCount : 0;
            }
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                var sharedMesh = skinnedMeshRenderer.sharedMesh;
                return sharedMesh != null ? sharedMesh.vertexCount : 0;
            }
            return 0;
        }

        private Mesh ResolveMeshForCurrentPose(Renderer renderer)
        {
            if (renderer is MeshRenderer meshRenderer)
            {
                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                return meshFilter != null ? meshFilter.sharedMesh : null;
            }
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
            {
                if (_bakeMeshBuffer == null)
                {
                    _bakeMeshBuffer = new Mesh { name = "MeshVertexCollector.BakeBuffer" };
                    _bakeMeshBuffer.hideFlags = HideFlags.HideAndDontSave;
                    _bakeMeshBuffer.MarkDynamic();
                }
                // useScale: false avoids double-applying scale; the caller multiplies the baked
                // local-space vertices by renderer.transform.localToWorldMatrix (which already
                // includes scale) to obtain world-space positions.
                skinnedMeshRenderer.BakeMesh(_bakeMeshBuffer, useScale: false);
                return _bakeMeshBuffer;
            }
            return null;
        }

        private void EnsureLocalBufferCapacity(int requiredCapacity)
        {
            if (_localVertexBuffer.IsCreated && _localVertexBuffer.Length >= requiredCapacity) return;
            if (_localVertexBuffer.IsCreated) _localVertexBuffer.Dispose();
            _localVertexBuffer = new NativeArray<Vector3>(requiredCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
    }
}
