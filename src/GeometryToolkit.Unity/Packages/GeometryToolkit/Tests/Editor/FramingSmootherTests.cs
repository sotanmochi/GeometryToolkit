using NUnit.Framework;
using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing.Tests
{
    public sealed class FramingSmootherTests
    {
        [Test]
        public void SmoothOffsets_ReturnsRawOffsets_WhenDisabled()
        {
            var smoother = new FramingSmoother
            {
                Settings = FramingSmoothingSettings.CreateDefault()
            };
            var settings = smoother.Settings;
            settings.Enabled = false;
            smoother.Settings = settings;

            var rawOffsets = new FramingOffsets(1f, 2f, 3f, 4f);

            FramingOffsets result = smoother.SmoothOffsets(rawOffsets, 1f / 60f);

            Assert.AreEqual(rawOffsets, result);
        }

        [Test]
        public void FinalizeFrame_HoldsPreviousPosition_WhenInsideDeadZone()
        {
            var smoother = new FramingSmoother
            {
                Settings = new FramingSmoothingSettings
                {
                    Enabled = true,
                    HorizontalMinCutoff = 1f,
                    VerticalMinCutoff = 1f,
                    HorizontalBeta = 0.007f,
                    VerticalBeta = 0.007f,
                    DerivativeCutoff = 1f,
                    DeadZone = 0.5f,
                    MaxLinearSpeed = 0f,
                }
            };

            var offsets = new FramingOffsets(0f, 0f, 0f, 0f);
            smoother.FinalizeFrame(offsets, offsets, Vector3.zero, new Vector3(1f, 0f, 0f), 1f / 60f);

            FramingSmoothingResult result = smoother.FinalizeFrame(
                offsets,
                offsets,
                new Vector3(1.1f, 0f, 0f),
                new Vector3(1.1f, 0f, 0f),
                1f / 60f);

            Assert.AreEqual(new Vector3(1f, 0f, 0f), result.SmoothedPosition);
            Assert.IsTrue(result.DeadZoneHit);
        }

        [Test]
        public void FinalizeFrame_ClampsDistance_WhenMaxSpeedIsExceeded()
        {
            var smoother = new FramingSmoother
            {
                Settings = new FramingSmoothingSettings
                {
                    Enabled = true,
                    HorizontalMinCutoff = 1f,
                    VerticalMinCutoff = 1f,
                    HorizontalBeta = 0.007f,
                    VerticalBeta = 0.007f,
                    DerivativeCutoff = 1f,
                    DeadZone = 0f,
                    MaxLinearSpeed = 2f,
                }
            };

            var offsets = new FramingOffsets(0f, 0f, 0f, 0f);
            smoother.FinalizeFrame(offsets, offsets, Vector3.zero, Vector3.zero, 1f);

            FramingSmoothingResult result = smoother.FinalizeFrame(
                offsets,
                offsets,
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                0.5f);

            Assert.AreEqual(new Vector3(1f, 0f, 0f), result.SmoothedPosition);
            Assert.IsTrue(result.MaxSpeedHit);
        }
    }
}
