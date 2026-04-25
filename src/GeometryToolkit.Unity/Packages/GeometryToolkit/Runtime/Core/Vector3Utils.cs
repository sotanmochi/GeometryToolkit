using System;
using UnityEngine;

namespace GeometryToolkit.Core
{
    public static class Vector3Utils
    {
        public const float Epsilon = 0.00001f;

        private const float QuantizationFactor = 100000; // 1f / Epsilon

        public static int GetHashCode(Vector3 v)
        {
            int x = (int)(v.x * QuantizationFactor);
            int y = (int)(v.y * QuantizationFactor);
            int z = (int)(v.z * QuantizationFactor);
            return HashCode.Combine(x, y, z);
        }
    }
}
