using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// High-performance <see cref="IMeshVertexCollector"/> implementation that uses a Compute
    /// Shader to read each <see cref="SkinnedMeshRenderer"/>'s GPU vertex buffer directly,
    /// transforming positions to world space on the GPU. The skinning has already been performed
    /// by the rendering pipeline, so this avoids the main-thread cost of
    /// <see cref="SkinnedMeshRenderer.BakeMesh(Mesh, bool)"/> entirely.
    ///
    /// For <see cref="MeshRenderer"/>, this collector falls back to the same CPU path used by
    /// <see cref="MeshVertexCollector"/> (compute shader provides no benefit for static meshes).
    ///
    /// If the runtime does not support compute shaders, all renderer types fall back to the CPU
    /// path. Use <see cref="UsesComputeShader"/> to inspect which path is active.
    /// </summary>
    /// <remarks>
    /// Uses synchronous GPU readback (<see cref="ComputeBuffer.GetData(System.Array)"/> via a
    /// pinned <see cref="NativeArray{T}"/>) so the API stays deterministic — the camera position
    /// is computed from the current frame's vertex data with no latency. If async readback is
    /// preferred (lower main-thread cost at the cost of 1-2 frame lag), implement a separate
    /// collector following this pattern.
    /// </remarks>
    public sealed class ComputeShaderMeshVertexCollector : IMeshVertexCollector
    {
        public const string KernelName = "TransformVertexPositions";

        private readonly bool _useComputeShader;
        private readonly MeshVertexCollector _cpuFallback = new();

        private readonly ComputeShader _computeShader;
        private readonly int _kernelId;
        private readonly uint _threadGroupSizeX;

        private ComputeBuffer _outputBuffer;
        private int _outputBufferCapacity;
        // Managed array for ComputeBuffer.GetData(System.Array, ...) partial readback. Grown once
        // to the maximum required size and reused thereafter — no per-frame allocation.
        private Vector3[] _readbackArray;

        // Per-renderer cache: GraphicsBuffer is short-lived (released after each Dispatch),
        // so no persistent state needed beyond the shared output buffer.

        /// <summary>
        /// Indicates whether the runtime supports the compute shader path. When false, all
        /// renderers fall back to the CPU path (<see cref="MeshVertexCollector"/>).
        /// </summary>
        public bool UsesComputeShader => _useComputeShader;

        /// <summary>
        /// Creates an instance using the supplied compute shader. The shader must contain a
        /// kernel named <see cref="KernelName"/> matching the signature in the package's
        /// <c>CameraFramingMeshVertexTransform.compute</c> reference implementation. Assign the
        /// shader via the Inspector, or load it via
        /// <see cref="UnityEditor.AssetDatabase.LoadAssetAtPath{T}"/> / AddressableAssets /
        /// similar mechanisms. Throws at construction so per-frame
        /// <see cref="WriteWorldVertices"/> calls never raise these failures.
        /// </summary>
        /// <exception cref="ArgumentNullException">If <paramref name="computeShader"/> is null.</exception>
        /// <exception cref="ArgumentException">If the kernel cannot be found in the shader (raised by
        /// <see cref="ComputeShader.FindKernel(string)"/>).</exception>
        public ComputeShaderMeshVertexCollector(ComputeShader computeShader)
        {
            if (computeShader == null) throw new ArgumentNullException(nameof(computeShader));

            _useComputeShader = SystemInfo.supportsComputeShaders;
            if (!_useComputeShader) return;

            // Instantiate so each collector instance has its own state. SetBuffer / SetInt /
            // SetMatrix mutate the shader, so sharing a single instance across collectors
            // would race on the bindings.
            var instance = UnityEngine.Object.Instantiate(computeShader);
            try
            {
                _kernelId = instance.FindKernel(KernelName);
                instance.GetKernelThreadGroupSizes(_kernelId, out _threadGroupSizeX, out _, out _);
                _computeShader = instance;
            }
            catch
            {
                // Construction failed; release the instantiated copy so it does not leak when the
                // caller cannot reach Dispose() (the constructor never returned the object).
                UnityEngine.Object.DestroyImmediate(instance);
                throw;
            }
        }

        /// <summary>
        /// Release GPU and CPU resources.
        /// </summary>
        public void Dispose()
        {
            _outputBuffer?.Release();
            _outputBuffer = null;
            _outputBufferCapacity = 0;

            _readbackArray = null;

            if (_computeShader != null)
            {
                UnityEngine.Object.DestroyImmediate(_computeShader);
            }

            _cpuFallback.Dispose();
        }

        /// <inheritdoc />
        public int GetTotalVertexCount(IReadOnlyList<Renderer> renderers)
        {
            return _cpuFallback.GetTotalVertexCount(renderers);
        }

        /// <inheritdoc />
        public int WriteWorldVertices(IReadOnlyList<Renderer> renderers, NativeArray<Vector3> output)
        {
            if (renderers == null) return 0;
            if (!_useComputeShader)
            {
                return _cpuFallback.WriteWorldVertices(renderers, output);
            }

            int writeIndex = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;

                if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
                {
                    int written = WriteSkinnedMeshViaCompute(skinnedMeshRenderer, output, writeIndex);
                    writeIndex += written;
                }
                else
                {
                    // MeshRenderer or unsupported types: fall back to CPU path for this single renderer.
                    var singleRendererList = new[] { renderer };
                    int written = _cpuFallback.WriteWorldVertices(singleRendererList, output.GetSubArray(writeIndex, output.Length - writeIndex));
                    writeIndex += written;
                }
            }
            return writeIndex;
        }

        private int WriteSkinnedMeshViaCompute(SkinnedMeshRenderer renderer, NativeArray<Vector3> output, int offset)
        {
            int vertexCount = renderer.sharedMesh.vertexCount;
            if (vertexCount == 0) return 0;

            // Acquire the SkinnedMeshRenderer's current-pose GPU vertex buffer.
            // Returns null if the renderer hasn't been rendered yet or vertexBufferTarget is
            // misconfigured. Also fall back if rootBone is missing (we need it for the
            // local→world transform; see comment below).
            var gpuVertexBuffer = renderer.GetVertexBuffer();
            if (gpuVertexBuffer == null || renderer.rootBone == null)
            {
                var single = new[] { (Renderer)renderer };
                int written = _cpuFallback.WriteWorldVertices(single, output.GetSubArray(offset, output.Length - offset));
                return written;
            }

            try
            {
                EnsureOutputBufferCapacity(vertexCount);

                int dispatchCount = Mathf.CeilToInt(vertexCount / (float)_threadGroupSizeX);
                _computeShader.SetBuffer(_kernelId, "GpuVertexBuffer", gpuVertexBuffer);
                _computeShader.SetBuffer(_kernelId, "OutputVertexBuffer", _outputBuffer);
                _computeShader.SetInt("GpuVertexBufferStride", gpuVertexBuffer.stride);
                _computeShader.SetInt("VertexCount", vertexCount);
                // The GPU vertex buffer for a SkinnedMeshRenderer holds positions in the
                // rootBone's local frame (post-skinning). To get world positions, multiply by
                // rootBone.localToWorldMatrix. Using transform.localToWorldMatrix here would
                // produce incorrect results when the renderer's transform differs from the
                // rootBone's transform (typical for humanoid rigs).
                _computeShader.SetMatrix("LocalToWorld", renderer.rootBone.localToWorldMatrix);
                _computeShader.Dispatch(_kernelId, dispatchCount, 1, 1);

                // Synchronous readback. ComputeBuffer.GetData has no partial-read overload for
                // NativeArray, so route via a reused managed array and then NativeArray.Copy.
                EnsureReadbackArrayCapacity(vertexCount);
                _outputBuffer.GetData(_readbackArray, 0, 0, vertexCount);
                NativeArray<Vector3>.Copy(_readbackArray, 0, output, offset, vertexCount);
                return vertexCount;
            }
            finally
            {
                gpuVertexBuffer.Release();
            }
        }

        private void EnsureOutputBufferCapacity(int requiredCapacity)
        {
            if (_outputBuffer != null && _outputBufferCapacity >= requiredCapacity) return;
            _outputBuffer?.Release();
            _outputBuffer = new ComputeBuffer(requiredCapacity, sizeof(float) * 3);
            _outputBufferCapacity = requiredCapacity;
        }

        private void EnsureReadbackArrayCapacity(int requiredCapacity)
        {
            if (_readbackArray != null && _readbackArray.Length >= requiredCapacity) return;
            _readbackArray = new Vector3[requiredCapacity];
        }
    }
}
