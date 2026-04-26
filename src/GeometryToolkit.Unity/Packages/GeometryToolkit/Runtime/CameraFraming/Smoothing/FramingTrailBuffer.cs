using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    internal sealed class FramingTrailBuffer
    {
        private readonly Color _rawColor = new(1f, 0.85f, 0.15f, 0.85f);
        private readonly Color _smoothedColor = new(0.25f, 1f, 0.5f, 0.85f);

        private Vector3[] _rawTrail;
        private Vector3[] _smoothedTrail;
        private int _count;
        private int _writeIndex;

        public void Clear()
        {
            _count = 0;
            _writeIndex = 0;
        }

        public void Push(Vector3 raw, Vector3 smoothed, int capacity)
        {
            int requiredCapacity = Mathf.Max(2, capacity);
            if (_rawTrail == null || _rawTrail.Length != requiredCapacity)
            {
                _rawTrail = new Vector3[requiredCapacity];
                _smoothedTrail = new Vector3[requiredCapacity];
                Clear();
            }

            _rawTrail[_writeIndex] = raw;
            _smoothedTrail[_writeIndex] = smoothed;
            _writeIndex = (_writeIndex + 1) % requiredCapacity;
            if (_count < requiredCapacity) _count++;
        }

        public void DrawGizmos(float markerRadius = 0.03f)
        {
            if (_count < 2 || _rawTrail == null || _smoothedTrail == null) return;

            int capacity = _rawTrail.Length;
            int oldest = (_writeIndex - _count + capacity) % capacity;

            Vector3 previousRaw = _rawTrail[oldest];
            Vector3 previousSmoothed = _smoothedTrail[oldest];
            for (int i = 1; i < _count; i++)
            {
                int index = (oldest + i) % capacity;
                Vector3 raw = _rawTrail[index];
                Vector3 smoothed = _smoothedTrail[index];

                Gizmos.color = _rawColor;
                Gizmos.DrawLine(previousRaw, raw);
                Gizmos.color = _smoothedColor;
                Gizmos.DrawLine(previousSmoothed, smoothed);

                previousRaw = raw;
                previousSmoothed = smoothed;
            }

            int latest = (_writeIndex - 1 + capacity) % capacity;
            Gizmos.color = _rawColor;
            Gizmos.DrawSphere(_rawTrail[latest], markerRadius);
            Gizmos.color = _smoothedColor;
            Gizmos.DrawSphere(_smoothedTrail[latest], markerRadius);
        }
    }
}
