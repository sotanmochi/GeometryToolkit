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
        /// Cinematic. Heavy smoothing with slow follow — produces a "dolly on rails" feel
        /// where small subject movements are intentionally ignored.
        /// </summary>
        Cinematic,
    }

    /// <summary>
    /// Applies a <see cref="FramingSmoothingPreset"/> to a <see cref="FramingSmoother"/>'s
    /// public parameter properties. Centralized here so the table can be tuned without
    /// editing the smoother itself.
    /// </summary>
    public static class FramingSmoothingPresetCatalog
    {
        public static FramingSmoothingSettings Get(FramingSmoothingPreset preset)
        {
            switch (preset)
            {
                case FramingSmoothingPreset.Tight:
                    return CreateSettings(2.0f, 2.0f, 0.02f, 0.02f);
                case FramingSmoothingPreset.Documentary:
                    return CreateSettings(0.7f, 0.7f, 0.005f, 0.005f);
                case FramingSmoothingPreset.Cinematic:
                    return CreateSettings(0.3f, 0.3f, 0.003f, 0.003f);
                case FramingSmoothingPreset.Standard:
                default:
                    return CreateSettings(1.0f, 1.0f, 0.007f, 0.007f);
            }
        }

        private static FramingSmoothingSettings CreateSettings(
            float horizontalMinCutoff,
            float verticalMinCutoff,
            float horizontalBeta,
            float verticalBeta)
        {
            return new FramingSmoothingSettings
            {
                Enabled = true,
                HorizontalMinCutoff = horizontalMinCutoff,
                VerticalMinCutoff = verticalMinCutoff,
                HorizontalBeta = horizontalBeta,
                VerticalBeta = verticalBeta,
                DerivativeCutoff = 1f,
            };
        }
    }

    /// <summary>
    /// Backward-compatible preset helper. New code should read settings via
    /// <see cref="FramingSmoothingPresetCatalog"/>.
    /// </summary>
    public static class FramingSmootherPresets
    {
        public static void Apply(FramingSmoother smoother, FramingSmoothingPreset preset)
        {
            if (smoother == null) return;
            smoother.Settings = FramingSmoothingPresetCatalog.Get(preset);
        }

        public static PresetValues GetValues(FramingSmoothingPreset preset)
        {
            return new PresetValues(FramingSmoothingPresetCatalog.Get(preset));
        }

        public readonly struct PresetValues
        {
            public readonly float HorizontalMinCutoff;
            public readonly float VerticalMinCutoff;
            public readonly float HorizontalBeta;
            public readonly float VerticalBeta;

            public PresetValues(FramingSmoothingSettings settings)
            {
                HorizontalMinCutoff = settings.HorizontalMinCutoff;
                VerticalMinCutoff = settings.VerticalMinCutoff;
                HorizontalBeta = settings.HorizontalBeta;
                VerticalBeta = settings.VerticalBeta;
            }
        }
    }
}
