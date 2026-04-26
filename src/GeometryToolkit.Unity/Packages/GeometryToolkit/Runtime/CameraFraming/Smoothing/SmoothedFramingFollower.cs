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

        [Header("Smoothing")]
        [SerializeField] private FramingSmoothingSettings _smoothingSettings = FramingSmoothingSettings.CreateDefault();

        [Header("Preset")]
        [SerializeField, Tooltip("Selecting a preset writes its values into the One-Euro / Output Clamps fields below. Tweak afterward to fine-tune.")]
        private FramingSmoothingPreset _preset = FramingSmoothingPreset.Standard;

        [Header("Update")]
        [SerializeField] private UpdateTiming _updateTiming = UpdateTiming.LateUpdate;

        [Header("Vertex Collector")]
        [SerializeField] private CollectorType _collectorType = CollectorType.Cpu;
        [SerializeField] private ComputeShader _computeShader;

        [Header("Debug Visualization")]
        [SerializeField, Tooltip("Show numeric overlay of raw / smoothed offsets and clamp state in the Game view.")]
        private bool _showOnGuiOverlay = false;

        [SerializeField, Tooltip("Draw the screen-space margin rectangle (white) and the actual subject bounding rectangle (yellow) in the Game view. Lets you see whether smoothing causes the subject to leak outside the requested margin.")]
        private bool _showScreenFrame = false;

        [SerializeField, Tooltip("Show raw vs smoothed camera position trail in the Scene view.")]
        private bool _showSceneTrail = false;

        [SerializeField, Min(2), Tooltip("Number of trail samples kept in the ring buffer (~5s @ 60fps for 300).")]
        private int _trailSampleCount = 300;

        // Tracks the last preset value applied via OnValidate so individual field edits don't
        // re-trigger preset application. Hidden because it's an implementation detail.
        [SerializeField, HideInInspector] private FramingSmoothingPreset _appliedPreset = FramingSmoothingPreset.Standard;

        private AutoFramingCamera _autoFramingCamera;
        private FramingSmoother _smoother;
        private readonly FramingTrailBuffer _trailBuffer = new();
        private readonly FramingScreenFrameCache _screenFrameCache = new();

        public Camera Camera { get => _camera; set => _camera = value; }
        public IList<Renderer> Targets => _targets;
        public FramingSmoother Smoother => _smoother;

        /// <summary>
        /// Discard internal filter state. Call after a discontinuous change in the framing
        /// target (e.g. target switch, scene teleport) so the next frame re-initializes
        /// from the new raw offsets without the speed-estimate carrying the artifact.
        /// </summary>
        public void ResetSmoothing()
        {
            _smoother?.Reset();
            _trailBuffer.Clear();
            _screenFrameCache.Clear();
        }

        /// <summary>
        /// Apply a parameter preset to the One-Euro / Output Clamps fields. Equivalent to
        /// selecting the preset in the Inspector dropdown.
        /// </summary>
        public void ApplyPreset(FramingSmoothingPreset preset)
        {
            _smoothingSettings = FramingSmoothingPresetCatalog.Get(preset);
            _preset = preset;
            _appliedPreset = preset;
        }

        private void Awake()
        {
            EnsureSmoothingSettingsInitialized();
            _autoFramingCamera = new AutoFramingCamera(CreateVertexCollector(), ownsVertexCollector: true);
            _smoother = new FramingSmoother { Settings = _smoothingSettings };
        }

        private void OnDestroy()
        {
            _autoFramingCamera?.Dispose();
            _autoFramingCamera = null;
            _smoother = null;
        }

        private void OnValidate()
        {
            EnsureSmoothingSettingsInitialized();
            // Only apply preset values when the dropdown actually changed — leave manual tweaks alone.
            if (_preset != _appliedPreset)
            {
                ApplyPreset(_preset);
            }
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
            if (!TryPrepareTick(out var margin, out int width, out int height))
            {
                _screenFrameCache.Clear();
                return;
            }

            _smoother.Settings = _smoothingSettings;

            FramingOffsets rawOffsets = _autoFramingCamera.ComputeFramingOffsets(_camera, _targets, margin, width, height);
            FramingOffsets smoothedOffsets = _smoother.SmoothOffsets(rawOffsets, dt);
            Vector3 rawPosition = _autoFramingCamera.RecomposeCameraPosition(_camera, rawOffsets, margin, width, height);
            Vector3 smoothedPosition = _autoFramingCamera.RecomposeCameraPosition(_camera, smoothedOffsets, margin, width, height);
            FramingSmoothingResult result = _smoother.FinalizeFrame(
                rawOffsets,
                smoothedOffsets,
                rawPosition,
                smoothedPosition,
                dt);
            _camera.transform.position = result.SmoothedPosition;

            if (_showSceneTrail)
            {
                _trailBuffer.Push(result.RawPosition, result.SmoothedPosition, _trailSampleCount);
            }

            if (_showScreenFrame)
            {
                _screenFrameCache.Update(
                    _autoFramingCamera,
                    _camera,
                    result.SmoothedPosition,
                    result.RawOffsets,
                    result.SmoothedOffsets,
                    margin,
                    width,
                    height);
            }
            else
            {
                _screenFrameCache.Clear();
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            if (_showScreenFrame && _camera != null)
            {
                _screenFrameCache.Draw();
            }
            if (_showOnGuiOverlay && _smoother != null && _smoother.HasLastFrame)
            {
                FramingDebugOverlay.Draw(_smoother, _preset, _screenFrameCache, _showScreenFrame, Time.deltaTime);
            }
        }

        private bool TryPrepareTick(out ScreenMargin margin, out int width, out int height)
        {
            margin = default;
            width = 0;
            height = 0;
            if (_autoFramingCamera == null || _smoother == null) return false;
            if (_camera == null || _targets == null || _targets.Count == 0) return false;

            margin = ScreenMargin.Uniform(_marginPercent, ScreenMarginUnit.Percentage);
            width = Mathf.Max(1, _camera.pixelWidth > 0 ? _camera.pixelWidth : Screen.width);
            height = Mathf.Max(1, _camera.pixelHeight > 0 ? _camera.pixelHeight : Screen.height);
            return true;
        }

        private IMeshVertexCollector CreateVertexCollector()
        {
            if (_collectorType == CollectorType.ComputeShader && _computeShader != null)
            {
                return new ComputeShaderMeshVertexCollector(_computeShader);
            }
            return new MeshVertexCollector();
        }

        private void EnsureSmoothingSettingsInitialized()
        {
            FramingSmoothingSettings defaultSettings = FramingSmoothingSettings.CreateDefault();
            if (_smoothingSettings.Equals(default(FramingSmoothingSettings)))
            {
                _smoothingSettings = defaultSettings;
            }
            else if (_smoothingSettings.DerivativeCutoff <= 0f)
            {
                _smoothingSettings.DerivativeCutoff = defaultSettings.DerivativeCutoff;
            }
        }

        private void OnDrawGizmos()
        {
            if (!_showSceneTrail) return;
            _trailBuffer.DrawGizmos();
        }
    }
}
