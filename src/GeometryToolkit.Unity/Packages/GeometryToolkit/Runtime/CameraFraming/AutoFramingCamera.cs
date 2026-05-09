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
    /// Algorithm: build a <see cref="CameraAlignedBoundingFrustum"/> over the input vertices for the
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
    public sealed class AutoFramingCamera : IDisposable
    {
        private readonly CameraAlignedBoundingFrustum _boundingFrustum = new();
        private readonly IMeshVertexCollector _vertexCollector;
        private readonly bool _ownsVertexCollector;
        private NativeArray<Vector3> _worldVertexBuffer;

        public CameraAlignedBoundingFrustum BoundingFrustum => _boundingFrustum;
        public IMeshVertexCollector VertexCollector => _vertexCollector;

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

            var referencePoint = renderers[0] != null ? renderers[0].transform.position : Vector3.zero;

            // Pass only the valid prefix (writes may stop short of capacity if some renderers were unsupported / null).
            return ComputeCameraPosition(camera, _worldVertexBuffer.GetSubArray(0, written),
                                         referencePoint, margin, screenWidth, screenHeight);
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
            return ComputeCameraPositionFromBoundingFrustum(screenWidth, screenHeight, camera.fieldOfView, margin);
        }

        /// <summary>
        /// Compute the camera position using a previously built
        /// <see cref="CameraAlignedBoundingFrustum"/>.
        /// Useful when the bounding frustum is reused across multiple margin queries (e.g. when
        /// scrubbing through preset margins).
        /// </summary>
        public Vector3 ComputeCameraPositionFromBoundingFrustum(
            int screenWidth, int screenHeight, float verticalFieldOfView, ScreenMargin margin)
        {
            if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
            if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
            if (verticalFieldOfView <= 0f || verticalFieldOfView >= 180f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(verticalFieldOfView), verticalFieldOfView,
                    "The value of vertical field of view (degrees) must be in the open range (0, 180).");
            }

            var (nLeft, nRight, nBottom, nTop) = margin.ToNdcBounds(screenWidth, screenHeight);
            float horizontalSpan = nRight - nLeft;
            float verticalSpan = nTop - nBottom;
            const float MinSpan = 1e-4f;
            if (horizontalSpan < MinSpan || verticalSpan < MinSpan)
            {
                throw new ArgumentException(
                    "ScreenMargin leaves no usable area; (nRight - nLeft) and (nTop - nBottom) must be positive.");
            }

            float fovYRad = verticalFieldOfView * Mathf.Deg2Rad;
            float kVertical = Mathf.Tan(fovYRad * 0.5f);
            float aspectRatio = screenWidth / (float) screenHeight;
            float kHorizontal = kVertical * aspectRatio;

            var (left, right, bottom, top) = _boundingFrustum.ComputeFrustumPlaneOffsets(
                nLeft, nRight, nBottom, nTop, kHorizontal, kVertical);

            // Closed-form depth per axis (see CameraAlignedBoundingFrustum.ComputeFrustumPlaneOffsets
            // remarks for the touching-plane equations).
            float pfHorizontal = (right - left) / (horizontalSpan * kHorizontal);
            float pfVertical = (top - bottom) / (verticalSpan * kVertical);

            // Take the more restrictive depth so neither axis overflows the requested margins.
            float pf = Mathf.Max(pfHorizontal, pfVertical);

            // For the axis that became slack (smaller pf), recenter so the slack is shared
            // between the two sides instead of pinning to one edge.
            float pr = HorizontalIsTight(pfHorizontal, pfVertical)
                ? left - nLeft * kHorizontal * pf
                : ((left - nLeft * kHorizontal * pf) + (right - nRight * kHorizontal * pf)) * 0.5f;

            float pu = !HorizontalIsTight(pfHorizontal, pfVertical)
                ? bottom - nBottom * kVertical * pf
                : ((bottom - nBottom * kVertical * pf) + (top - nTop * kVertical * pf)) * 0.5f;

            // Camera world position = reference + R * pr + U * pu - F * pf.
            // The -F term places the camera "behind" the reference along the forward axis,
            // so the bounded points lie in front of the camera (positive depth).
            return _boundingFrustum.ReferencePoint
                   + _boundingFrustum.Right * pr
                   + _boundingFrustum.Up * pu
                   - _boundingFrustum.Forward * pf;
        }

        private void EnsureWorldVertexBufferCapacity(int requiredCapacity)
        {
            if (_worldVertexBuffer.IsCreated && _worldVertexBuffer.Length >= requiredCapacity) return;
            if (_worldVertexBuffer.IsCreated) _worldVertexBuffer.Dispose();
            _worldVertexBuffer = new NativeArray<Vector3>(requiredCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static bool HorizontalIsTight(float pfHorizontal, float pfVertical)
            => pfHorizontal >= pfVertical;
    }
}
