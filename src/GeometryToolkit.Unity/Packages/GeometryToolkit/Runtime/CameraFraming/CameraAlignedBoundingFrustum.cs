using System;
using Unity.Collections;
using UnityEngine;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// A frustum-shaped bounding volume for a point set, defined by four side planes parallel to
    /// the reference camera's view-frustum side planes. The bounding frustum is the unique smallest
    /// frustum (similar to the view frustum) that contains the point set.
    ///
    /// Each side plane "touches" the most extreme point of the input set in the corresponding
    /// direction (left / right / bottom / top). The four offsets of these touching planes — what
    /// <see cref="ComputeFrustumPlaneOffsets"/> returns — directly determine where the camera
    /// must be placed for the input set to fit a target screen rectangle exactly.
    ///
    /// Internally stores each input point projected onto the camera-orientation basis (R, U, F) as
    /// (r, u, f) coordinates, so the four offsets can be evaluated in O(N) without re-projecting
    /// per query. The (r, u, f) values are in a frame anchored at <see cref="ReferencePoint"/>;
    /// the frame's axes are the camera transform's right / up / forward at the time of
    /// <see cref="Rebuild"/>.
    ///
    /// This class has no knowledge of <see cref="Renderer"/> types; the caller is responsible for
    /// supplying world-space vertex positions. See <see cref="IMeshVertexCollector"/> for the
    /// standard collection strategies.
    /// </summary>
    public sealed class CameraAlignedBoundingFrustum : IDisposable
    {
        private Vector3 _referencePoint;
        private Vector3 _right;
        private Vector3 _up;
        private Vector3 _forward;

        // Reusable storage buffer. Grown to the maximum required capacity and kept across
        // Rebuild calls. Always accessed through _projectedPoints (a slice view) so that the
        // valid count is encoded in NativeArray.Length.
        private NativeArray<Vector3> _projectedPointsBuffer;
        // Slice of _projectedPointsBuffer covering the vertices written by the most recent
        // Rebuild. _projectedPoints.Length is the valid count.
        private NativeArray<Vector3> _projectedPoints;

        /// <summary>
        /// Origin of the local (r, u, f) coordinate frame in which every input point is stored.
        /// </summary>
        /// <remarks>
        /// Needed for two reasons:
        ///   1. World-space recomposition: the camera-position formulas in
        ///      <see cref="AutoFramingCamera"/> return local parameters (p_r, p_u, p_f); the
        ///      camera world position is recomposed as
        ///      <c>ReferencePoint + R * p_r + U * p_u - F * p_f</c>, so the same anchor passed to
        ///      <see cref="Rebuild"/> must be retained for callers to convert back.
        ///   2. Numerical precision: shifting the reference point translates (r, u, f) and
        ///      (p_r, p_u, p_f) by the same amount, leaving the final camera position invariant.
        ///      Choosing a point near the input keeps those values small, avoiding float
        ///      precision loss when the object lies far from the world origin.
        ///
        /// Choices for the reference point (e.g. first renderer's transform position, bounding-box
        /// center, convex-hull centroid) are discussed in the design document §7.5.
        /// </remarks>
        public Vector3 ReferencePoint => _referencePoint;

        /// <summary>Right axis of the local (r, u, f) frame, captured from the camera at the time of <see cref="Rebuild"/>.</summary>
        public Vector3 Right => _right;

        /// <summary>Up axis of the local (r, u, f) frame, captured from the camera at the time of <see cref="Rebuild"/>.</summary>
        public Vector3 Up => _up;

        /// <summary>Forward axis of the local (r, u, f) frame, captured from the camera at the time of <see cref="Rebuild"/>.</summary>
        public Vector3 Forward => _forward;

        /// <summary>Number of vertices currently stored from the last <see cref="Rebuild"/>.</summary>
        public int PointCount => _projectedPoints.Length;

        /// <summary>
        /// Release the internal NativeArray buffer.
        /// </summary>
        public void Dispose()
        {
            if (_projectedPointsBuffer.IsCreated) _projectedPointsBuffer.Dispose();
            _projectedPoints = default;
        }

        /// <summary>
        /// Project the supplied world-space vertices into the local (r, u, f) frame anchored at
        /// <paramref name="referencePoint"/> and using the supplied orthonormal basis, then cache
        /// the result so subsequent <see cref="ComputeFrustumPlaneOffsets"/> calls reuse it.
        /// </summary>
        /// <param name="worldVertices">World-space vertex positions. Pass a sub-array
        /// (<see cref="NativeArray{T}.GetSubArray"/>) when the source buffer holds extra
        /// reserve capacity beyond the valid count.</param>
        /// <param name="right">R axis of the local frame (camera's right direction in world space).</param>
        /// <param name="up">U axis of the local frame (camera's up direction in world space).</param>
        /// <param name="forward">F axis of the local frame (camera's forward direction in world space).</param>
        /// <param name="referencePoint">Origin of the local (r, u, f) frame.</param>
        /// <remarks>
        /// (<paramref name="right"/>, <paramref name="up"/>, <paramref name="forward"/>) is
        /// expected to be an orthonormal basis; the algorithm assumes this without verification.
        /// </remarks>
        public void Rebuild(NativeArray<Vector3> worldVertices,
                            Vector3 right, Vector3 up, Vector3 forward,
                            Vector3 referencePoint)
        {
            int vertexCount = worldVertices.Length;

            _right = right;
            _up = up;
            _forward = forward;
            _referencePoint = referencePoint;

            EnsureBufferCapacity(vertexCount);

            for (int i = 0; i < vertexCount; i++)
            {
                var d = worldVertices[i] - _referencePoint;
                _projectedPointsBuffer[i] = new Vector3(
                    Vector3.Dot(d, _right),
                    Vector3.Dot(d, _up),
                    Vector3.Dot(d, _forward)
                );
            }
            _projectedPoints = _projectedPointsBuffer.GetSubArray(0, vertexCount);
        }

        /// <summary>
        /// Compute the offsets of the four side planes — each parallel to the corresponding
        /// view-frustum side plane and touching the most extreme point of the input set in that
        /// direction. The four offsets are exactly the values the camera position parameters must
        /// match to make the bounding frustum fit the target Normalized Device Coordinates (NDC)
        /// rectangle.
        /// </summary>
        /// <remarks>
        /// For each point P with (r, u, f) coordinates relative to <see cref="ReferencePoint"/>,
        /// the touching-plane equations for the left/right/bottom/top edges are:
        /// <code>
        ///   left   edge: r - n_l * k_h * f = p_r + n_l * k_h * p_f
        ///   right  edge: r - n_r * k_h * f = p_r + n_r * k_h * p_f
        ///   bottom edge: u - n_b * k_v * f = p_u + n_b * k_v * p_f
        ///   top    edge: u - n_t * k_v * f = p_u + n_t * k_v * p_f
        /// </code>
        /// where (p_r, p_u, p_f) parameterize the camera position in the same frame
        /// (camera position = ReferencePoint + R * p_r + U * p_u - F * p_f, with p_f &gt; 0).
        ///
        /// The most extreme point in each direction defines the touching plane offset, hence:
        /// left   = min(r - n_l * k_h * f), right = max(r - n_r * k_h * f),
        /// bottom = min(u - n_b * k_v * f), top   = max(u - n_t * k_v * f).
        /// </remarks>
        public (float left, float right, float bottom, float top) ComputeFrustumPlaneOffsets(
            float nLeft, float nRight, float nBottom, float nTop, float kHorizontal, float kVertical)
        {
            if (_projectedPoints.Length == 0)
            {
                throw new InvalidOperationException(
                    "CameraAlignedBoundingFrustum has no points. Call Rebuild() with non-empty input first.");
            }

            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity;
            float top = float.NegativeInfinity;

            for (int i = 0; i < _projectedPoints.Length; i++)
            {
                var p = _projectedPoints[i];
                float r = p.x;
                float u = p.y;
                float f = p.z;

                float leftPlaneOffset = r - nLeft * kHorizontal * f;
                float rightPlaneOffset = r - nRight * kHorizontal * f;
                float bottomPlaneOffset = u - nBottom * kVertical * f;
                float topPlaneOffset = u - nTop * kVertical * f;

                if (leftPlaneOffset < left) left = leftPlaneOffset;
                if (rightPlaneOffset > right) right = rightPlaneOffset;
                if (bottomPlaneOffset < bottom) bottom = bottomPlaneOffset;
                if (topPlaneOffset > top) top = topPlaneOffset;
            }

            return (left, right, bottom, top);
        }

        private void EnsureBufferCapacity(int requiredCapacity)
        {
            if (_projectedPointsBuffer.IsCreated && _projectedPointsBuffer.Length >= requiredCapacity) return;
            if (_projectedPointsBuffer.IsCreated) _projectedPointsBuffer.Dispose();
            _projectedPointsBuffer = new NativeArray<Vector3>(requiredCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _projectedPoints = default; // Invalidate the slice; Rebuild will recreate it after writing the new data.
        }
    }
}
