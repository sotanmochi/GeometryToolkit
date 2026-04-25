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

        [Header("Preset")]
        [SerializeField, Tooltip("Selecting a preset writes its values into the One-Euro / Output Clamps fields below. Tweak afterward to fine-tune.")]
        private FramingSmoothingPreset _preset = FramingSmoothingPreset.Standard;

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

        // Trail ring buffer (lazy-allocated when _showSceneTrail is enabled).
        private Vector3[] _rawTrail;
        private Vector3[] _smoothedTrail;
        private int _trailCount;
        private int _trailWriteIndex;

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
            _trailCount = 0;
            _trailWriteIndex = 0;
        }

        /// <summary>
        /// Apply a parameter preset to the One-Euro / Output Clamps fields. Equivalent to
        /// selecting the preset in the Inspector dropdown.
        /// </summary>
        public void ApplyPreset(FramingSmoothingPreset preset)
        {
            var values = FramingSmootherPresets.GetValues(preset);
            _horizontalMinCutoff = values.HorizontalMinCutoff;
            _verticalMinCutoff = values.VerticalMinCutoff;
            _horizontalBeta = values.HorizontalBeta;
            _verticalBeta = values.VerticalBeta;
            _deadZone = values.DeadZone;
            _maxLinearSpeed = float.IsPositiveInfinity(values.MaxLinearSpeed) ? 0f : values.MaxLinearSpeed;
            _preset = preset;
            _appliedPreset = preset;
        }

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
            _rawTrail = null;
            _smoothedTrail = null;
        }

        private void OnValidate()
        {
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

            Vector3 smoothedPosition = _smoother.ApplyAndRecompose(
                _autoFramingCamera, _camera, _targets, margin, width, height, dt);
            _camera.transform.position = smoothedPosition;

            if (_showSceneTrail) PushTrailSample(_smoother.LastRawPosition, smoothedPosition);
        }

        private void PushTrailSample(Vector3 raw, Vector3 smoothed)
        {
            int capacity = Mathf.Max(2, _trailSampleCount);
            if (_rawTrail == null || _rawTrail.Length != capacity)
            {
                _rawTrail = new Vector3[capacity];
                _smoothedTrail = new Vector3[capacity];
                _trailCount = 0;
                _trailWriteIndex = 0;
            }

            _rawTrail[_trailWriteIndex] = raw;
            _smoothedTrail[_trailWriteIndex] = smoothed;
            _trailWriteIndex = (_trailWriteIndex + 1) % capacity;
            if (_trailCount < capacity) _trailCount++;
        }

        private void OnGUI()
        {
            if (_showScreenFrame && _camera != null) DrawScreenFrames();
            if (!_showOnGuiOverlay || _smoother == null || !_smoother.HasLastFrame) return;

            var raw = _smoother.LastRawOffsets;
            var smoothed = _smoother.LastSmoothedOffsets;
            Vector3 rawPos = _smoother.LastRawPosition;
            Vector3 smoothedPos = _smoother.LastSmoothedPosition;
            float positionDelta = Vector3.Distance(rawPos, smoothedPos);

            const int Pad = 8;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = true };
            string text =
                $"<b>SmoothedFramingFollower</b>\n" +
                $"preset:    {_preset}    dt: {Time.deltaTime * 1000f:F2} ms\n" +
                $"  raw L/R/B/T:    {raw.left,7:F4} / {raw.right,7:F4} / {raw.bottom,7:F4} / {raw.top,7:F4}\n" +
                $"  smooth L/R/B/T: {smoothed.left,7:F4} / {smoothed.right,7:F4} / {smoothed.bottom,7:F4} / {smoothed.top,7:F4}\n" +
                $"  delta L/R/B/T:  {smoothed.left - raw.left,+7:F4} / {smoothed.right - raw.right,+7:F4} / {smoothed.bottom - raw.bottom,+7:F4} / {smoothed.top - raw.top,+7:F4}\n" +
                $"raw      pos:  ({rawPos.x,7:F3}, {rawPos.y,7:F3}, {rawPos.z,7:F3})\n" +
                $"smoothed pos:  ({smoothedPos.x,7:F3}, {smoothedPos.y,7:F3}, {smoothedPos.z,7:F3})    Δ = {positionDelta,6:F4} m\n" +
                $"DeadZone hit:  {_smoother.LastDeadZoneHit}    MaxSpeed clamp hit: {_smoother.LastMaxSpeedHit}";

            var rect = new Rect(Pad, Pad, 540, 130);
            GUI.Box(rect, text, style);
        }

        private void DrawScreenFrames()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);

            // (a) Margin target rectangle — the bounds the framing is supposed to fit inside.
            var margin = ScreenMargin.Uniform(_marginPercent, ScreenMarginUnit.Percentage);
            var (nLeft, nRight, nBottom, nTop) = margin.ToNdcBounds(width, height);
            Rect marginRect = NdcToGuiRect(nLeft, nRight, nBottom, nTop, width, height);
            DrawGuiRectBorder(marginRect, new Color(1f, 1f, 1f, 0.7f), 2f);

            // (b) Actual subject screen extent — bounding rectangle of all input vertices
            // projected to screen via the (post-smoothing) camera position. Shows whether the
            // subject is still inside the margin despite the smoothing lag.
            if (_autoFramingCamera == null) return;
            var verts = _autoFramingCamera.WorldVertices;
            if (!verts.IsCreated || verts.Length == 0) return;

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            int validCount = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 sp = _camera.WorldToScreenPoint(verts[i]);
                if (sp.z <= 0f) continue;  // skip points behind the camera
                if (sp.x < minX) minX = sp.x;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.y > maxY) maxY = sp.y;
                validCount++;
            }
            if (validCount == 0) return;

            // Camera.WorldToScreenPoint returns y measured from the bottom; OnGUI's Rect
            // measures y from the top — flip accordingly.
            Rect subjectRect = new Rect(minX, height - maxY, maxX - minX, maxY - minY);
            DrawGuiRectBorder(subjectRect, new Color(1f, 0.85f, 0.15f, 0.85f), 2f);
        }

        private static Rect NdcToGuiRect(float nLeft, float nRight, float nBottom, float nTop,
                                        int screenWidth, int screenHeight)
        {
            float pixelLeft = (nLeft + 1f) * 0.5f * screenWidth;
            float pixelRight = (nRight + 1f) * 0.5f * screenWidth;
            float pixelBottom = (nBottom + 1f) * 0.5f * screenHeight;
            float pixelTop = (nTop + 1f) * 0.5f * screenHeight;
            // Flip y for GUI coords (y from top).
            return new Rect(pixelLeft, screenHeight - pixelTop, pixelRight - pixelLeft, pixelTop - pixelBottom);
        }

        private static Texture2D s_lineTexture;
        private static Texture2D LineTexture
        {
            get
            {
                if (s_lineTexture == null)
                {
                    s_lineTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    s_lineTexture.SetPixel(0, 0, Color.white);
                    s_lineTexture.Apply();
                }
                return s_lineTexture;
            }
        }

        private static void DrawGuiRectBorder(Rect r, Color color, float thickness)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), LineTexture);                     // top
            GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), LineTexture);      // bottom
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), LineTexture);                    // left
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), LineTexture);     // right
            GUI.color = prev;
        }

        private void OnDrawGizmos()
        {
            if (!_showSceneTrail || _trailCount < 2) return;

            // Walk from oldest to newest, drawing each line strip.
            int capacity = _rawTrail.Length;
            int oldest = (_trailWriteIndex - _trailCount + capacity) % capacity;

            Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.85f); // raw — yellow
            Vector3 prevRaw = _rawTrail[oldest];
            Gizmos.color = new Color(0.25f, 1f, 0.5f, 0.85f);  // smoothed — green
            Vector3 prevSmoothed = _smoothedTrail[oldest];

            for (int i = 1; i < _trailCount; i++)
            {
                int idx = (oldest + i) % capacity;
                Vector3 raw = _rawTrail[idx];
                Vector3 smoothed = _smoothedTrail[idx];

                Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.85f);
                Gizmos.DrawLine(prevRaw, raw);
                Gizmos.color = new Color(0.25f, 1f, 0.5f, 0.85f);
                Gizmos.DrawLine(prevSmoothed, smoothed);

                prevRaw = raw;
                prevSmoothed = smoothed;
            }

            // Mark the latest sample with small spheres.
            int latestIdx = (_trailWriteIndex - 1 + capacity) % capacity;
            Gizmos.color = new Color(1f, 0.85f, 0.15f);
            Gizmos.DrawSphere(_rawTrail[latestIdx], 0.03f);
            Gizmos.color = new Color(0.25f, 1f, 0.5f);
            Gizmos.DrawSphere(_smoothedTrail[latestIdx], 0.03f);
        }
    }
}
