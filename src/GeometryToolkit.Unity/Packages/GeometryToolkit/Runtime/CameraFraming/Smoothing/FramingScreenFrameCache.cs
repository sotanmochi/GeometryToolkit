using Unity.Collections;
using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    internal sealed class FramingScreenFrameCache
    {
        private static Texture2D s_lineTexture;

        public Rect MarginRect { get; private set; }
        public Rect RawExtentRect { get; private set; }
        public Rect SmoothedExtentRect { get; private set; }
        public bool HasRawExtent { get; private set; }
        public bool HasSmoothedExtent { get; private set; }

        public void Clear()
        {
            MarginRect = default;
            RawExtentRect = default;
            SmoothedExtentRect = default;
            HasRawExtent = false;
            HasSmoothedExtent = false;
        }

        public void Update(
            AutoFramingCamera autoFramingCamera,
            Camera camera,
            Vector3 smoothedPosition,
            FramingOffsets rawOffsets,
            FramingOffsets smoothedOffsets,
            ScreenMargin margin,
            int width,
            int height)
        {
            var (nLeft, nRight, nBottom, nTop) = margin.ToNdcBounds(width, height);
            MarginRect = NdcToGuiRect(nLeft, nRight, nBottom, nTop, width, height);

            HasRawExtent = false;
            HasSmoothedExtent = false;
            if (autoFramingCamera == null || camera == null) return;

            var vertices = autoFramingCamera.WorldVertices;
            if (!vertices.IsCreated || vertices.Length == 0) return;

            Matrix4x4 worldToCamera = ComputeWorldToCameraMatrix(smoothedPosition, camera.transform);
            Matrix4x4 viewProjection = camera.projectionMatrix * worldToCamera;
            float fovYRad = camera.fieldOfView * Mathf.Deg2Rad;
            float kVertical = Mathf.Tan(fovYRad * 0.5f);
            float kHorizontal = kVertical * ((float)width / height);

            if (TryComputeRawAndSmoothedRects(
                    vertices,
                    viewProjection,
                    width,
                    height,
                    kHorizontal,
                    kVertical,
                    rawOffsets,
                    smoothedOffsets,
                    out var rawRect,
                    out var smoothedRect))
            {
                RawExtentRect = rawRect;
                SmoothedExtentRect = smoothedRect;
                HasRawExtent = true;
                HasSmoothedExtent = true;
            }
        }

        public void Draw()
        {
            DrawGuiRectBorder(MarginRect, new Color(1f, 1f, 1f, 0.7f), 2f);
            if (HasRawExtent)
            {
                DrawGuiRectBorder(RawExtentRect, new Color(1f, 0.85f, 0.15f, 0.85f), 2f);
            }
            if (HasSmoothedExtent)
            {
                DrawGuiRectBorder(SmoothedExtentRect, new Color(0.25f, 1f, 0.5f, 0.85f), 2f);
            }
        }

        private static bool TryComputeRawAndSmoothedRects(
            NativeArray<Vector3> vertices,
            Matrix4x4 viewProjection,
            int width,
            int height,
            float kHorizontal,
            float kVertical,
            FramingOffsets rawOffsets,
            FramingOffsets smoothedOffsets,
            out Rect rawRect,
            out Rect smoothedRect)
        {
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            float clipWAtMinX = 1f;
            float clipWAtMaxX = 1f;
            float clipWAtMinY = 1f;
            float clipWAtMaxY = 1f;

            int validCount = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                float clipW = viewProjection.m30 * vertex.x + viewProjection.m31 * vertex.y + viewProjection.m32 * vertex.z + viewProjection.m33;
                if (clipW <= 0f) continue;

                float clipX = viewProjection.m00 * vertex.x + viewProjection.m01 * vertex.y + viewProjection.m02 * vertex.z + viewProjection.m03;
                float clipY = viewProjection.m10 * vertex.x + viewProjection.m11 * vertex.y + viewProjection.m12 * vertex.z + viewProjection.m13;
                float inverseW = 1f / clipW;
                float screenX = (clipX * inverseW + 1f) * halfWidth;
                float screenY = (clipY * inverseW + 1f) * halfHeight;

                if (screenX < minX) { minX = screenX; clipWAtMinX = clipW; }
                if (screenX > maxX) { maxX = screenX; clipWAtMaxX = clipW; }
                if (screenY < minY) { minY = screenY; clipWAtMinY = clipW; }
                if (screenY > maxY) { maxY = screenY; clipWAtMaxY = clipW; }
                validCount++;
            }

            if (validCount == 0)
            {
                rawRect = default;
                smoothedRect = default;
                return false;
            }

            rawRect = new Rect(minX, height - maxY, maxX - minX, maxY - minY);

            float deltaLeft = (smoothedOffsets.Left - rawOffsets.Left) * halfWidth / (kHorizontal * clipWAtMinX);
            float deltaRight = (smoothedOffsets.Right - rawOffsets.Right) * halfWidth / (kHorizontal * clipWAtMaxX);
            float deltaBottom = (smoothedOffsets.Bottom - rawOffsets.Bottom) * halfHeight / (kVertical * clipWAtMinY);
            float deltaTop = (smoothedOffsets.Top - rawOffsets.Top) * halfHeight / (kVertical * clipWAtMaxY);

            float smoothedMinX = minX + deltaLeft;
            float smoothedMaxX = maxX + deltaRight;
            float smoothedMinY = minY + deltaBottom;
            float smoothedMaxY = maxY + deltaTop;
            smoothedRect = new Rect(
                smoothedMinX,
                height - smoothedMaxY,
                smoothedMaxX - smoothedMinX,
                smoothedMaxY - smoothedMinY);
            return true;
        }

        private static Matrix4x4 ComputeWorldToCameraMatrix(Vector3 position, Transform transform)
        {
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            Vector3 forward = transform.forward;
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m00 = right.x;
            matrix.m01 = right.y;
            matrix.m02 = right.z;
            matrix.m03 = -Vector3.Dot(right, position);
            matrix.m10 = up.x;
            matrix.m11 = up.y;
            matrix.m12 = up.z;
            matrix.m13 = -Vector3.Dot(up, position);
            matrix.m20 = -forward.x;
            matrix.m21 = -forward.y;
            matrix.m22 = -forward.z;
            matrix.m23 = Vector3.Dot(forward, position);
            return matrix;
        }

        private static Rect NdcToGuiRect(
            float nLeft,
            float nRight,
            float nBottom,
            float nTop,
            int screenWidth,
            int screenHeight)
        {
            float pixelLeft = (nLeft + 1f) * 0.5f * screenWidth;
            float pixelRight = (nRight + 1f) * 0.5f * screenWidth;
            float pixelBottom = (nBottom + 1f) * 0.5f * screenHeight;
            float pixelTop = (nTop + 1f) * 0.5f * screenHeight;
            return new Rect(pixelLeft, screenHeight - pixelTop, pixelRight - pixelLeft, pixelTop - pixelBottom);
        }

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

        private static void DrawGuiRectBorder(Rect rect, Color color, float thickness)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), LineTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), LineTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), LineTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), LineTexture);
            GUI.color = previous;
        }
    }
}
