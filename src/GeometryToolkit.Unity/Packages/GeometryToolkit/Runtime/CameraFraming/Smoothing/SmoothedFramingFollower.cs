using System.Collections.Generic;
using Unity.Collections;
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
        [SerializeField, Tooltip("Master switch. When off, the One-Euro filter and the dead-zone / max-speed clamps are bypassed: smoothed values match raw values, and the camera tracks the unfiltered framing target each frame. Useful for verifying the visualization baseline.")]
        private bool _smoothingEnabled = true;

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

        // Screen-frame visualization cache. Recomputed once per Tick (when _showScreenFrame is on)
        // so OnGUI — which fires multiple times per frame for different EventType — doesn't repeat
        // the per-vertex projection work.
        private Rect _cachedMarginRect;
        private Rect _cachedSmoothedExtentRect;  // subject extent under the smoothed (applied) camera
        private Rect _cachedRawExtentRect;       // subject extent under the raw camera position (filter-off baseline)
        private bool _cachedSmoothedExtentValid;
        private bool _cachedRawExtentValid;

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

        // REVIEWING
        private void Tick(float dt)
        {
            if (_autoFramingCamera == null || _smoother == null) return;
            if (_camera == null || _targets == null || _targets.Count == 0) return;

            _smoother.Enabled = _smoothingEnabled;
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

            // DEBUGGING
            if (_showSceneTrail) PushTrailSample(_smoother.LastRawPosition, smoothedPosition);
            if (_showScreenFrame) UpdateScreenFrameCache(margin, width, height);
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

        // Drawing is gated to EventType.Repaint — OnGUI fires once per Layout, once per Repaint,
        // and additionally for input events (MouseMove, KeyDown, …). Drawing on every event
        // amplifies the per-frame cost 3-5x for no visual benefit.
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            if (_showScreenFrame && _camera != null) DrawCachedScreenFrames();
            if (_showOnGuiOverlay && _smoother != null && _smoother.HasLastFrame) DrawDebugOverlay();
        }

        private void DrawDebugOverlay()
        {
            var raw = _smoother.LastRawOffsets;
            var smoothed = _smoother.LastSmoothedOffsets;
            Vector3 rawPos = _smoother.LastRawPosition;
            Vector3 smoothedPos = _smoother.LastSmoothedPosition;
            float positionDelta = Vector3.Distance(rawPos, smoothedPos);

            const int Pad = 8;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = true };

            string frameInfo = "";
            if (_showScreenFrame)
            {
                string FormatRect(Rect r, bool valid)
                    => valid ? $"x={r.x,5:F0} y={r.y,5:F0} w={r.width,5:F0} h={r.height,5:F0}" : "(invalid)";
                frameInfo =
                    $"<color=#FFD835>raw frame  (yellow):</color> {FormatRect(_cachedRawExtentRect, _cachedRawExtentValid)}\n" +
                    $"<color=#40FF80>smth frame (green):</color>  {FormatRect(_cachedSmoothedExtentRect, _cachedSmoothedExtentValid)}\n" +
                    $"<color=#FFFFFF>margin frame (white):</color>{FormatRect(_cachedMarginRect, true)}\n";
            }

            string text =
                $"<b>SmoothedFramingFollower</b>\n" +
                $"preset:    {_preset}    dt: {Time.deltaTime * 1000f:F2} ms\n" +
                $"  raw L/R/B/T:    {raw.left,7:F4} / {raw.right,7:F4} / {raw.bottom,7:F4} / {raw.top,7:F4}\n" +
                $"  smooth L/R/B/T: {smoothed.left,7:F4} / {smoothed.right,7:F4} / {smoothed.bottom,7:F4} / {smoothed.top,7:F4}\n" +
                $"  delta L/R/B/T:  {smoothed.left - raw.left,+7:F4} / {smoothed.right - raw.right,+7:F4} / {smoothed.bottom - raw.bottom,+7:F4} / {smoothed.top - raw.top,+7:F4}\n" +
                $"raw      pos:  ({rawPos.x,7:F3}, {rawPos.y,7:F3}, {rawPos.z,7:F3})\n" +
                $"smoothed pos:  ({smoothedPos.x,7:F3}, {smoothedPos.y,7:F3}, {smoothedPos.z,7:F3})    Δ = {positionDelta,6:F4} m\n" +
                $"DeadZone hit:  {_smoother.LastDeadZoneHit}    MaxSpeed clamp hit: {_smoother.LastMaxSpeedHit}\n" +
                frameInfo;

            int height = string.IsNullOrEmpty(frameInfo) ? 130 : 180;
            var rect = new Rect(Pad, Pad, 580, height);
            GUI.Box(rect, text, style);
        }

        // DEBUGGING
        private void DrawCachedScreenFrames()
        {
            // DrawGuiRectBorder(_cachedMarginRect, new Color(1f, 1f, 1f, 0.7f), 2f);
            DrawGuiRectBorder(_cachedMarginRect, new Color(1f, 0f, 0f, 0.7f), 2f);
            // Match the camera-trail color scheme: yellow = filter-off (raw), green = filter-on (smoothed).
            if (_cachedRawExtentValid)
            {
                DrawGuiRectBorder(_cachedRawExtentRect, new Color(1f, 0.85f, 0.15f, 0.85f), 2f);
            }
            if (_cachedSmoothedExtentValid)
            {
                DrawGuiRectBorder(_cachedSmoothedExtentRect, new Color(0.25f, 1f, 0.5f, 0.85f), 2f);
            }
        }

        // Computed once per Tick. Both raw and smoothed extent rects are derived from the same
        // single vertex-projection pass under the smoothed camera, so they coincide exactly
        // when raw offsets equal smoothed offsets (smoothing disabled or stationary subject).
        //
        //   raw extent (yellow): the subject's actual screen footprint under the smoothed
        //     camera. Bounding rect of all vertices projected through the smoothed VP matrix.
        //     Exact match for the rendered character's pixel extents.
        //
        //   smoothed extent (green): the same yellow rect, with each edge shifted by the
        //     smoothing-induced offset delta (sm_X − raw_X). The shift is converted from
        //     OBF-local meters to screen pixels using the actual depth (clip.w) of that edge's
        //     extreme vertex — this preserves the perspective correction so the formula
        //     reduces to (green = yellow) exactly when sm_X = raw_X for all 4 edges.
        //
        //     Derivation (left edge): pretend the leftmost extreme vertex P_left had offset
        //     sm_L instead of raw_L while keeping its depth f(P_left). Its NDC x shifts by
        //     (sm_L − raw_L) / (kH * (f(P_left) + pf_s)) = (sm_L − raw_L) / (kH * cw_at_left),
        //     i.e. (sm_L − raw_L) * halfW / (kH * cw_at_left) in screen pixels.
        private void UpdateScreenFrameCache(ScreenMargin margin, int width, int height)
        {
            var (nLeft, nRight, nBottom, nTop) = margin.ToNdcBounds(width, height);
            _cachedMarginRect = NdcToGuiRect(nLeft, nRight, nBottom, nTop, width, height);

            _cachedSmoothedExtentValid = false;
            _cachedRawExtentValid = false;
            if (_autoFramingCamera == null || _smoother == null || !_smoother.HasLastFrame) return;

            var verts = _autoFramingCamera.WorldVertices;
            if (!verts.IsCreated || verts.Length == 0) return;

            Matrix4x4 smoothedW2C = ComputeWorldToCameraMatrix(_smoother.LastSmoothedPosition, _camera.transform);
            Matrix4x4 smoothedVP = _camera.projectionMatrix * smoothedW2C;
            float fovYRad = _camera.fieldOfView * Mathf.Deg2Rad;
            float kV = Mathf.Tan(fovYRad * 0.5f);
            float kH = kV * _camera.aspect;

            if (TryComputeRawAndSmoothedRects(
                    verts, smoothedVP, width, height, kH, kV,
                    _smoother.LastRawOffsets, _smoother.LastSmoothedOffsets,
                    out _cachedRawExtentRect, out _cachedSmoothedExtentRect))
            {
                _cachedRawExtentValid = true;
                _cachedSmoothedExtentValid = true;
            }
        }

        // Single vertex-projection pass that returns both the raw bounding rect (= yellow)
        // and the smoothed-offset-shifted rect (= green). Tracking the per-edge clip.w lets
        // green inherit yellow's exact depth-aware screen position and shift only by the
        // smoothing-introduced offset delta — guaranteeing green == yellow when sm == raw.
        private static bool TryComputeRawAndSmoothedRects(
            NativeArray<Vector3> verts, Matrix4x4 vp,
            int width, int height, float kH, float kV,
            (float left, float right, float bottom, float top) raw,
            (float left, float right, float bottom, float top) smoothed,
            out Rect rawRect, out Rect smoothedRect)
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            // clip.w of the vertex that contributed to each extreme — used to perspective-
            // correct the smoothing-delta shift below. Initialized to 1 only as a defensive
            // value; they are overwritten the first time a valid vertex updates the extreme.
            float cwAtMinX = 1f, cwAtMaxX = 1f, cwAtMinY = 1f, cwAtMaxY = 1f;

            int validCount = 0;
            int count = verts.Length;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = verts[i];
                float cw = vp.m30 * v.x + vp.m31 * v.y + vp.m32 * v.z + vp.m33;
                if (cw <= 0f) continue;  // behind camera
                float cx = vp.m00 * v.x + vp.m01 * v.y + vp.m02 * v.z + vp.m03;
                float cy = vp.m10 * v.x + vp.m11 * v.y + vp.m12 * v.z + vp.m13;
                float invW = 1f / cw;
                float sx = (cx * invW + 1f) * halfW;
                float sy = (cy * invW + 1f) * halfH;
                if (sx < minX) { minX = sx; cwAtMinX = cw; }
                if (sx > maxX) { maxX = sx; cwAtMaxX = cw; }
                if (sy < minY) { minY = sy; cwAtMinY = cw; }
                if (sy > maxY) { maxY = sy; cwAtMaxY = cw; }
                validCount++;
            }
            if (validCount == 0) { rawRect = default; smoothedRect = default; return false; }

            // Yellow — flip y for GUI coords (y from top).
            rawRect = new Rect(minX, height - maxY, maxX - minX, maxY - minY);

            // Green — yellow + per-edge perspective-corrected shift. Δ = 0 ⇒ green == yellow.
            float deltaLeft = (smoothed.left - raw.left) * halfW / (kH * cwAtMinX);
            float deltaRight = (smoothed.right - raw.right) * halfW / (kH * cwAtMaxX);
            float deltaBottom = (smoothed.bottom - raw.bottom) * halfH / (kV * cwAtMinY);
            float deltaTop = (smoothed.top - raw.top) * halfH / (kV * cwAtMaxY);

            float smMinX = minX + deltaLeft;
            float smMaxX = maxX + deltaRight;
            float smMinY = minY + deltaBottom;
            float smMaxY = maxY + deltaTop;
            smoothedRect = new Rect(smMinX, height - smMaxY, smMaxX - smMinX, smMaxY - smMinY);
            return true;
        }

        // Build worldToCameraMatrix following Unity's GL convention (camera looks down -Z in
        // camera space) for an arbitrary position with the supplied transform's orientation.
        // Avoids mutating the live Transform just to read the matrix.
        private static Matrix4x4 ComputeWorldToCameraMatrix(Vector3 pos, Transform t)
        {
            Vector3 r = t.right;
            Vector3 u = t.up;
            Vector3 f = t.forward;
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = r.x;  m.m01 = r.y;  m.m02 = r.z;  m.m03 = -Vector3.Dot(r, pos);
            m.m10 = u.x;  m.m11 = u.y;  m.m12 = u.z;  m.m13 = -Vector3.Dot(u, pos);
            m.m20 = -f.x; m.m21 = -f.y; m.m22 = -f.z; m.m23 = Vector3.Dot(f, pos);
            return m;
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
