using UnityEngine;
using GeometryToolkit.CameraFraming;

namespace GeometryToolkit.Samples
{
    /// <summary>
    /// Repositions the assigned camera each frame so the assigned renderers fit inside the
    /// requested margins. Camera orientation is left untouched.
    /// </summary>
    public sealed class AutoFramingCameraSample : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Renderer[] _renderers;
        [SerializeField, Range(0f, 0.4f)] private float _marginPercent = 0.1f;

        private readonly AutoFramingCamera _autoFraming = new();

        void Update()
        {
            if (_camera == null || _renderers == null || _renderers.Length == 0) return;

            var margin = ScreenMargin.Uniform(_marginPercent, isPercentage: true);
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);

            _camera.transform.position = _autoFraming.ComputeCameraPosition(
                _camera, _renderers, margin, width, height);
        }
    }
}
