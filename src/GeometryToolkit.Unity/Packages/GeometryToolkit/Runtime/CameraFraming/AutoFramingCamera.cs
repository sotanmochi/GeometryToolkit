using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.CameraFraming
{
    /// <summary>
    /// Computes a camera world position that frames the given renderers within the requested
    /// screen-space margins, given a fixed camera orientation.
    ///
    /// Algorithm: for the given camera orientation, build an
    /// <see cref="ObjectBoundingFrustum"/> over the input vertices, evaluate the four side
    /// support values for the requested NDC bounds, then solve the closed-form camera position.
    /// One axis tightly fits the requested margins; the other axis is slack and re-centered.
    /// </summary>
    public sealed class AutoFramingCamera
    {
        private readonly ObjectBoundingFrustum _boundingFrustum = new();

        public ObjectBoundingFrustum BoundingFrustum => _boundingFrustum;

        /// <summary>
        /// Compute the camera position that frames the renderers within the requested margins.
        /// The camera's orientation is taken from <paramref name="camera"/> and is unchanged.
        /// </summary>
        public Vector3 ComputeCameraPosition(
            Camera camera,
            IReadOnlyList<Renderer> renderers,
            ScreenMargin margin,
            int screenWidth,
            int screenHeight)
        {
            if (camera == null) throw new System.ArgumentNullException(nameof(camera));
            if (renderers == null) throw new System.ArgumentNullException(nameof(renderers));
            if (renderers.Count == 0) throw new System.ArgumentException("renderers must not be empty.", nameof(renderers));

            _boundingFrustum.Rebuild(renderers, camera);
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
            if (camera == null) throw new System.ArgumentNullException(nameof(camera));

            var (nLeft, nRight, nBottom, nTop) = margin.ToNdcBounds(screenWidth, screenHeight);
            float horizontalSpan = nRight - nLeft;
            float verticalSpan = nTop - nBottom;
            const float MinSpan = 1e-4f;
            if (horizontalSpan < MinSpan || verticalSpan < MinSpan)
            {
                throw new System.ArgumentException(
                    "ScreenMargin leaves no usable area; (nRight - nLeft) and (nTop - nBottom) must be positive.");
            }

            float fovYRad = camera.fieldOfView * Mathf.Deg2Rad;
            float kVertical = Mathf.Tan(fovYRad * 0.5f);
            float kHorizontal = kVertical * camera.aspect;

            var (a, b, c, d) = _boundingFrustum.ComputeSupportValues(
                nLeft, nRight, nBottom, nTop, kHorizontal, kVertical);

            // Closed-form depth on each axis. Re-derived from the touching-plane equations
            // (see ObjectBoundingFrustum.ComputeSupportValues comment); the design document
            // listed these with flipped signs.
            float pfHorizontal = (b - a) / (horizontalSpan * kHorizontal);
            float pfVertical = (d - c) / (verticalSpan * kVertical);

            // Take the more restrictive depth so neither axis overflows the requested margins.
            float pf = Mathf.Max(pfHorizontal, pfVertical);

            // For the axis that became slack (smaller pf), recenter so the slack is shared
            // between the two sides instead of pinning to one edge.
            float pr = HorizontalIsTight(pfHorizontal, pfVertical)
                ? a - nLeft * kHorizontal * pf
                : ((a - nLeft * kHorizontal * pf) + (b - nRight * kHorizontal * pf)) * 0.5f;

            float pu = !HorizontalIsTight(pfHorizontal, pfVertical)
                ? c - nBottom * kVertical * pf
                : ((c - nBottom * kVertical * pf) + (d - nTop * kVertical * pf)) * 0.5f;

            // Camera world position = reference + R * pr + U * pu - F * pf.
            // The -F term places the camera "behind" the reference along the forward axis,
            // so the bounded points lie in front of the camera (positive depth).
            return _boundingFrustum.ReferencePoint
                   + _boundingFrustum.Right * pr
                   + _boundingFrustum.Up * pu
                   - _boundingFrustum.Forward * pf;
        }

        private static bool HorizontalIsTight(float pfHorizontal, float pfVertical)
            => pfHorizontal >= pfVertical;
    }
}
