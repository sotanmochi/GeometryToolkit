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
        public void FinalizeFrame_ReturnsSmoothedPositionUnchanged()
        {
            var smoother = new FramingSmoother();
            var rawOffsets = new FramingOffsets(0f, 1f, 2f, 3f);
            var smoothedOffsets = new FramingOffsets(4f, 5f, 6f, 7f);
            Vector3 rawPosition = new(1f, 2f, 3f);
            Vector3 smoothedPosition = new(4f, 5f, 6f);

            FramingSmoothingResult result = smoother.FinalizeFrame(
                rawOffsets,
                smoothedOffsets,
                rawPosition,
                smoothedPosition);

            Assert.AreEqual(rawOffsets, result.RawOffsets);
            Assert.AreEqual(smoothedOffsets, result.SmoothedOffsets);
            Assert.AreEqual(rawPosition, result.RawPosition);
            Assert.AreEqual(smoothedPosition, result.SmoothedPosition);
            Assert.AreEqual(smoothedPosition, smoother.LastSmoothedPosition);
            Assert.IsTrue(smoother.HasLastFrame);
        }

        [Test]
        public void Reset_ClearsLastFrameState()
        {
            var smoother = new FramingSmoother();
            var offsets = new FramingOffsets(0f, 0f, 0f, 0f);
            smoother.FinalizeFrame(offsets, offsets, Vector3.zero, Vector3.one);
            smoother.Reset();

            Assert.IsFalse(smoother.HasLastFrame);
            Assert.AreEqual(default(FramingOffsets), smoother.LastRawOffsets);
            Assert.AreEqual(default(Vector3), smoother.LastSmoothedPosition);
        }
    }
}
