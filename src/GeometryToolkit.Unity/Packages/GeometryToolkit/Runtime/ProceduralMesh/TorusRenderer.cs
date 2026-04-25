using UnityEngine;

namespace GeometryToolkit
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class TorusRenderer : MonoBehaviour
    {
        [Header("Torus Parameters")]
        [SerializeField, Range(0.1f, 10f)] private float _majorRadius = 1.0f;
        [SerializeField, Range(0.01f, 5f)] private float _minorRadius = 0.2f;
        [SerializeField, Range(3, 64)] private int _segments = 32;
        [SerializeField, Range(3, 32)] private int _tubeSegments = 16;

        private MeshFilter _meshFilter;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            RegenerateMesh();
        }

        private void OnValidate()
        {
            if (_meshFilter != null && Application.isEditor)
            {
                RegenerateMesh();
            }
        }

        public void RegenerateMesh()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            // Destroy old mesh
            if (_meshFilter.sharedMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_meshFilter.sharedMesh);
                }
                else
                {
                    DestroyImmediate(_meshFilter.sharedMesh);
                }
            }

            // Generate new mesh
            _meshFilter.sharedMesh = TorusMeshGenerator.Generate(_majorRadius, _minorRadius, _segments, _tubeSegments);
        }

        public void RegenerateMesh(float newMajorRadius, float newMinorRadius, int newSegments, int newTubeSegments)
        {
            _majorRadius = Mathf.Max(0.1f, newMajorRadius);
            _minorRadius = Mathf.Max(0.01f, newMinorRadius);
            _segments = Mathf.Clamp(newSegments, 3, 64);
            _tubeSegments = Mathf.Clamp(newTubeSegments, 3, 32);
            RegenerateMesh();
        }
    }
}
