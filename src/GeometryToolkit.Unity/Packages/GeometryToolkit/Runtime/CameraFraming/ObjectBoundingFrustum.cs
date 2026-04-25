using System.Collections.Generic;
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
    /// (r, u, f) coordinates, so the four offsets can be evaluated in O(N) without re-traversing
    /// the meshes per query. The (r, u, f) values are in a frame anchored at
    /// <see cref="ReferencePoint"/>; the frame's axes are the camera transform's right / up /
    /// forward at the time of <see cref="Rebuild"/>.
    /// </summary>
    public sealed class ObjectBoundingFrustum
    {
        private Vector3 _referencePoint;
        private Vector3 _right;
        private Vector3 _up;
        private Vector3 _forward;
        private Vector3[] _projectedPoints;
        private int _projectedPointCount;
        private Mesh _bakeMeshBuffer;

        /// <summary>
        /// Origin of the local (r, u, f) coordinate frame in which every input point is stored.
        /// After <see cref="Rebuild"/>, each projected point represents
        /// <c>(world_pos - ReferencePoint)</c> projected onto (<see cref="Right"/>,
        /// <see cref="Up"/>, <see cref="Forward"/>).
        /// </summary>
        /// <remarks>
        /// Needed for two reasons:
        ///   1. World-space recomposition: the camera-position formulas in
        ///      <see cref="AutoFramingCamera"/> return local parameters (p_r, p_u, p_f); the
        ///      camera world position is recomposed as
        ///      <c>ReferencePoint + R * p_r + U * p_u - F * p_f</c>, so the same anchor used
        ///      during <see cref="Rebuild"/> must be retained for callers to convert back.
        ///   2. Numerical precision: shifting the reference point translates (r, u, f) and
        ///      (p_r, p_u, p_f) by the same amount, leaving the final camera position invariant.
        ///      Choosing a point near the input keeps those values small, avoiding float
        ///      precision loss when the object lies far from the world origin.
        ///
        /// <see cref="Rebuild"/> currently uses the first renderer's transform position. Other
        /// choices (bounding-box center, convex-hull centroid, explicit user-supplied point) are
        /// discussed in the design document §7.5.
        /// </remarks>
        public Vector3 ReferencePoint => _referencePoint;

        /// <summary>Right axis of the local (r, u, f) frame, captured from the camera at the time of <see cref="Rebuild"/>.</summary>
        public Vector3 Right => _right;

        /// <summary>Up axis of the local (r, u, f) frame, captured from the camera at the time of <see cref="Rebuild"/>.</summary>
        public Vector3 Up => _up;

        /// <summary>Forward axis of the local (r, u, f) frame, captured from the camera at the time of <see cref="Rebuild"/>.</summary>
        public Vector3 Forward => _forward;

        /// <summary>Number of vertices currently stored from the last <see cref="Rebuild"/>.</summary>
        public int PointCount => _projectedPointCount;

        /// <summary>
        /// Collect vertices from the given renderers and project them onto the camera's
        /// orientation basis. Subsequent <see cref="ComputeFrustumPlaneOffsets"/> calls reuse
        /// these projections without re-traversing the meshes.
        ///
        /// Supports both <see cref="MeshRenderer"/> (uses the shared mesh) and
        /// <see cref="SkinnedMeshRenderer"/> (snapshots the current pose via
        /// <see cref="SkinnedMeshRenderer.BakeMesh(Mesh, bool)"/> with <c>useScale: false</c>).
        /// Other renderer types are silently skipped.
        /// </summary>
        public void Rebuild(IReadOnlyList<Renderer> renderers, Camera camera)
        {
            if (renderers == null) throw new System.ArgumentNullException(nameof(renderers));
            if (camera == null) throw new System.ArgumentNullException(nameof(camera));

            _right = camera.transform.right;
            _up = camera.transform.up;
            _forward = camera.transform.forward;
            _referencePoint = renderers.Count > 0 ? renderers[0].transform.position : Vector3.zero;

            int totalVertexCount = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                totalVertexCount += GetSourceVertexCount(renderers[i]);
            }

            if (_projectedPoints == null || _projectedPoints.Length < totalVertexCount)
            {
                _projectedPoints = new Vector3[totalVertexCount];
            }

            int writeIndex = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                var mesh = ResolveMeshForCurrentPose(renderer);
                if (mesh == null) continue;

                var localToWorld = renderer.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                for (int j = 0; j < vertices.Length; j++)
                {
                    var world = localToWorld.MultiplyPoint3x4(vertices[j]);
                    var d = world - _referencePoint;
                    _projectedPoints[writeIndex++] = new Vector3(
                        Vector3.Dot(d, _right),
                        Vector3.Dot(d, _up),
                        Vector3.Dot(d, _forward)
                    );
                }
            }
            _projectedPointCount = writeIndex;
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
            if (_projectedPointCount == 0)
            {
                throw new System.InvalidOperationException(
                    "ObjectBoundingFrustum has no points. Call Rebuild() with non-empty renderers first.");
            }

            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity;
            float top = float.NegativeInfinity;

            for (int i = 0; i < _projectedPointCount; i++)
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
                    _bakeMeshBuffer = new Mesh { name = "ObjectBoundingFrustum.BakeBuffer" };
                    _bakeMeshBuffer.hideFlags = HideFlags.HideAndDontSave;
                }
                // useScale: false にすることで、後段で renderer.transform.localToWorldMatrix を
                // 適用したときに scale が二重に適用されないようにする。
                skinnedMeshRenderer.BakeMesh(_bakeMeshBuffer, useScale: false);
                return _bakeMeshBuffer;
            }
            return null;
        }
    }
}
