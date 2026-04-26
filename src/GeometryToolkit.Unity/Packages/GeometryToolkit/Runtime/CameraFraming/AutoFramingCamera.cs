using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// Computes a camera world position that frames the given renderers within the requested
    /// screen-space margins, given a fixed camera orientation.
    ///
    /// Algorithm: build an <see cref="ObjectBoundingFrustum"/> over the input vertices for the
    /// given camera orientation, evaluate the four frustum-plane offsets (one per Normalized
    /// Device Coordinates (NDC) edge) for the requested bounds, then compute the camera position
    /// directly from those offsets via fixed formulas (no iteration). One axis tightly fits the
    /// requested margins; the other axis is slack and re-centered.
    ///
    /// Vertex acquisition is delegated to an <see cref="IMeshVertexCollector"/> implementation
    /// (default: <see cref="MeshVertexCollector"/> using zero-allocation CPU readback). Callers
    /// can substitute <see cref="ComputeShaderMeshVertexCollector"/> for GPU-side collection on
    /// compute-shader-capable hardware, or supply their own implementation.
    /// </summary>
    /// <remarks>
    /// The pipeline is exposed both as the convenience entry point
    /// <see cref="ComputeCameraPosition(Camera, IReadOnlyList{Renderer}, ScreenMargin, int, int)"/>
    /// (single call) and as the decomposed pair
    /// <see cref="ComputeFrustumPlaneOffsets(Camera, IReadOnlyList{Renderer}, ScreenMargin, int, int)"/>
    /// + <see cref="RecomposeCameraPosition(Camera, float, float, float, float, ScreenMargin, int, int)"/>.
    /// The decomposed form lets callers operate on the four offsets between the two steps —
    /// e.g. apply temporal smoothing for video-capture / live use cases.
    /// </remarks>
    public sealed class AutoFramingCamera : IDisposable
    {
        private readonly ObjectBoundingFrustum _boundingFrustum = new();
        private readonly IMeshVertexCollector _vertexCollector;
        private readonly bool _ownsVertexCollector;
        private NativeArray<Vector3> _worldVertexBuffer;
        private int _worldVertexCount;

        public ObjectBoundingFrustum BoundingFrustum => _boundingFrustum;
        public IMeshVertexCollector VertexCollector => _vertexCollector;

        /// <summary>
        /// Read-only view of the world-space vertices collected during the most recent
        /// <see cref="BuildBoundingFrustum(Camera, IReadOnlyList{Renderer})"/> /
        /// <see cref="ComputeFrustumPlaneOffsets(Camera, IReadOnlyList{Renderer}, ScreenMargin, int, int)"/>
        /// call. Empty <see cref="NativeArray{T}"/> if no build has run yet. Useful for
        /// debug visualization (e.g. projecting these to screen space to draw the actual
        /// subject footprint over the framing margin).
        /// </summary>
        public NativeArray<Vector3> WorldVertices =>
            _worldVertexBuffer.IsCreated && _worldVertexCount > 0
                ? _worldVertexBuffer.GetSubArray(0, _worldVertexCount)
                : default;

        /// <summary>
        /// Creates an instance that owns and disposes a default <see cref="MeshVertexCollector"/>.
        /// </summary>
        public AutoFramingCamera() : this(new MeshVertexCollector(), ownsVertexCollector: true)
        {
        }

        /// <summary>
        /// Creates an instance using the supplied vertex collector. The caller retains ownership
        /// and is responsible for disposing the collector unless <paramref name="ownsVertexCollector"/>
        /// is true.
        /// </summary>
        public AutoFramingCamera(IMeshVertexCollector vertexCollector, bool ownsVertexCollector = false)
        {
            _vertexCollector = vertexCollector ?? throw new ArgumentNullException(nameof(vertexCollector));
            _ownsVertexCollector = ownsVertexCollector;
        }

        /// <summary>
        /// Release internal NativeArray and (optionally) the owned vertex collector.
        /// </summary>
        public void Dispose()
        {
            _boundingFrustum.Dispose();
            if (_ownsVertexCollector) _vertexCollector.Dispose();
            if (_worldVertexBuffer.IsCreated) _worldVertexBuffer.Dispose();
        }

        /// <summary>
        /// Convenience overload: collects world-space vertices from the renderers using the
        /// configured <see cref="IMeshVertexCollector"/>, then computes the camera position.
        /// The reference point is set to the first renderer's transform position.
        /// </summary>
        public Vector3 ComputeCameraPosition(
            Camera camera,
            IReadOnlyList<Renderer> renderers,
            ScreenMargin margin,
            int screenWidth,
            int screenHeight)
        {
            BuildBoundingFrustum(camera, renderers);
            return ComputeCameraPositionFromBoundingFrustum(camera, margin, screenWidth, screenHeight);
        }

        /// <summary>
        /// Core overload: takes a pre-collected NativeArray of world-space vertices. Use this when
        /// the caller wants to bypass the standard vertex collection (e.g. supply pre-baked convex
        /// hull points, custom GPU-derived data, or vertices from a non-Renderer source).
        /// If the caller's source buffer holds extra reserve capacity beyond the valid count,
        /// pass a sub-array via <see cref="NativeArray{T}.GetSubArray"/>.
        /// </summary>
        public Vector3 ComputeCameraPosition(
            Camera camera,
            NativeArray<Vector3> worldVertices,
            Vector3 referencePoint,
            ScreenMargin margin,
            int screenWidth,
            int screenHeight)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (worldVertices.Length == 0)
            {
                throw new ArgumentException("worldVertices must not be empty.", nameof(worldVertices));
            }

            var t = camera.transform;
            _boundingFrustum.Rebuild(worldVertices, t.right, t.up, t.forward, referencePoint);
            return ComputeCameraPositionFromBoundingFrustum(camera, margin, screenWidth, screenHeight);
        }

        /// <summary>
        /// Compute the camera position using a previously built <see cref="BoundingFrustum"/>.
        /// Useful when the bounding frustum is reused across multiple margin queries (e.g. when
        /// scrubbing through preset margins).
        /// </summary>
        public Vector3 ComputeCameraPositionFromBoundingFrustum(
            Camera camera, ScreenMargin margin, int screenWidth, int screenHeight)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var p = ComputeProjectionParameters(camera, margin, screenWidth, screenHeight);
            var (left, right, bottom, top) = _boundingFrustum.ComputeFrustumPlaneOffsets(
                p.nLeft, p.nRight, p.nBottom, p.nTop, p.kHorizontal, p.kVertical);
            return RecomposeFromOffsets(in p, left, right, bottom, top);
        }

        /// <summary>
        /// Recompose the camera world position from arbitrary frustum-plane offsets, using the
        /// currently cached <see cref="BoundingFrustum"/> basis (built by a prior
        /// <see cref="BuildBoundingFrustum"/> / <see cref="ComputeFrustumPlaneOffsets"/> call).
        /// Pass smoothed or otherwise post-processed offsets here to obtain the corresponding
        /// camera position via the same closed-form formulas used by
        /// <see cref="ComputeCameraPosition(Camera, IReadOnlyList{Renderer}, ScreenMargin, int, int)"/>.
        /// </summary>
        public Vector3 RecomposeCameraPosition(
            Camera camera,
            float left, float right, float bottom, float top,
            ScreenMargin margin,
            int screenWidth,
            int screenHeight)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var p = ComputeProjectionParameters(camera, margin, screenWidth, screenHeight);
            // return RecomposeFromOffsets(in p, p.left, p.right, p.bottom, p.top); // WIP
            return RecomposeFromOffsets(in p, left, right, bottom, top);
        }

        /// <summary>
        /// Build the internal <see cref="BoundingFrustum"/> from the given renderers, using the
        /// configured <see cref="IMeshVertexCollector"/>. The bounding frustum is then available
        /// via <see cref="BoundingFrustum"/> for subsequent offset / position queries.
        /// </summary>
        public void BuildBoundingFrustum(Camera camera, IReadOnlyList<Renderer> renderers)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            if (renderers.Count == 0) throw new ArgumentException("renderers must not be empty.", nameof(renderers));

            int totalVertexCount = _vertexCollector.GetTotalVertexCount(renderers);
            if (totalVertexCount == 0)
            {
                throw new InvalidOperationException(
                    "No supported renderers found in the input list (no vertices to frame).");
            }

            EnsureWorldVertexBufferCapacity(totalVertexCount);
            int written = _vertexCollector.WriteWorldVertices(renderers, _worldVertexBuffer);
            _worldVertexCount = written;

            var referencePoint = renderers[0] != null ? renderers[0].transform.position : Vector3.zero;
            var t = camera.transform;
            // Pass only the valid prefix (writes may stop short of capacity if some renderers were unsupported / null).
            _boundingFrustum.Rebuild(_worldVertexBuffer.GetSubArray(0, written),
                                     t.right, t.up, t.forward, referencePoint);
        }

        /// <summary>
        /// Build the bounding frustum from the renderers and return the four frustum-plane
        /// offsets (left / right / bottom / top) in the OBF's local (r, u, f) frame.
        /// Use this entry when the caller wants to operate on the offsets between collection
        /// and recomposition — e.g. apply per-axis temporal smoothing before deriving the
        /// final camera position via <see cref="RecomposeCameraPosition"/>.
        /// </summary>
        public (float left, float right, float bottom, float top) ComputeFrustumPlaneOffsets(
            Camera camera,
            IReadOnlyList<Renderer> renderers,
            ScreenMargin margin,
            int screenWidth,
            int screenHeight)
        {
            BuildBoundingFrustum(camera, renderers);
            var p = ComputeProjectionParameters(camera, margin, screenWidth, screenHeight);
            return _boundingFrustum.ComputeFrustumPlaneOffsets(
                p.nLeft, p.nRight, p.nBottom, p.nTop, p.kHorizontal, p.kVertical);
        }

        private void EnsureWorldVertexBufferCapacity(int requiredCapacity)
        {
            if (_worldVertexBuffer.IsCreated && _worldVertexBuffer.Length >= requiredCapacity) return;
            if (_worldVertexBuffer.IsCreated) _worldVertexBuffer.Dispose();
            _worldVertexBuffer = new NativeArray<Vector3>(requiredCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static ProjectionParameters ComputeProjectionParameters(
            Camera camera, ScreenMargin margin, int screenWidth, int screenHeight)
        {
            var (nLeft, nRight, nBottom, nTop) = margin.ToNdcBounds(screenWidth, screenHeight);
            float horizontalSpan = nRight - nLeft;
            float verticalSpan = nTop - nBottom;
            const float MinSpan = 1e-4f;
            if (horizontalSpan < MinSpan || verticalSpan < MinSpan)
            {
                throw new ArgumentException(
                    "ScreenMargin leaves no usable area; (nRight - nLeft) and (nTop - nBottom) must be positive.");
            }

            float fovYRad = camera.fieldOfView * Mathf.Deg2Rad;
            float kVertical = Mathf.Tan(fovYRad * 0.5f);
            float kHorizontal = kVertical * camera.aspect;

            return new ProjectionParameters
            {
                nLeft = nLeft, nRight = nRight, nBottom = nBottom, nTop = nTop,
                kHorizontal = kHorizontal, kVertical = kVertical,
                horizontalSpan = horizontalSpan, verticalSpan = verticalSpan,
            };
        }

        // Closed-form depth per axis (see ObjectBoundingFrustum.ComputeFrustumPlaneOffsets remarks
        // for the touching-plane equations). The axis with the larger pf is the tight one — it
        // anchors to its NDC edges; the other axis is recentered so the slack is shared.
        private Vector3 RecomposeFromOffsets(in ProjectionParameters p,
                                             float left, float right, float bottom, float top)
        {
            float pfHorizontal = (right - left) / (p.horizontalSpan * p.kHorizontal);
            float pfVertical = (top - bottom) / (p.verticalSpan * p.kVertical);

            // Take the more restrictive depth so neither axis overflows the requested margins.
            float pf = Mathf.Max(pfHorizontal, pfVertical);

            bool horizontalIsTight = pfHorizontal >= pfVertical;

            float pr = horizontalIsTight
                ? left - p.nLeft * p.kHorizontal * pf
                : ((left - p.nLeft * p.kHorizontal * pf) + (right - p.nRight * p.kHorizontal * pf)) * 0.5f;

            float pu = !horizontalIsTight
                ? bottom - p.nBottom * p.kVertical * pf
                : ((bottom - p.nBottom * p.kVertical * pf) + (top - p.nTop * p.kVertical * pf)) * 0.5f;

            // Camera world position = reference + R * pr + U * pu - F * pf.
            // The -F term places the camera "behind" the reference along the forward axis,
            // so the bounded points lie in front of the camera (positive depth).
            return _boundingFrustum.ReferencePoint
                   + _boundingFrustum.Right * pr
                   + _boundingFrustum.Up * pu
                   - _boundingFrustum.Forward * pf;
        }

        private struct ProjectionParameters
        {
            public float nLeft, nRight, nBottom, nTop;
            public float kHorizontal, kVertical;
            public float horizontalSpan, verticalSpan;
        }
    }
}
