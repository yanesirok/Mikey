namespace Mikey.UI.Map
{
    /// <summary>
    /// Pure clamping math behind the Map's pan/zoom interaction, kept free of
    /// UnityEngine/UI Toolkit so it can be exercised directly in EditMode tests
    /// (mirrors <c>Mikey.UI.Audio.AudioSettingsStore</c>'s clamp-only pure logic).
    /// <see cref="MapPanZoomController"/> applies these clamped values to a
    /// pannable ".*-canvas" transform, shared by both the Japan world map and the
    /// Okinawa chapter map.
    ///
    /// <para>
    /// <b>Why <see cref="MinZoom"/> is 1 regardless of viewport/image aspect
    /// ratio:</b> the canvas art (".map-canvas-art" / ".okinawa-canvas-art")
    /// renders with <c>-unity-background-scale-mode: scale-and-crop</c> against
    /// the canvas's own laid-out box, which always exactly matches the viewport
    /// (the canvas is <c>inset: 0</c> within the viewport). "scale-and-crop" is a
    /// cover-fit — by construction it already scales/crops the source image so it
    /// fully covers that box with zero gaps, for ANY combination of image and
    /// viewport aspect ratio (that is the entire point of a cover fit). The
    /// separate <c>transform.scale</c> this class' clamp feeds
    /// (<see cref="MapPanZoomController"/>'s zoom) is applied ON TOP of that
    /// already-covering render, purely as a visual magnification — so scaling
    /// below 1 shrinks an already-viewport-covering box, which is exactly what
    /// exposes empty background around it. 1 is therefore the true minimum safe
    /// zoom, independent of aspect ratio; there is no additional aspect-ratio
    /// term to compute here because the cover-fit already absorbed it.
    /// </para>
    ///
    /// <para>
    /// <b>Why pan range must depend on zoom:</b> at zoom <c>z</c>, the
    /// (centered) canvas box is <c>z * viewportDimension</c> along each axis, so
    /// the excess beyond the viewport is <c>(z - 1) * viewportDimension</c>,
    /// split evenly on both sides. Panning further than half that excess on
    /// either side would slide the box's edge past the viewport, revealing
    /// background — see <see cref="MaxPanForZoom"/>. At <c>z == MinZoom == 1</c>
    /// there is zero excess, so pan must clamp to exactly 0.
    /// </para>
    /// </summary>
    public static class MapPanZoomMath
    {
        /// <summary>
        /// The minimum zoom that still keeps the map art covering the whole
        /// viewport, for any viewport or image aspect ratio (see class remarks).
        /// </summary>
        public const float MinZoom = 1f;

        public const float MaxZoom = 2.5f;

        /// <summary>How far above <see cref="MinZoom"/> a map starts by default, so it reads as noticeably close/immersive rather than a bare cover-fit with zero pan room.</summary>
        public const float InitialZoomMultiplier = 1.4f;

        /// <summary>The default zoom on a fresh entry to a map screen — deliberately above <see cref="MinZoom"/>, see <see cref="InitialZoomMultiplier"/>.</summary>
        public const float DefaultZoom = MinZoom * InitialZoomMultiplier;

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

        /// <summary>
        /// The furthest a single axis may be panned (in either direction) at the
        /// given zoom before the canvas's covering edge would move inward of the
        /// viewport edge and expose background — half the zoomed canvas's excess
        /// over the viewport on that axis. 0 at or below <see cref="MinZoom"/>.
        /// </summary>
        public static float MaxPanForZoom(float zoom, float viewportDimension)
        {
            if (!IsFinite(zoom) || !IsFinite(viewportDimension) || viewportDimension <= 0f)
                return 0f;

            float excess = (zoom - 1f) * viewportDimension;
            return excess > 0f ? excess * 0.5f : 0f;
        }

        /// <summary>Clamps a single pan axis to +/- <see cref="MaxPanForZoom"/> for the current zoom and viewport dimension.</summary>
        public static float ClampPan(float pan, float zoom, float viewportDimension)
        {
            if (!IsFinite(pan))
                return 0f;

            float max = MaxPanForZoom(zoom, viewportDimension);
            return Clamp(pan, -max, max);
        }

        /// <summary>
        /// Decelerating (ease-out cubic) progress curve for the opening zoom
        /// animation: fast at first, settling smoothly by <paramref name="t"/> = 1.
        /// <paramref name="t"/> is clamped to [0, 1]; <c>EaseOutCubic(0) == 0</c>,
        /// <c>EaseOutCubic(1) == 1</c>, strictly increasing and always decelerating
        /// in between (no bounce/overshoot).
        /// </summary>
        public static float EaseOutCubic(float t)
        {
            float clamped = Clamp(IsFinite(t) ? t : 0f, 0f, 1f);
            float inv = 1f - clamped;
            return 1f - inv * inv * inv;
        }

        /// <summary>
        /// Symmetric ease-in-out cubic: slow start, fast middle, slow finish
        /// — no bounce/overshoot/elastic. Used by the Japan&lt;-&gt;Okinawa
        /// chapter transition's approach/settle camera motion (see
        /// MapPanZoomController.AnimateViewToSourceFocalPoint), a distinct
        /// animation from the plain per-screen opening zoom above (which
        /// deliberately stays decelerate-only via <see cref="EaseOutCubic"/>,
        /// since it always starts from a dead stop at MinZoom rather than
        /// from an arbitrary in-progress view). <paramref name="t"/> is
        /// clamped to [0, 1]; EaseInOutCubic(0) == 0, EaseInOutCubic(1) == 1,
        /// EaseInOutCubic(0.5) == 0.5, monotonically increasing, never
        /// overshoots.
        /// </summary>
        public static float EaseInOutCubic(float t)
        {
            float clamped = Clamp(IsFinite(t) ? t : 0f, 0f, 1f);
            return clamped < 0.5f
                ? 4f * clamped * clamped * clamped
                : 1f - Pow3(-2f * clamped + 2f) * 0.5f;
        }

        private static float Pow3(float v) => v * v * v;

        /// <summary>
        /// The pan offset (one axis) that places a given point of the
        /// UNSCALED canvas (<paramref name="canvasNormalized"/>, 0-1 against
        /// the canvas's own box before any zoom/pan is applied — e.g. from
        /// <see cref="Mikey.UI.Map.MapCoordinateMapping.SourceToViewport"/>)
        /// at a given target position within the viewport
        /// (<paramref name="targetNormalized"/>, 0-1, 0.5 = dead center),
        /// given the canvas is scaled by <paramref name="zoom"/> around its
        /// own center (the transform pivot <see cref="Mikey.UI.Map.MapPanZoomController"/>
        /// already relies on). Not clamped — callers should still clamp via
        /// <see cref="ClampPan"/> so an off-canvas or extreme target never
        /// exposes background. Used both for the Japan world map's opening
        /// "focus on the current chapter" framing (target != 0.5, a
        /// deliberate comfortable offset) and for reproducing a captured
        /// view across a Japan&lt;-&gt;Okinawa cloud transition (target == 0.5,
        /// dead center, an exact carry-over — see
        /// MapCloudTransitionController).
        /// </summary>
        public static float PanForTarget(float canvasNormalized, float targetNormalized, float zoom, float viewportDimension)
        {
            if (!IsFinite(canvasNormalized) || !IsFinite(targetNormalized) || !IsFinite(zoom) || !IsFinite(viewportDimension))
                return 0f;
            return viewportDimension * ((targetNormalized - 0.5f) + zoom * (0.5f - canvasNormalized));
        }

        /// <summary>
        /// Inverse of <see cref="PanForTarget"/> for the case
        /// <c>targetNormalized == 0.5</c> (viewport dead center): given the
        /// CURRENT pan/zoom, which point of the unscaled canvas currently
        /// sits at the viewport's exact center. Used to capture "what is the
        /// player currently looking at" before a cloud transition (see
        /// MapCloudTransitionController).
        /// </summary>
        public static float CanvasNormalizedAtViewportCenter(float pan, float zoom, float viewportDimension)
        {
            if (!IsFinite(pan) || !IsFinite(zoom) || zoom == 0f || !IsFinite(viewportDimension) || viewportDimension == 0f)
                return 0.5f;
            return 0.5f - pan / (zoom * viewportDimension);
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
