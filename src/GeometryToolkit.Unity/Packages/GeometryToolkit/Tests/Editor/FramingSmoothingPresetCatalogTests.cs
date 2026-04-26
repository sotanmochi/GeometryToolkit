using NUnit.Framework;

namespace GeometryToolkit.CameraFraming.Smoothing.Tests
{
    public sealed class FramingSmoothingPresetCatalogTests
    {
        [Test]
        public void StandardPreset_MatchesExpectedDefaults()
        {
            FramingSmoothingSettings settings = FramingSmoothingPresetCatalog.Get(FramingSmoothingPreset.Standard);

            Assert.IsTrue(settings.Enabled);
            Assert.AreEqual(1f, settings.HorizontalMinCutoff);
            Assert.AreEqual(1f, settings.VerticalMinCutoff);
            Assert.AreEqual(0.007f, settings.HorizontalBeta);
            Assert.AreEqual(0.007f, settings.VerticalBeta);
            Assert.AreEqual(1f, settings.DerivativeCutoff);
        }

        [Test]
        public void LegacyPresetWrapper_UsesCatalogValues()
        {
            FramingSmoothingSettings settings = FramingSmoothingPresetCatalog.Get(FramingSmoothingPreset.Documentary);
            FramingSmootherPresets.PresetValues values = FramingSmootherPresets.GetValues(FramingSmoothingPreset.Documentary);

            Assert.AreEqual(settings.HorizontalMinCutoff, values.HorizontalMinCutoff);
            Assert.AreEqual(settings.VerticalMinCutoff, values.VerticalMinCutoff);
            Assert.AreEqual(settings.HorizontalBeta, values.HorizontalBeta);
            Assert.AreEqual(settings.VerticalBeta, values.VerticalBeta);
        }
    }
}
