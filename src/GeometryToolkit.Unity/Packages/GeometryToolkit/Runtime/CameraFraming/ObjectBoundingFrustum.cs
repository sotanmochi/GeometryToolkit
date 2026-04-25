using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// A frustum-shaped bounding volume for a point set, defined by four side planes parallel to
    /// the reference camera's view-frustum side planes. The bounding frustum is the unique smallest
    /// frustum (similar to the view frustum) that contains the point set.
    ///
    /// Internally stores each input point projected onto the camera-orientation basis (R, U, F) as
    /// (r, u, f) coordinates, which lets <see cref="ComputeSupportValues"/> evaluate the four side
    /// support values in O(N) without re-projecting per query.
    ///
    /// The (r, u, f) values are in a frame anchored at <see cref="ReferencePoint"/>; the frame's
    /// axes are the camera transform's right / up / forward at the time of <see cref="Rebuild"/>.
    /// </summary>
    public sealed class ObjectBoundingFrustum
    {
        private Vector3 _referencePoint;
        private Vector3 _right;
        private Vector3 _up;
        private Vector3 _forward;
        private Vector3[] _projectedPoints;
        private int _projectedPointCount;

        public Vector3 ReferencePoint => _referencePoint;
        public Vector3 Right => _right;
        public Vector3 Up => _up;
        public Vector3 Forward => _forward;
        public int PointCount => _projectedPointCount;

        /// <summary>
        /// Collect vertices from the given renderers and project them onto the camera's
        /// orientation basis. Subsequent <see cref="ComputeSupportValues"/> calls reuse these
        /// projections without re-traversing the meshes.
        ///
        /// Phase 2 supports <see cref="MeshRenderer"/>; <see cref="SkinnedMeshRenderer"/> support
        /// is planned for Phase 3.
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
                var mesh = TryGetSharedMesh(renderers[i]);
                if (mesh == null) continue;
                totalVertexCount += mesh.vertexCount;
            }

            if (_projectedPoints == null || _projectedPoints.Length < totalVertexCount)
            {
                _projectedPoints = new Vector3[totalVertexCount];
            }

            int writeIndex = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                var mesh = TryGetSharedMesh(renderer);
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
        /// Compute the four side support values used to solve for the camera position.
        /// </summary>
        /// <remarks>
        /// For each point P with (r, u, f) coordinates relative to <see cref="ReferencePoint"/>,
        /// the touching plane equations for the left/right/bottom/top edges are:
        /// <code>
        ///   left   edge: r - n_l * k_h * f = p_r + n_l * k_h * p_f
        ///   right  edge: r - n_r * k_h * f = p_r + n_r * k_h * p_f
        ///   bottom edge: u - n_b * k_v * f = p_u + n_b * k_v * p_f
        ///   top    edge: u - n_t * k_v * f = p_u + n_t * k_v * p_f
        /// </code>
        /// where (p_r, p_u, p_f) parameterize the camera position in the same frame
        /// (camera position = ReferencePoint + R * p_r + U * p_u - F * p_f, with p_f &gt; 0).
        ///
        /// The most extreme point in each direction defines the touching plane, hence:
        /// A = min(r - n_l * k_h * f), B = max(r - n_r * k_h * f),
        /// C = min(u - n_b * k_v * f), D = max(u - n_t * k_v * f).
        /// </remarks>
        public (float a, float b, float c, float d) ComputeSupportValues(
            float nLeft, float nRight, float nBottom, float nTop, float kHorizontal, float kVertical)
        {
            if (_projectedPointCount == 0)
            {
                throw new System.InvalidOperationException(
                    "ObjectBoundingFrustum has no points. Call Rebuild() with non-empty renderers first.");
            }

            float a = float.PositiveInfinity;
            float b = float.NegativeInfinity;
            float c = float.PositiveInfinity;
            float d = float.NegativeInfinity;

            for (int i = 0; i < _projectedPointCount; i++)
            {
                var p = _projectedPoints[i];
                float r = p.x;
                float u = p.y;
                float f = p.z;

                float supportLeft = r - nLeft * kHorizontal * f;
                float supportRight = r - nRight * kHorizontal * f;
                float supportBottom = u - nBottom * kVertical * f;
                float supportTop = u - nTop * kVertical * f;

                if (supportLeft < a) a = supportLeft;
                if (supportRight > b) b = supportRight;
                if (supportBottom < c) c = supportBottom;
                if (supportTop > d) d = supportTop;
            }

            return (a, b, c, d);
        }

        private static Mesh TryGetSharedMesh(Renderer renderer)
        {
            if (renderer is MeshRenderer meshRenderer)
            {
                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                return meshFilter != null ? meshFilter.sharedMesh : null;
            }
            // SkinnedMeshRenderer support is planned for Phase 3 (BakeMesh + optional convex hull cache).
            return null;
        }
    }
}
