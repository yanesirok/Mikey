namespace Mikey.UI.Map
{
    /// <summary>
    /// Pure math converting a normalized SOURCE-IMAGE coordinate (e.g.
    /// MapMarkerLayout's chapter/mission positions, normalized against the
    /// source JPG's own pixel bounds) into a normalized VIEWPORT coordinate
    /// (0-1 against the ".pan-canvas" box a marker is actually percentage-
    /// positioned within) — kept free of UnityEngine/UI Toolkit so it can be
    /// exercised directly in EditMode tests (mirrors <see cref="MapPanZoomMath"/>).
    ///
    /// <para>
    /// <b>Why this conversion is needed:</b> the map art (".map-canvas-art" /
    /// ".okinawa-canvas-art") renders with "-unity-background-scale-mode:
    /// scale-and-crop" — a cover fit — against a canvas box that always
    /// exactly matches the on-screen viewport (see <see cref="MapPanZoomMath"/>'s
    /// remarks on why MinZoom == 1). Whenever the viewport's aspect ratio
    /// differs from the source image's own aspect ratio, the cover fit
    /// scales the image up and crops it on ONE axis to eliminate gaps on the
    /// OTHER axis. A marker's SOURCE-normalized coordinate must be
    /// re-projected through that same scale+crop before it's a valid
    /// VIEWPORT percentage — applying the raw source percentage directly
    /// (the previous bug) is only correct on whichever single axis happens
    /// not to be cropped for the current viewport's aspect ratio, which
    /// silently "works" for X or for Y depending on whether the viewport
    /// being tested is wider or narrower than the source image.
    /// </para>
    /// </summary>
    public static class MapCoordinateMapping
    {
        /// <summary>
        /// Converts one normalized source-image point into a normalized
        /// viewport point, accounting for a cover-fit (scale-and-crop)
        /// display of that source image within the viewport. X and Y are
        /// computed together since cover-fit's single shared scale factor —
        /// <c>max(viewportWidth / sourceWidth, viewportHeight / sourceHeight)</c>
        /// — depends on both axes' dimensions at once. Falls back to an
        /// identity mapping (source normalized == viewport normalized,
        /// today's actual pre-fix behavior) for degenerate/not-yet-laid-out
        /// input rather than dividing by zero or producing NaN.
        /// </summary>
        public static void SourceToViewport(
            float sourceNormalizedX, float sourceNormalizedY,
            float sourceWidth, float sourceHeight,
            float viewportWidth, float viewportHeight,
            out float viewportNormalizedX, out float viewportNormalizedY)
        {
            if (!IsFinite(sourceNormalizedX) || !IsFinite(sourceNormalizedY)
                || !IsPositiveFinite(sourceWidth) || !IsPositiveFinite(sourceHeight)
                || !IsPositiveFinite(viewportWidth) || !IsPositiveFinite(viewportHeight))
            {
                viewportNormalizedX = sourceNormalizedX;
                viewportNormalizedY = sourceNormalizedY;
                return;
            }

            float scale = Max(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
            float renderedWidth = sourceWidth * scale;
            float renderedHeight = sourceHeight * scale;

            // Cover-fit centers the scaled image within the viewport by
            // default (no background-position override anywhere in
            // Map.uss), so the crop excess on each axis splits evenly
            // between both sides.
            float renderedPixelX = sourceNormalizedX * renderedWidth - (renderedWidth - viewportWidth) * 0.5f;
            float renderedPixelY = sourceNormalizedY * renderedHeight - (renderedHeight - viewportHeight) * 0.5f;

            viewportNormalizedX = renderedPixelX / viewportWidth;
            viewportNormalizedY = renderedPixelY / viewportHeight;
        }

        private static float Max(float a, float b) => a > b ? a : b;

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool IsPositiveFinite(float v) => IsFinite(v) && v > 0f;
    }
}
