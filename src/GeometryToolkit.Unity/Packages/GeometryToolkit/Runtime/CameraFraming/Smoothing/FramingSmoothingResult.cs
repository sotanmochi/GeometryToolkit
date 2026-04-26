using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    /// <summary>
    /// Output of a single smoothing step.
    /// </summary>
    public readonly struct FramingSmoothingResult
    {
        public readonly FramingOffsets RawOffsets;
        public readonly FramingOffsets SmoothedOffsets;
        public readonly Vector3 RawPosition;
        public readonly Vector3 SmoothedPosition;

        public FramingSmoothingResult(
            FramingOffsets rawOffsets,
            FramingOffsets smoothedOffsets,
            Vector3 rawPosition,
            Vector3 smoothedPosition)
        {
            RawOffsets = rawOffsets;
            SmoothedOffsets = smoothedOffsets;
            RawPosition = rawPosition;
            SmoothedPosition = smoothedPosition;
        }
    }
}
