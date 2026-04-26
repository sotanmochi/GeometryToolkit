using System;
using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    /// <summary>
    /// Serialized smoothing configuration shared by presets, MonoBehaviours, and runtime filters.
    /// </summary>
    [Serializable]
    public struct FramingSmoothingSettings : IEquatable<FramingSmoothingSettings>
    {
        [Tooltip("Master switch. When off, smoothing and output clamps are bypassed.")]
        public bool Enabled;

        [Min(0f)]
        public float HorizontalMinCutoff;

        [Min(0f)]
        public float VerticalMinCutoff;

        [Min(0f)]
        public float HorizontalBeta;

        [Min(0f)]
        public float VerticalBeta;

        [HideInInspector]
        public float DerivativeCutoff;

        [Min(0f), Tooltip("Hold previous position when target moves less than this distance (meters). 0 disables.")]
        public float DeadZone;

        [Min(0f), Tooltip("Maximum camera translation speed (m/s). 0 = unlimited.")]
        public float MaxLinearSpeed;

        public static FramingSmoothingSettings CreateDefault()
        {
            return new FramingSmoothingSettings
            {
                Enabled = true,
                HorizontalMinCutoff = 1f,
                VerticalMinCutoff = 1f,
                HorizontalBeta = 0.007f,
                VerticalBeta = 0.007f,
                DerivativeCutoff = 1f,
                DeadZone = 0.005f,
                MaxLinearSpeed = 50f,
            };
        }

        public bool Equals(FramingSmoothingSettings other) =>
            Enabled == other.Enabled &&
            HorizontalMinCutoff == other.HorizontalMinCutoff &&
            VerticalMinCutoff == other.VerticalMinCutoff &&
            HorizontalBeta == other.HorizontalBeta &&
            VerticalBeta == other.VerticalBeta &&
            DerivativeCutoff == other.DerivativeCutoff &&
            DeadZone == other.DeadZone &&
            MaxLinearSpeed == other.MaxLinearSpeed;

        public override bool Equals(object obj) => obj is FramingSmoothingSettings other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Enabled,
            HorizontalMinCutoff,
            VerticalMinCutoff,
            HorizontalBeta,
            VerticalBeta,
            DerivativeCutoff,
            DeadZone,
            MaxLinearSpeed);
    }
}
