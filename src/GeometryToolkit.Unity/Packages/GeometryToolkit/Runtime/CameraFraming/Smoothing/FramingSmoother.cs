using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    /// <summary>
    /// Smooths the four frustum-plane offsets returned by <see cref="ObjectBoundingFrustum"/>
    /// using one <see cref="OneEuroFilter"/> per offset, then recomposes the camera position
    /// via <see cref="AutoFramingCamera.RecomposeCameraPosition"/>. A dead zone and a maximum
    /// linear speed clamp are applied to the resulting camera position to suppress idle micro-
    /// jitter and prevent abrupt jumps.
    ///
    /// Smoothing operates in framing space (the frustum-plane offsets), not on the final
    /// camera position. This keeps the camera in tight closed-form fit to a temporally
    /// smoothed framing target — preserving the geometric correctness of <see cref="AutoFramingCamera"/>'s
    /// closed-form solution while attenuating per-frame noise.
    ///
    /// Horizontal (left/right) and vertical (bottom/top) axes can have independent One-Euro
    /// parameters, allowing e.g. faster horizontal tracking with looser vertical follow.
    /// </summary>
    public sealed class FramingSmoother
    {
        private readonly OneEuroFilter _leftFilter = new();
        private readonly OneEuroFilter _rightFilter = new();
        private readonly OneEuroFilter _bottomFilter = new();
        private readonly OneEuroFilter _topFilter = new();

        /// <summary>One-Euro minimum cutoff (Hz) applied to left / right offsets.</summary>
        public float HorizontalMinCutoff { get; set; } = 1f;

        /// <summary>One-Euro minimum cutoff (Hz) applied to bottom / top offsets.</summary>
        public float VerticalMinCutoff { get; set; } = 1f;

        /// <summary>One-Euro speed coefficient applied to left / right offsets.</summary>
        public float HorizontalBeta { get; set; } = 0.007f;

        /// <summary>One-Euro speed coefficient applied to bottom / top offsets.</summary>
        public float VerticalBeta { get; set; } = 0.007f;

        /// <summary>One-Euro cutoff (Hz) for the speed estimate. 1.0 is the canonical default.</summary>
        public float DerivativeCutoff { get; set; } = 1f;

        /// <summary>
        /// If the recomposed camera position would move less than this distance (meters) from
        /// the previous frame's position, the previous position is held instead. Suppresses
        /// idle micro-jitter. Set to 0 to disable.
        /// </summary>
        public float DeadZone { get; set; } = 0.005f;

        /// <summary>
        /// Upper bound on camera-position translation per second (m/s). Caps abrupt jumps
        /// (e.g. target switch). Default <see cref="float.PositiveInfinity"/> = no limit.
        /// </summary>
        public float MaxLinearSpeed { get; set; } = float.PositiveInfinity;

        private bool _hasPreviousPosition;
        private Vector3 _previousPosition;

        /// <summary>
        /// Build the bounding frustum, smooth the four offsets, and return the recomposed
        /// camera position with dead-zone and max-speed clamps applied.
        /// </summary>
        /// <param name="autoFramingCamera">The framing solver that owns the bounding frustum and vertex collector.</param>
        /// <param name="camera">Target camera (orientation defines the framing axes).</param>
        /// <param name="renderers">Renderers whose vertices define the framing target.</param>
        /// <param name="margin">Screen-space margin specification.</param>
        /// <param name="screenWidth">Screen width in pixels (or rendering target width).</param>
        /// <param name="screenHeight">Screen height in pixels (or rendering target height).</param>
        /// <param name="dt">Time elapsed since the previous call, in seconds.</param>
        public Vector3 ApplyAndRecompose(
            AutoFramingCamera autoFramingCamera,
            Camera camera,
            IReadOnlyList<Renderer> renderers,
            ScreenMargin margin,
            int screenWidth,
            int screenHeight,
            float dt)
        {
            ApplyParametersToFilters();

            var raw = autoFramingCamera.ComputeFrustumPlaneOffsets(
                camera, renderers, margin, screenWidth, screenHeight);

            float sLeft = _leftFilter.Filter(raw.left, dt);
            float sRight = _rightFilter.Filter(raw.right, dt);
            float sBottom = _bottomFilter.Filter(raw.bottom, dt);
            float sTop = _topFilter.Filter(raw.top, dt);

            Vector3 target = autoFramingCamera.RecomposeCameraPosition(
                camera, sLeft, sRight, sBottom, sTop, margin, screenWidth, screenHeight);

            if (_hasPreviousPosition)
            {
                target = ApplyMaxSpeedClamp(target, dt);
                target = ApplyDeadZone(target);
            }

            _previousPosition = target;
            _hasPreviousPosition = true;
            return target;
        }

        /// <summary>
        /// Discard internal filter and history state. Call this when the framing target
        /// changes discontinuously (target switch, teleport) so the One-Euro speed estimate
        /// and the previous-position memory don't carry over the artifact.
        /// </summary>
        public void Reset()
        {
            _leftFilter.Reset();
            _rightFilter.Reset();
            _bottomFilter.Reset();
            _topFilter.Reset();
            _hasPreviousPosition = false;
            _previousPosition = default;
        }

        private void ApplyParametersToFilters()
        {
            _leftFilter.MinCutoff = HorizontalMinCutoff;
            _leftFilter.Beta = HorizontalBeta;
            _leftFilter.DerivativeCutoff = DerivativeCutoff;
            _rightFilter.MinCutoff = HorizontalMinCutoff;
            _rightFilter.Beta = HorizontalBeta;
            _rightFilter.DerivativeCutoff = DerivativeCutoff;
            _bottomFilter.MinCutoff = VerticalMinCutoff;
            _bottomFilter.Beta = VerticalBeta;
            _bottomFilter.DerivativeCutoff = DerivativeCutoff;
            _topFilter.MinCutoff = VerticalMinCutoff;
            _topFilter.Beta = VerticalBeta;
            _topFilter.DerivativeCutoff = DerivativeCutoff;
        }

        private Vector3 ApplyMaxSpeedClamp(Vector3 target, float dt)
        {
            if (float.IsPositiveInfinity(MaxLinearSpeed) || dt <= 0f) return target;

            Vector3 delta = target - _previousPosition;
            float maxDistance = MaxLinearSpeed * dt;
            float deltaSqr = delta.sqrMagnitude;
            if (deltaSqr > maxDistance * maxDistance && deltaSqr > 0f)
            {
                return _previousPosition + delta * (maxDistance / Mathf.Sqrt(deltaSqr));
            }
            return target;
        }

        private Vector3 ApplyDeadZone(Vector3 target)
        {
            if (DeadZone <= 0f) return target;

            Vector3 delta = target - _previousPosition;
            if (delta.sqrMagnitude < DeadZone * DeadZone)
            {
                return _previousPosition;
            }
            return target;
        }
    }
}
