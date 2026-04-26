using UnityEngine;

namespace GeometryToolkit.CameraFraming.Smoothing
{
    internal static class FramingDebugOverlay
    {
        public static void Draw(
            FramingSmoother smoother,
            FramingSmoothingPreset preset,
            FramingScreenFrameCache frameCache,
            bool showScreenFrame,
            float deltaTimeSeconds)
        {
            var raw = smoother.LastRawOffsets;
            var smoothed = smoother.LastSmoothedOffsets;
            Vector3 rawPosition = smoother.LastRawPosition;
            Vector3 smoothedPosition = smoother.LastSmoothedPosition;
            float positionDelta = Vector3.Distance(rawPosition, smoothedPosition);

            string frameInfo = "";
            if (showScreenFrame)
            {
                frameInfo =
                    $"<color=#FFD835>raw frame  (yellow):</color> {FormatRect(frameCache.RawExtentRect, frameCache.HasRawExtent)}\n" +
                    $"<color=#40FF80>smth frame (green):</color>  {FormatRect(frameCache.SmoothedExtentRect, frameCache.HasSmoothedExtent)}\n" +
                    $"<color=#FFFFFF>margin frame (white):</color>{FormatRect(frameCache.MarginRect, true)}\n";
            }

            string text =
                $"<b>SmoothedFramingFollower</b>\n" +
                $"preset:    {preset}    dt: {deltaTimeSeconds * 1000f:F2} ms\n" +
                $"  raw L/R/B/T:    {raw.Left,7:F4} / {raw.Right,7:F4} / {raw.Bottom,7:F4} / {raw.Top,7:F4}\n" +
                $"  smooth L/R/B/T: {smoothed.Left,7:F4} / {smoothed.Right,7:F4} / {smoothed.Bottom,7:F4} / {smoothed.Top,7:F4}\n" +
                $"  delta L/R/B/T:  {smoothed.Left - raw.Left,+7:F4} / {smoothed.Right - raw.Right,+7:F4} / {smoothed.Bottom - raw.Bottom,+7:F4} / {smoothed.Top - raw.Top,+7:F4}\n" +
                $"raw      pos:  ({rawPosition.x,7:F3}, {rawPosition.y,7:F3}, {rawPosition.z,7:F3})\n" +
                $"smoothed pos:  ({smoothedPosition.x,7:F3}, {smoothedPosition.y,7:F3}, {smoothedPosition.z,7:F3})    Δ = {positionDelta,6:F4} m\n" +
                $"DeadZone hit:  {smoother.LastDeadZoneHit}    MaxSpeed clamp hit: {smoother.LastMaxSpeedHit}\n" +
                frameInfo;

            const int padding = 8;
            int height = string.IsNullOrEmpty(frameInfo) ? 130 : 180;
            var rect = new Rect(padding, padding, 580, height);
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                richText = true,
            };
            GUI.Box(rect, text, style);
        }

        private static string FormatRect(Rect rect, bool valid)
        {
            return valid
                ? $"x={rect.x,5:F0} y={rect.y,5:F0} w={rect.width,5:F0} h={rect.height,5:F0}"
                : "(invalid)";
        }
    }
}
