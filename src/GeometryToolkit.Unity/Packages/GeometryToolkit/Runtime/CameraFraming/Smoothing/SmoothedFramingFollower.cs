using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    /// <summary>
    /// MonoBehaviour wrapper that drives a camera's world position each frame so the assigned
    /// renderers stay framed within the requested screen-space margins, with One-Euro Filter
    /// smoothing applied per frustum-plane offset to suppress idle jitter and over-responsive
    /// follow during fast motion (e.g. dance / live capture).
    ///
    /// Camera orientation is left untouched. For non-smoothed framing, use
    /// <see cref="AutoFramingCamera"/> directly via a custom MonoBehaviour.
    /// </summary>
    [AddComponentMenu("Camera Framing/Smoothed Framing Follower")]
    public sealed class SmoothedFramingFollower : MonoBehaviour
    {
        public enum UpdateTiming { Update, LateUpdate, FixedUpdate }
        public enum CollectorType { Cpu, ComputeShader }

        [SerializeField] private Camera _camera;
        [SerializeField] private List<Renderer> _targets = new();
        [SerializeField, Range(0f, 40f)] private float _marginPercent = 10f;

        [Header("One-Euro Filter (Horizontal)")]
        [SerializeField, Min(0f)] private float _horizontalMinCutoff = 1f;
        [SerializeField, Min(0f)] private float _horizontalBeta = 0.007f;

        [Header("One-Euro Filter (Vertical)")]
        [SerializeField, Min(0f)] private float _verticalMinCutoff = 1f;
        [SerializeField, Min(0f)] private float _verticalBeta = 0.007f;

        [Header("Output Clamps")]
        [SerializeField, Min(0f), Tooltip("Hold previous position when target moves less than this distance (meters). 0 disables.")]
        private float _deadZone = 0.005f;

        [SerializeField, Min(0f), Tooltip("Maximum camera translation speed (m/s). 0 = unlimited.")]
        private float _maxLinearSpeed = 50f;

        [Header("Update")]
        [SerializeField] private UpdateTiming _updateTiming = UpdateTiming.LateUpdate;

        [Header("Vertex Collector")]
        [SerializeField] private CollectorType _collectorType = CollectorType.Cpu;
        [SerializeField] private ComputeShader _computeShader;

        private AutoFramingCamera _autoFramingCamera;
        private FramingSmoother _smoother;

        public Camera Camera { get => _camera; set => _camera = value; }
        public IList<Renderer> Targets => _targets;

        /// <summary>
        /// Discard internal filter state. Call after a discontinuous change in the framing
        /// target (e.g. target switch, scene teleport) so the next frame re-initializes
        /// from the new raw offsets without the speed-estimate carrying the artifact.
        /// </summary>
        public void ResetSmoothing() => _smoother?.Reset();

        private void Awake()
        {
            IMeshVertexCollector collector;
            if (_collectorType == CollectorType.ComputeShader && _computeShader != null)
            {
                collector = new ComputeShaderMeshVertexCollector(_computeShader);
            }
            else
            {
                collector = new MeshVertexCollector();
            }
            _autoFramingCamera = new AutoFramingCamera(collector, ownsVertexCollector: true);
            _smoother = new FramingSmoother();
        }

        private void OnDestroy()
        {
            _autoFramingCamera?.Dispose();
            _autoFramingCamera = null;
            _smoother = null;
        }

        private void Update()
        {
            if (_updateTiming == UpdateTiming.Update) Tick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (_updateTiming == UpdateTiming.LateUpdate) Tick(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (_updateTiming == UpdateTiming.FixedUpdate) Tick(Time.fixedDeltaTime);
        }

        private void Tick(float dt)
        {
            if (_autoFramingCamera == null || _smoother == null) return;
            if (_camera == null || _targets == null || _targets.Count == 0) return;

            _smoother.HorizontalMinCutoff = _horizontalMinCutoff;
            _smoother.HorizontalBeta = _horizontalBeta;
            _smoother.VerticalMinCutoff = _verticalMinCutoff;
            _smoother.VerticalBeta = _verticalBeta;
            _smoother.DeadZone = _deadZone;
            _smoother.MaxLinearSpeed = _maxLinearSpeed > 0f ? _maxLinearSpeed : float.PositiveInfinity;

            var margin = ScreenMargin.Uniform(_marginPercent, ScreenMarginUnit.Percentage);
            int width = Mathf.Max(1, _camera.pixelWidth > 0 ? _camera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, _camera.pixelHeight > 0 ? _camera.pixelHeight : Screen.height);

            _camera.transform.position = _smoother.ApplyAndRecompose(
                _autoFramingCamera, _camera, _targets, margin, width, height, dt);
        }
    }
}
