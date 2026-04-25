namespace GeometryToolkit.CameraFraming.Smoothing
{
    /// <summary>
    /// Named parameter sets for <see cref="FramingSmoother"/>. Use as a starting point — apply
    /// via <see cref="SmoothedFramingFollower"/> Inspector or <see cref="FramingSmootherPresets.Apply"/>,
    /// then fine-tune individual fields as needed.
    /// </summary>
    public enum FramingSmoothingPreset
    {
        /// <summary>
        /// Maximally responsive. Higher cutoff and beta — minimal lag, only fine jitter is suppressed.
        /// Suited for sports / action capture where quick subject moves must be tracked tightly.
        /// </summary>
        Tight,

        /// <summary>
        /// Balanced default (Phase A1 baseline). Reasonable for general use.
        /// </summary>
        Standard,

        /// <summary>
        /// Documentary-style follow. Slightly slower and more forgiving of subject jitter,
        /// while still tracking large motion within ~half a second.
        /// </summary>
        Documentary,

        /// <summary>
        /// Cinematic. Heavy smoothing with slow follow and a wide dead zone — produces a
        /// "dolly on rails" feel where small subject movements are intentionally ignored.
        /// </summary>
        Cinematic,
    }

    /// <summary>
    /// Applies a <see cref="FramingSmoothingPreset"/> to a <see cref="FramingSmoother"/>'s
    /// public parameter properties. Centralized here so the table can be tuned without
    /// editing the smoother itself.
    /// </summary>
    public static class FramingSmootherPresets
    {
        public static void Apply(FramingSmoother smoother, FramingSmoothingPreset preset)
        {
            if (smoother == null) return;
            switch (preset)
            {
                case FramingSmoothingPreset.Tight:
                    smoother.HorizontalMinCutoff = 2.0f;
                    smoother.VerticalMinCutoff = 2.0f;
                    smoother.HorizontalBeta = 0.02f;
                    smoother.VerticalBeta = 0.02f;
                    smoother.DeadZone = 0.002f;
                    smoother.MaxLinearSpeed = float.PositiveInfinity;
                    break;
                case FramingSmoothingPreset.Documentary:
                    smoother.HorizontalMinCutoff = 0.7f;
                    smoother.VerticalMinCutoff = 0.7f;
                    smoother.HorizontalBeta = 0.005f;
                    smoother.VerticalBeta = 0.005f;
                    smoother.DeadZone = 0.01f;
                    smoother.MaxLinearSpeed = 30f;
                    break;
                case FramingSmoothingPreset.Cinematic:
                    smoother.HorizontalMinCutoff = 0.3f;
                    smoother.VerticalMinCutoff = 0.3f;
                    smoother.HorizontalBeta = 0.003f;
                    smoother.VerticalBeta = 0.003f;
                    smoother.DeadZone = 0.02f;
                    smoother.MaxLinearSpeed = 10f;
                    break;
                case FramingSmoothingPreset.Standard:
                default:
                    smoother.HorizontalMinCutoff = 1.0f;
                    smoother.VerticalMinCutoff = 1.0f;
                    smoother.HorizontalBeta = 0.007f;
                    smoother.VerticalBeta = 0.007f;
                    smoother.DeadZone = 0.005f;
                    smoother.MaxLinearSpeed = 50f;
                    break;
            }
        }

        /// <summary>
        /// Snapshot of preset values as a struct, for callers that want to apply them to
        /// serialized fields directly (e.g. <see cref="SmoothedFramingFollower"/> Inspector
        /// fields written via OnValidate).
        /// </summary>
        public static PresetValues GetValues(FramingSmoothingPreset preset)
        {
            switch (preset)
            {
                case FramingSmoothingPreset.Tight:
                    return new PresetValues(2.0f, 2.0f, 0.02f, 0.02f, 0.002f, float.PositiveInfinity);
                case FramingSmoothingPreset.Documentary:
                    return new PresetValues(0.7f, 0.7f, 0.005f, 0.005f, 0.01f, 30f);
                case FramingSmoothingPreset.Cinematic:
                    return new PresetValues(0.3f, 0.3f, 0.003f, 0.003f, 0.02f, 10f);
                case FramingSmoothingPreset.Standard:
                default:
                    return new PresetValues(1.0f, 1.0f, 0.007f, 0.007f, 0.005f, 50f);
            }
        }

        public readonly struct PresetValues
        {
            public readonly float HorizontalMinCutoff;
            public readonly float VerticalMinCutoff;
            public readonly float HorizontalBeta;
            public readonly float VerticalBeta;
            public readonly float DeadZone;
            public readonly float MaxLinearSpeed;

            public PresetValues(float hCutoff, float vCutoff, float hBeta, float vBeta, float deadZone, float maxSpeed)
            {
                HorizontalMinCutoff = hCutoff;
                VerticalMinCutoff = vCutoff;
                HorizontalBeta = hBeta;
                VerticalBeta = vBeta;
                DeadZone = deadZone;
                MaxLinearSpeed = maxSpeed;
            }
        }
    }
}
