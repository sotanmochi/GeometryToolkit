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

        private FramingSmoothingSettings _settings = FramingSmoothingSettings.CreateDefault();

        /// <summary>
        /// Master switch. When false, the One-Euro filters and the dead-zone / max-speed clamps
        /// are bypassed: smoothed offsets equal raw offsets and the smoothed camera position
        /// equals the raw one. Use for debugging to verify the visualization matches the
        /// unfiltered baseline. The internal filter state is reset while disabled so the next
        /// re-enabled frame initializes cleanly from the current input.
        /// </summary>
        public FramingSmoothingSettings Settings
        {
            get => _settings;
            set => _settings = value;
        }

        public bool Enabled
        {
            get => _settings.Enabled;
            set => _settings.Enabled = value;
        }

        /// <summary>One-Euro minimum cutoff (Hz) applied to left / right offsets.</summary>
        public float HorizontalMinCutoff
        {
            get => _settings.HorizontalMinCutoff;
            set => _settings.HorizontalMinCutoff = value;
        }

        /// <summary>One-Euro minimum cutoff (Hz) applied to bottom / top offsets.</summary>
        public float VerticalMinCutoff
        {
            get => _settings.VerticalMinCutoff;
            set => _settings.VerticalMinCutoff = value;
        }

        /// <summary>One-Euro speed coefficient applied to left / right offsets.</summary>
        public float HorizontalBeta
        {
            get => _settings.HorizontalBeta;
            set => _settings.HorizontalBeta = value;
        }

        /// <summary>One-Euro speed coefficient applied to bottom / top offsets.</summary>
        public float VerticalBeta
        {
            get => _settings.VerticalBeta;
            set => _settings.VerticalBeta = value;
        }

        /// <summary>One-Euro cutoff (Hz) for the speed estimate. 1.0 is the canonical default.</summary>
        public float DerivativeCutoff
        {
            get => _settings.DerivativeCutoff;
            set => _settings.DerivativeCutoff = value;
        }

        /// <summary>
        /// If the recomposed camera position would move less than this distance (meters) from
        /// the previous frame's position, the previous position is held instead. Suppresses
        /// idle micro-jitter. Set to 0 to disable.
        /// </summary>
        public float DeadZone
        {
            get => _settings.DeadZone;
            set => _settings.DeadZone = value;
        }

        /// <summary>
        /// Upper bound on camera-position translation per second (m/s). Caps abrupt jumps
        /// (e.g. target switch). Default <see cref="float.PositiveInfinity"/> = no limit.
        /// </summary>
        public float MaxLinearSpeed
        {
            get => _settings.MaxLinearSpeed;
            set => _settings.MaxLinearSpeed = value;
        }

        private bool _hasPreviousPosition;
        private Vector3 _previousPosition;

        // Last-frame snapshots, exposed via properties for debug visualization.
        private FramingOffsets _lastRawOffsets;
        private FramingOffsets _lastSmoothedOffsets;
        private Vector3 _lastRawPosition;
        private Vector3 _lastSmoothedPosition;
        private bool _lastDeadZoneHit;
        private bool _lastMaxSpeedHit;
        private bool _hasLastFrame;

        /// <summary>True once <see cref="ApplyAndRecompose"/> has been called at least once.</summary>
        public bool HasLastFrame => _hasLastFrame;

        /// <summary>Raw frustum-plane offsets (pre-filter) from the most recent call.</summary>
        public FramingOffsets LastRawOffsets => _lastRawOffsets;

        /// <summary>Smoothed frustum-plane offsets (post-filter) from the most recent call.</summary>
        public FramingOffsets LastSmoothedOffsets => _lastSmoothedOffsets;

        /// <summary>Camera position recomposed from the raw offsets (no smoothing applied).</summary>
        public Vector3 LastRawPosition => _lastRawPosition;

        /// <summary>Final smoothed camera position returned to the caller (after dead-zone / max-speed clamps).</summary>
        public Vector3 LastSmoothedPosition => _lastSmoothedPosition;

        /// <summary>True if the most recent call hit the dead zone (held previous position).</summary>
        public bool LastDeadZoneHit => _lastDeadZoneHit;

        /// <summary>True if the most recent call hit the max-linear-speed clamp.</summary>
        public bool LastMaxSpeedHit => _lastMaxSpeedHit;

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
            FramingOffsets rawOffsets = autoFramingCamera.ComputeFramingOffsets(
                camera, renderers, margin, screenWidth, screenHeight);
            FramingOffsets smoothedOffsets = SmoothOffsets(rawOffsets, dt);
            Vector3 rawPosition = autoFramingCamera.RecomposeCameraPosition(camera, rawOffsets, margin, screenWidth, screenHeight);
            Vector3 smoothedPosition = autoFramingCamera.RecomposeCameraPosition(camera, smoothedOffsets, margin, screenWidth, screenHeight);
            return FinalizeFrame(rawOffsets, smoothedOffsets, rawPosition, smoothedPosition, dt).SmoothedPosition;
        }

        /// <summary>
        /// Apply One-Euro filtering to raw frustum-plane offsets.
        /// </summary>
        public FramingOffsets SmoothOffsets(FramingOffsets rawOffsets, float dt)
        {
            ApplyParametersToFilters();
            if (!Enabled)
            {
                ResetFiltersOnly();
                return rawOffsets;
            }

            return new FramingOffsets(
                _leftFilter.Filter(rawOffsets.Left, dt),
                _rightFilter.Filter(rawOffsets.Right, dt),
                _bottomFilter.Filter(rawOffsets.Bottom, dt),
                _topFilter.Filter(rawOffsets.Top, dt));
        }

        /// <summary>
        /// Apply output clamps, update history, and return the frame snapshot used for debug views.
        /// </summary>
        public FramingSmoothingResult FinalizeFrame(
            FramingOffsets rawOffsets,
            FramingOffsets smoothedOffsets,
            Vector3 rawPosition,
            Vector3 smoothedPosition,
            float dt)
        {
            bool deadZoneHit = false;
            bool maxSpeedHit = false;
            Vector3 finalPosition = smoothedPosition;
            if (Enabled && _hasPreviousPosition)
            {
                finalPosition = ApplyMaxSpeedClamp(finalPosition, dt, out maxSpeedHit);
                finalPosition = ApplyDeadZone(finalPosition, out deadZoneHit);
            }

            _previousPosition = finalPosition;
            _hasPreviousPosition = true;

            _lastRawOffsets = rawOffsets;
            _lastSmoothedOffsets = smoothedOffsets;
            _lastRawPosition = rawPosition;
            _lastSmoothedPosition = finalPosition;
            _lastDeadZoneHit = deadZoneHit;
            _lastMaxSpeedHit = maxSpeedHit;
            _hasLastFrame = true;

            return new FramingSmoothingResult(
                rawOffsets,
                smoothedOffsets,
                rawPosition,
                finalPosition,
                deadZoneHit,
                maxSpeedHit);
        }

        /// <summary>
        /// Discard internal filter and history state. Call this when the framing target
        /// changes discontinuously (target switch, teleport) so the One-Euro speed estimate
        /// and the previous-position memory don't carry over the artifact.
        /// </summary>
        public void Reset()
        {
            ResetFiltersOnly();
            _hasPreviousPosition = false;
            _previousPosition = default;
            _hasLastFrame = false;
            _lastRawOffsets = default;
            _lastSmoothedOffsets = default;
            _lastRawPosition = default;
            _lastSmoothedPosition = default;
            _lastDeadZoneHit = false;
            _lastMaxSpeedHit = false;
        }

        private void ApplyParametersToFilters()
        {
            _leftFilter.MinCutoff = _settings.HorizontalMinCutoff;
            _leftFilter.Beta = _settings.HorizontalBeta;
            _leftFilter.DerivativeCutoff = _settings.DerivativeCutoff;
            _rightFilter.MinCutoff = _settings.HorizontalMinCutoff;
            _rightFilter.Beta = _settings.HorizontalBeta;
            _rightFilter.DerivativeCutoff = _settings.DerivativeCutoff;
            _bottomFilter.MinCutoff = _settings.VerticalMinCutoff;
            _bottomFilter.Beta = _settings.VerticalBeta;
            _bottomFilter.DerivativeCutoff = _settings.DerivativeCutoff;
            _topFilter.MinCutoff = _settings.VerticalMinCutoff;
            _topFilter.Beta = _settings.VerticalBeta;
            _topFilter.DerivativeCutoff = _settings.DerivativeCutoff;
        }

        private void ResetFiltersOnly()
        {
            _leftFilter.Reset();
            _rightFilter.Reset();
            _bottomFilter.Reset();
            _topFilter.Reset();
        }

        private Vector3 ApplyMaxSpeedClamp(Vector3 target, float dt, out bool clamped)
        {
            clamped = false;
            if (_settings.MaxLinearSpeed <= 0f || float.IsPositiveInfinity(_settings.MaxLinearSpeed) || dt <= 0f) return target;

            Vector3 delta = target - _previousPosition;
            float maxDistance = _settings.MaxLinearSpeed * dt;
            float deltaSqr = delta.sqrMagnitude;
            if (deltaSqr > maxDistance * maxDistance && deltaSqr > 0f)
            {
                clamped = true;
                return _previousPosition + delta * (maxDistance / Mathf.Sqrt(deltaSqr));
            }
            return target;
        }

        private Vector3 ApplyDeadZone(Vector3 target, out bool held)
        {
            held = false;
            if (_settings.DeadZone <= 0f) return target;

            Vector3 delta = target - _previousPosition;
            if (delta.sqrMagnitude < _settings.DeadZone * _settings.DeadZone)
            {
                held = true;
                return _previousPosition;
            }
            return target;
        }
    }
}
