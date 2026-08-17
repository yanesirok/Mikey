using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Which edge/corner of a cloud's REST box stays fixed while it expands
    /// toward full cover (see <see cref="MapCloudMath.ComputeClosedLayout"/>).
    /// Fixed per cloud slot, not per preset — Left1/Left2 always anchor their
    /// left edge, Right1 its right edge, Bottom1 its bottom-right corner,
    /// matching how each was authored in the user's Canva composition.
    /// </summary>
    public enum CloudExpansionAnchor
    {
        LeftEdge,
        RightEdge,
        BottomRightCorner
    }

    /// <summary>
    /// Normalized (SOURCE-IMAGE-relative, same basis as MapMarkerLayout — see
    /// remarks on <see cref="MapCloudLayout"/>) placement/size/rotation/opacity
    /// for one decorative cloud element. X/Y/Width/Height may legitimately be
    /// negative or exceed 1 — the cloud art is deliberately larger than and
    /// bleeds past the viewport edges as part of its "framing" composition.
    /// </summary>
    public readonly struct CloudLayout
    {
        public readonly float NormalizedX;
        public readonly float NormalizedY;
        public readonly float NormalizedWidth;
        public readonly float NormalizedHeight;
        public readonly float RotationDegrees;
        public readonly float Opacity;

        public CloudLayout(float normalizedX, float normalizedY, float normalizedWidth, float normalizedHeight, float rotationDegrees, float opacity)
        {
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
            NormalizedWidth = normalizedWidth;
            NormalizedHeight = normalizedHeight;
            RotationDegrees = rotationDegrees;
            Opacity = opacity;
        }
    }

    /// <summary>One composition of all 4 cloud elements — a "rest" preset.</summary>
    public readonly struct MapCloudPreset
    {
        public readonly CloudLayout Left1;
        public readonly CloudLayout Left2;
        public readonly CloudLayout Right1;
        public readonly CloudLayout Bottom1;

        public MapCloudPreset(CloudLayout left1, CloudLayout left2, CloudLayout right1, CloudLayout bottom1)
        {
            Left1 = left1;
            Left2 = left2;
            Right1 = right1;
            Bottom1 = bottom1;
        }
    }

    /// <summary>
    /// ONE centralized source of truth for the 4 decorative cloud elements'
    /// placement, opacity, and expansion behavior (Map Pass 3B, corrected).
    ///
    /// <para>
    /// <b>Coordinate basis:</b> unlike the first cloud implementation, these
    /// coordinates are normalized against the SOURCE MAP IMAGE
    /// (SourceImageWidth x SourceImageHeight, same basis as
    /// MapMarkerLayout — the user's Canva reference canvases, 2046x868 for
    /// Japan and 2048x869.1 for Okinawa, share the source image's own
    /// 2.357:1 aspect ratio), NOT the on-screen viewport. Clouds are part of
    /// the painted map composition, not screen-fixed atmospheric HUD — they
    /// live as children of the SAME transformed ".pan-canvas" as the map art
    /// and markers (see Map.uss ".map-cloud-layer"), so they pan/zoom
    /// together with it, and a cloud's SOURCE-normalized rectangle is
    /// converted through the same cover-fit crop as a marker's coordinate
    /// (see MapCoordinateMapping.SourceRectToViewport) — never applied as a
    /// raw percentage of the viewport.
    /// </para>
    ///
    /// <para>
    /// <b>Rest vs. closed:</b> <see cref="JapanRest"/>/<see cref="OkinawaRest"/>
    /// are the user's exact final decorative compositions, applied whenever
    /// no transition is in flight. There is no longer a single hand-picked
    /// "Cover" preset — a transition's fully-closed state is COMPUTED per
    /// cloud from its own rest layout via <see cref="MapCloudMath.ComputeClosedLayout"/>,
    /// expanding by <see cref="CloseExpansionFactor"/> while keeping the
    /// cloud's own <see cref="CloudExpansionAnchor"/> edge/corner fixed —
    /// the dominant transition motion is the ink mass visibly spreading
    /// from its resting position, never a slide into the center.
    /// </para>
    /// </summary>
    public static class MapCloudLayout
    {
        /// <summary>Pixel dimensions of japan_map_final.jpg and okinawa_map_final.jpg — every stored coordinate below is normalized against these, matching MapMarkerLayout.</summary>
        public const float SourceImageWidth = MapMarkerLayout.SourceImageWidth;
        public const float SourceImageHeight = MapMarkerLayout.SourceImageHeight;

        /// <summary>
        /// How much each cloud's rest size grows at full close, derived from
        /// the user's Canva concept for Right1: rest width 1689.9 @ x1116.3
        /// (right edge 2806.2) expanding to width 2806.1 @ x0 (right edge
        /// 2806.1, i.e. the right edge stays effectively fixed) on the
        /// 2046x868 Japan reference canvas — 2806.1 / 1689.9 ~= 1.6605. The
        /// SAME factor is reused for all 4 clouds rather than a per-cloud
        /// guess, per the task's explicit instruction.
        /// </summary>
        public const float CloseExpansionFactor = 1.6605f;

        public const CloudExpansionAnchor Left1Anchor = CloudExpansionAnchor.LeftEdge;
        public const CloudExpansionAnchor Left2Anchor = CloudExpansionAnchor.LeftEdge;
        public const CloudExpansionAnchor Right1Anchor = CloudExpansionAnchor.RightEdge;
        public const CloudExpansionAnchor Bottom1Anchor = CloudExpansionAnchor.BottomRightCorner;

        /// <summary>Rest opacity is identical across both chapters' presets (see class remarks) — Left1 0.74, Left2 0.77, Right1 1.00 (fully opaque), Bottom1 0.66.</summary>
        private const float Left1RestOpacity = 0.74f;
        private const float Left2RestOpacity = 0.77f;
        private const float Right1RestOpacity = 1.00f;
        private const float Bottom1RestOpacity = 0.66f;

        public static readonly MapCloudPreset JapanRest = new MapCloudPreset(
            left1: new CloudLayout(-0.02781f, -0.22051f, 0.62014f, 0.72051f, 0f, Left1RestOpacity),
            left2: new CloudLayout(-0.02781f, -0.39320f, 0.35792f, 0.47454f, 0f, Left2RestOpacity),
            right1: new CloudLayout(0.54560f, -0.11901f, 0.82595f, 1.03076f, 0f, Right1RestOpacity),
            bottom1: new CloudLayout(0.38255f, 0.39631f, 0.85611f, 0.63445f, -180f, Bottom1RestOpacity));

        public static readonly MapCloudPreset OkinawaRest = new MapCloudPreset(
            left1: new CloudLayout(0.00000f, -0.22023f, 0.62007f, 0.72017f, 0f, Left1RestOpacity),
            left2: new CloudLayout(-0.01514f, -0.49937f, 0.35757f, 0.47394f, 0f, Left2RestOpacity),
            right1: new CloudLayout(0.61313f, -0.26602f, 0.82515f, 1.02946f, 0f, Right1RestOpacity),
            bottom1: new CloudLayout(0.45234f, 0.45702f, 0.85527f, 0.63364f, -180f, Bottom1RestOpacity));

        /// <summary>
        /// Converts a normalized SOURCE-IMAGE rectangle+opacity into a
        /// viewport percentage (via MapCoordinateMapping.SourceRectToViewport,
        /// accounting for the cover-fit crop) and writes it onto
        /// <paramref name="element"/> as style.left/top/width/height/rotate/
        /// opacity. <paramref name="viewportWidth"/>/<paramref name="viewportHeight"/>
        /// must be the CURRENT resolved size of the ".pan-canvas" element the
        /// cloud is a child of — NOT the source image's own dimensions.
        /// </summary>
        public static void Apply(VisualElement element, CloudLayout layout, float viewportWidth, float viewportHeight)
        {
            if (element == null)
                return;

            MapCoordinateMapping.SourceRectToViewport(
                layout.NormalizedX, layout.NormalizedY, layout.NormalizedWidth, layout.NormalizedHeight,
                SourceImageWidth, SourceImageHeight,
                viewportWidth, viewportHeight,
                out float vx, out float vy, out float vw, out float vh);

            element.style.left = new Length(vx * 100f, LengthUnit.Percent);
            element.style.top = new Length(vy * 100f, LengthUnit.Percent);
            element.style.width = new Length(vw * 100f, LengthUnit.Percent);
            element.style.height = new Length(vh * 100f, LengthUnit.Percent);
            element.style.rotate = new Rotate(new Angle(layout.RotationDegrees, AngleUnit.Degree));
            element.style.opacity = layout.Opacity;
        }

        /// <summary>Applies all 4 elements of a preset via <see cref="Apply"/>.</summary>
        public static void ApplyPreset(VisualElement left1, VisualElement left2, VisualElement right1, VisualElement bottom1, MapCloudPreset preset, float viewportWidth, float viewportHeight)
        {
            Apply(left1, preset.Left1, viewportWidth, viewportHeight);
            Apply(left2, preset.Left2, viewportWidth, viewportHeight);
            Apply(right1, preset.Right1, viewportWidth, viewportHeight);
            Apply(bottom1, preset.Bottom1, viewportWidth, viewportHeight);
        }
    }
}
