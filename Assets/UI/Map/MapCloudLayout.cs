using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Normalized (viewport-relative, NOT source-image-relative — see remarks
    /// on <see cref="MapCloudLayout"/>) placement/size/rotation for one
    /// decorative cloud element. X/Y/Width/Height may legitimately be
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

        public CloudLayout(float normalizedX, float normalizedY, float normalizedWidth, float normalizedHeight, float rotationDegrees)
        {
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
            NormalizedWidth = normalizedWidth;
            NormalizedHeight = normalizedHeight;
            RotationDegrees = rotationDegrees;
        }
    }

    /// <summary>One composition of all 4 cloud elements — a "rest" preset or the shared "cover" preset.</summary>
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
    /// placement (Map Pass 3B). Unlike <see cref="MapMarkerLayout"/>, these
    /// coordinates are normalized directly against the on-screen map content
    /// VIEWPORT (the same box as ".pan-stage"/".map-cloud-layer"), not the
    /// source map image — clouds are foreground decoration that must NOT
    /// pan/zoom with the map art (see Map.uss ".map-cloud-layer", which is a
    /// sibling of ".pan-stage" inside ".map-root", never a child of the
    /// transformed ".pan-canvas"). The user's Canva reference canvases
    /// (2046x868 for Japan, 2048x869.1 for Okinawa) happen to share the
    /// source image's aspect ratio, but the values below are already
    /// converted to plain 0-1 viewport fractions — apply them directly as
    /// percentage style.left/top/width/height, no further conversion.
    ///
    /// Three presets: <see cref="JapanRest"/> and <see cref="OkinawaRest"/>
    /// are the exact user-supplied decorative resting compositions (applied
    /// whenever no transition is in flight); <see cref="Cover"/> is ONE
    /// derived, shared full-coverage composition used as the transition
    /// midpoint for both directions (see MapCloudTransitionController) — not
    /// user-supplied, since the task only specifies resting layouts.
    /// </summary>
    public static class MapCloudLayout
    {
        public static readonly MapCloudPreset JapanRest = new MapCloudPreset(
            left1: new CloudLayout(-0.02781f, -0.22051f, 0.62014f, 0.72051f, 0f),
            left2: new CloudLayout(-0.02781f, -0.39320f, 0.35792f, 0.47454f, 0f),
            right1: new CloudLayout(0.54560f, -0.11901f, 0.82595f, 1.03076f, 0f),
            bottom1: new CloudLayout(0.38255f, 0.39631f, 0.85611f, 0.63445f, -180f));

        public static readonly MapCloudPreset OkinawaRest = new MapCloudPreset(
            left1: new CloudLayout(0.00000f, -0.22023f, 0.62007f, 0.72017f, 0f),
            left2: new CloudLayout(-0.01514f, -0.49937f, 0.35757f, 0.47394f, 0f),
            right1: new CloudLayout(0.61313f, -0.26602f, 0.82515f, 1.02946f, 0f),
            bottom1: new CloudLayout(0.45234f, 0.45702f, 0.85527f, 0.63364f, -180f));

        /// <summary>
        /// Derived full-coverage composition — movement toward center is the
        /// primary covering mechanism (matching each cloud's rest-relative
        /// direction hint: Left1 left-to-inward-right, Left2 upper-left-to-
        /// inward-down-right, Right1 right-to-inward-left, Bottom1 bottom-
        /// to-inward-up), with only a subtle uniform size increase (not a
        /// resting-layout change) for coverage margin. Right1 alone already
        /// spans the full viewport height and most of its width; Left1 fills
        /// the remaining left edge; Left2/Bottom1 add central overlap so the
        /// seam between Left1/Right1 never reads as two flat panels meeting.
        /// Rotation is preserved from rest (Bottom1 stays -180deg) — no
        /// rotation animation.
        /// </summary>
        public static readonly MapCloudPreset Cover = new MapCloudPreset(
            left1: new CloudLayout(0.00000f, 0.00000f, 0.71000f, 0.83000f, 0f),
            left2: new CloudLayout(0.29000f, 0.28000f, 0.41000f, 0.55000f, 0f),
            right1: new CloudLayout(0.18000f, -0.09000f, 0.95000f, 1.19000f, 0f),
            bottom1: new CloudLayout(0.01000f, 0.14000f, 0.98000f, 0.73000f, -180f));

        /// <summary>Writes a normalized viewport-relative layout onto an element as percentage style.left/top/width/height plus a rotate transform.</summary>
        public static void Apply(VisualElement element, CloudLayout layout)
        {
            if (element == null)
                return;
            element.style.left = new Length(layout.NormalizedX * 100f, LengthUnit.Percent);
            element.style.top = new Length(layout.NormalizedY * 100f, LengthUnit.Percent);
            element.style.width = new Length(layout.NormalizedWidth * 100f, LengthUnit.Percent);
            element.style.height = new Length(layout.NormalizedHeight * 100f, LengthUnit.Percent);
            element.style.rotate = new Rotate(new Angle(layout.RotationDegrees, AngleUnit.Degree));
        }

        /// <summary>Applies all 4 elements of a preset via <see cref="Apply"/>.</summary>
        public static void ApplyPreset(VisualElement left1, VisualElement left2, VisualElement right1, VisualElement bottom1, MapCloudPreset preset)
        {
            Apply(left1, preset.Left1);
            Apply(left2, preset.Left2);
            Apply(right1, preset.Right1);
            Apply(bottom1, preset.Bottom1);
        }
    }
}
