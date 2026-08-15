namespace Mikey.UI.Map
{
    /// <summary>
    /// Pure clamping math behind the Map's pan/zoom interaction, kept free of
    /// UnityEngine/UI Toolkit so it can be exercised directly in EditMode tests
    /// (mirrors <c>Mikey.UI.Audio.AudioSettingsStore</c>'s clamp-only pure logic).
    /// <see cref="MapPanZoomController"/> applies these clamped values to the
    /// pannable ".map-canvas" transform. Deliberately simple: zoom always pivots
    /// around the canvas's own center (its default USS transform-origin, which
    /// matches the viewport center since the canvas fills it) — no zoom-around-
    /// cursor/pinch-point math — and panning is clamped by a fixed fraction of the
    /// viewport size rather than pixel-exact content-edge alignment. A reliable,
    /// smooth system over a pixel-perfect one, per the mobile-map rebuild's brief.
    /// </summary>
    public static class MapPanZoomMath
    {
        public const float MinZoom = 0.6f;
        public const float MaxZoom = 2.5f;
        public const float DefaultZoom = 1f;

        /// <summary>
        /// Fraction of the viewport dimension the canvas may be panned beyond the
        /// viewport edge before clamping — a simple, robust heuristic (not
        /// pixel-perfect content-edge alignment) so the map can never be dragged
        /// away entirely, at any zoom level.
        /// </summary>
        public const float MaxPanFractionOfViewport = 0.6f;

        /// <summary>Clamps a zoom value into [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]; non-finite input falls back to <see cref="DefaultZoom"/>.</summary>
        public static float ClampZoom(float zoom)
        {
            if (!IsFinite(zoom))
                return DefaultZoom;
            return Clamp(zoom, MinZoom, MaxZoom);
        }

        /// <summary>Applies a wheel/pinch zoom delta to <paramref name="currentZoom"/> and returns the newly clamped zoom. Positive delta zooms in.</summary>
        public static float ApplyZoomDelta(float currentZoom, float delta)
        {
            float baseZoom = IsFinite(currentZoom) ? currentZoom : DefaultZoom;
            float appliedDelta = IsFinite(delta) ? delta : 0f;
            return ClampZoom(baseZoom + appliedDelta);
        }

        /// <summary>Clamps a single pan axis to +/- <see cref="MaxPanFractionOfViewport"/> of the corresponding viewport dimension.</summary>
        public static float ClampPan(float pan, float viewportDimension)
        {
            if (!IsFinite(pan))
                return 0f;
            if (!IsFinite(viewportDimension) || viewportDimension <= 0f)
                return 0f;

            float max = viewportDimension * MaxPanFractionOfViewport;
            return Clamp(pan, -max, max);
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
