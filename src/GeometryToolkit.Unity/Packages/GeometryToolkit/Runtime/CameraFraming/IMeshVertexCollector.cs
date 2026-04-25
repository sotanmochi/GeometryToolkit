using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace GeometryToolkit.CameraFraming
{
    public interface IMeshVertexCollector : IDisposable
    {
        /// <summary>
        /// Returns the total number of vertices that <see cref="WriteWorldVertices"/> will produce
        /// across all renderers in the input list. Used by callers to size their world-vertex buffer
        /// before invoking the writer.
        /// </summary>
        int GetTotalVertexCount(IReadOnlyList<Renderer> renderers);

        /// <summary>
        /// Writes the world-space vertex positions for every supported renderer in
        /// <paramref name="renderers"/> sequentially into <paramref name="output"/>, starting at
        /// index 0. Returns the total number of vertices actually written. Renderer types not
        /// supported by this collector are silently skipped.
        /// </summary>
        /// <remarks>
        /// Implementations may batch GPU work internally (e.g. dispatch a single compute shader
        /// readback for all SkinnedMeshRenderers) for performance, so callers should prefer this
        /// bulk method over invoking per-renderer collection in a loop.
        /// </remarks>
        int WriteWorldVertices(IReadOnlyList<Renderer> renderers, NativeArray<Vector3> output);
    }
}
