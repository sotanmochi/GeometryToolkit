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

        // Computed once per Tick.
        //
        //   raw extent (yellow): the subject's actual screen footprint under the smoothed
        //     camera, computed by projecting every input vertex through the smoothed
        //     view-projection matrix and taking the bounding rect. This is the exact answer
        //     for "where the character is on screen right now" — it ignores any depth
        //     approximation and matches the rendered character's pixel extents.
        //
        //   smoothed extent (green): the smoothing target rectangle — where the smoothing
        //     system aims to fit the subject. Derived from the smoothed offsets: under the
        //     smoothed camera at OBF-local (pr, pu, pf), an offset L for the left edge maps
        //     to NDC x = (L - pr) / (kH * pf). For tight axes this collapses to the margin
        //     by construction; for slack axes it shows the recentered position the smoother
        //     resolved to. Symmetric for the other 3 edges.
        //
        // We can't use the offset → NDC formula for raw extents because raw offsets are not
        // satisfied by the smoothed camera position — the touching plane projects to a slanted
        // (depth-dependent) line under that camera, so any single-depth approximation (f=0 or
        // otherwise) drifts visibly from the actual subject extents, especially when the subject
        // has significant depth variation on the slack axis.
        private void UpdateScreenFrameCache(ScreenMargin margin, int width, int height)
        {
            var (nLeft, nRight, nBottom, nTop) = margin.ToNdcBounds(width, height);
            _cachedMarginRect = NdcToGuiRect(nLeft, nRight, nBottom, nTop, width, height);

            _cachedSmoothedExtentValid = false;
            _cachedRawExtentValid = false;
            if (_autoFramingCamera == null || _smoother == null || !_smoother.HasLastFrame) return;

            // Raw extent: project the subject vertices through a smoothed-camera VP matrix
            // built from the smoother's recorded LastSmoothedPosition (matches the camera
            // position we just applied, independent of any Unity transform-cache state).
            var verts = _autoFramingCamera.WorldVertices;
            if (verts.IsCreated && verts.Length > 0)
            {
                Matrix4x4 smoothedW2C = ComputeWorldToCameraMatrix(_smoother.LastSmoothedPosition, _camera.transform);
                Matrix4x4 smoothedVP = _camera.projectionMatrix * smoothedW2C;
                if (TryComputeProjectedRect(verts, smoothedVP, width, height, out _cachedRawExtentRect))
                {
                    _cachedRawExtentValid = true;
                }
            }

            // Smoothed extent: closed-form smoothing target derived from smoothed offsets.
            var obf = _autoFramingCamera.BoundingFrustum;
            if (obf.PointCount > 0)
            {
                Vector3 toRef = _smoother.LastSmoothedPosition - obf.ReferencePoint;
                float pr = Vector3.Dot(obf.Right, toRef);
                float pu = Vector3.Dot(obf.Up, toRef);
                float pf = -Vector3.Dot(obf.Forward, toRef);
                if (pf > 0f)
                {
                    float fovYRad = _camera.fieldOfView * Mathf.Deg2Rad;
                    float kV = Mathf.Tan(fovYRad * 0.5f);
                    float kH = kV * _camera.aspect;
                    _cachedSmoothedExtentRect = OffsetsToGuiRect(
                        _smoother.LastSmoothedOffsets, pr, pu, pf, kH, kV, width, height);
                    _cachedSmoothedExtentValid = true;
                }
            }
        }

        private static Rect OffsetsToGuiRect(
            (float left, float right, float bottom, float top) offsets,
            float pr, float pu, float pf, float kH, float kV,
            int width, int height)
        {
            float invKHPF = 1f / (kH * pf);
            float invKVPF = 1f / (kV * pf);
            float ndcLeft = (offsets.left - pr) * invKHPF;
            float ndcRight = (offsets.right - pr) * invKHPF;
            float ndcBottom = (offsets.bottom - pu) * invKVPF;
            float ndcTop = (offsets.top - pu) * invKVPF;
            return NdcToGuiRect(ndcLeft, ndcRight, ndcBottom, ndcTop, width, height);
        }

        // Project all valid vertices through `vp` and return their screen-space bounding rect
        // (in GUI coords — y from top). Direct 4x4 matrix multiplication beats Camera.WorldToScreenPoint
        // by 3-5x for high-vertex SkinnedMesh targets.
        private static bool TryComputeProjectedRect(NativeArray<Vector3> verts, Matrix4x4 vp,
                                                   int width, int height, out Rect rect)
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
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
                if (sx < minX) minX = sx;
                if (sx > maxX) maxX = sx;
                if (sy < minY) minY = sy;
                if (sy > maxY) maxY = sy;
                validCount++;
            }
            if (validCount == 0) { rect = default; return false; }
            // Flip y for GUI coords (y from top).
            rect = new Rect(minX, height - maxY, maxX - minX, maxY - minY);
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
