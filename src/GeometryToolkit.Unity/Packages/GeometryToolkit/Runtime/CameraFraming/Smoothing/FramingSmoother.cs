using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    /// <summary>
    /// Smooths the four frustum-plane offsets returned by <see cref="ObjectBoundingFrustum"/>
    /// using one <see cref="OneEuroFilter"/> per offset, then recomposes the camera position
    /// via <see cref="AutoFramingCamera.RecomposeCameraPosition"/>.
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
        /// Master switch. When false, the One-Euro filters are bypassed: smoothed offsets equal
        /// raw offsets and the smoothed camera position equals the raw one. Use for debugging to
        /// verify the visualization matches the unfiltered baseline. The internal filter state is
        /// reset while disabled so the next re-enabled frame initializes cleanly from the current
        /// input.
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

        // Last-frame snapshots, exposed via properties for debug visualization.
        private FramingOffsets _lastRawOffsets;
        private FramingOffsets _lastSmoothedOffsets;
        private Vector3 _lastRawPosition;
        private Vector3 _lastSmoothedPosition;
        private bool _hasLastFrame;

        /// <summary>True once <see cref="ApplyAndRecompose"/> has been called at least once.</summary>
        public bool HasLastFrame => _hasLastFrame;

        /// <summary>Raw frustum-plane offsets (pre-filter) from the most recent call.</summary>
        public FramingOffsets LastRawOffsets => _lastRawOffsets;

        /// <summary>Smoothed frustum-plane offsets (post-filter) from the most recent call.</summary>
        public FramingOffsets LastSmoothedOffsets => _lastSmoothedOffsets;

        /// <summary>Camera position recomposed from the raw offsets (no smoothing applied).</summary>
        public Vector3 LastRawPosition => _lastRawPosition;

        /// <summary>Camera position recomposed from the smoothed offsets.</summary>
        public Vector3 LastSmoothedPosition => _lastSmoothedPosition;

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
        /// Update the frame snapshot used for debug views.
        /// </summary>
        public FramingSmoothingResult FinalizeFrame(
            FramingOffsets rawOffsets,
            FramingOffsets smoothedOffsets,
            Vector3 rawPosition,
            Vector3 smoothedPosition)
        {
            _lastRawOffsets = rawOffsets;
            _lastSmoothedOffsets = smoothedOffsets;
            _lastRawPosition = rawPosition;
            _lastSmoothedPosition = smoothedPosition;
            _hasLastFrame = true;

            return new FramingSmoothingResult(
                rawOffsets,
                smoothedOffsets,
                rawPosition,
                smoothedPosition);
        }

        /// <summary>
        /// Discard internal filter and snapshot state. Call this when the framing target
        /// changes discontinuously (target switch, teleport) so the One-Euro speed estimate
        /// doesn't carry over the artifact.
        /// </summary>
        public void Reset()
        {
            ResetFiltersOnly();
            _hasLastFrame = false;
            _lastRawOffsets = default;
            _lastSmoothedOffsets = default;
            _lastRawPosition = default;
            _lastSmoothedPosition = default;
        }

        private void ResetFiltersOnly()
        {
            _leftFilter.Reset();
            _rightFilter.Reset();
            _bottomFilter.Reset();
            _topFilter.Reset();
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

    }
}
