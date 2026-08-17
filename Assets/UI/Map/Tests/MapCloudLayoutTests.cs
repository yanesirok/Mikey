using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Direct behavioral contract for MapCloudLayout (Map Pass 3B): the one
    /// centralized source of truth for the 4 decorative cloud elements'
    /// resting compositions. Exact-value tests guard against silent drift
    /// away from the user's supplied Canva reference layouts, mirroring
    /// MapMarkerLayoutTests' exact-coordinate tests for the same reason.
    /// </summary>
    public class MapCloudLayoutTests
    {
        private const float Tolerance = 0.00005f;

        // ---------- every preset has exactly 4 named cloud definitions ----------

        [Test]
        public void JapanRest_HasExactlyFourCloudDefinitions()
        {
            var preset = MapCloudLayout.JapanRest;
            // MapCloudPreset's shape (Left1/Left2/Right1/Bottom1, no more, no
            // fewer) is itself the "exactly 4" guarantee — this just proves
            // all 4 fields are populated, non-default, distinct entries.
            Assert.AreNotEqual(default(CloudLayout), preset.Left1);
            Assert.AreNotEqual(default(CloudLayout), preset.Left2);
            Assert.AreNotEqual(default(CloudLayout), preset.Right1);
            Assert.AreNotEqual(default(CloudLayout), preset.Bottom1);
        }

        [Test]
        public void OkinawaRest_HasExactlyFourCloudDefinitions()
        {
            var preset = MapCloudLayout.OkinawaRest;
            Assert.AreNotEqual(default(CloudLayout), preset.Left1);
            Assert.AreNotEqual(default(CloudLayout), preset.Left2);
            Assert.AreNotEqual(default(CloudLayout), preset.Right1);
            Assert.AreNotEqual(default(CloudLayout), preset.Bottom1);
        }

        // ---------- Japan rest: exact values from the user's Canva reference (2046x868) ----------

        [Test]
        public void JapanRest_Left1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Left1, -0.02781f, -0.22051f, 0.62014f, 0.72051f, 0f);
        }

        [Test]
        public void JapanRest_Left2_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Left2, -0.02781f, -0.39320f, 0.35792f, 0.47454f, 0f);
        }

        [Test]
        public void JapanRest_Right1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Right1, 0.54560f, -0.11901f, 0.82595f, 1.03076f, 0f);
        }

        [Test]
        public void JapanRest_Bottom1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Bottom1, 0.38255f, 0.39631f, 0.85611f, 0.63445f, -180f);
        }

        // ---------- Okinawa rest: exact values from the user's Canva reference (2048x869.1) ----------

        [Test]
        public void OkinawaRest_Left1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Left1, 0.00000f, -0.22023f, 0.62007f, 0.72017f, 0f);
        }

        [Test]
        public void OkinawaRest_Left2_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Left2, -0.01514f, -0.49937f, 0.35757f, 0.47394f, 0f);
        }

        [Test]
        public void OkinawaRest_Right1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Right1, 0.61313f, -0.26602f, 0.82515f, 1.02946f, 0f);
        }

        [Test]
        public void OkinawaRest_Bottom1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Bottom1, 0.45234f, 0.45702f, 0.85527f, 0.63364f, -180f);
        }

        // ---------- rest layouts intentionally extend past the viewport ----------

        [Test]
        public void NegativeNormalizedPositions_ArePreserved_NotClamped()
        {
            Assert.Less(MapCloudLayout.JapanRest.Left1.NormalizedX, 0f);
            Assert.Less(MapCloudLayout.JapanRest.Left1.NormalizedY, 0f);
            Assert.Less(MapCloudLayout.OkinawaRest.Left2.NormalizedX, 0f);
            Assert.Less(MapCloudLayout.OkinawaRest.Right1.NormalizedY, 0f);
        }

        [Test]
        public void SizesAboveOne_ArePreserved_NotClamped()
        {
            Assert.Greater(MapCloudLayout.JapanRest.Right1.NormalizedHeight, 1f);
            Assert.Greater(MapCloudLayout.OkinawaRest.Right1.NormalizedHeight, 1f);
        }

        // ---------- bottom cloud is always flipped ----------

        [Test]
        public void BottomCloud_Rotation_IsMinus180Degrees_InBothPresets()
        {
            Assert.AreEqual(-180f, MapCloudLayout.JapanRest.Bottom1.RotationDegrees);
            Assert.AreEqual(-180f, MapCloudLayout.OkinawaRest.Bottom1.RotationDegrees);
        }

        [Test]
        public void NonBottomClouds_HaveZeroRotation_InBothPresets()
        {
            Assert.AreEqual(0f, MapCloudLayout.JapanRest.Left1.RotationDegrees);
            Assert.AreEqual(0f, MapCloudLayout.JapanRest.Left2.RotationDegrees);
            Assert.AreEqual(0f, MapCloudLayout.JapanRest.Right1.RotationDegrees);
            Assert.AreEqual(0f, MapCloudLayout.OkinawaRest.Left1.RotationDegrees);
            Assert.AreEqual(0f, MapCloudLayout.OkinawaRest.Left2.RotationDegrees);
            Assert.AreEqual(0f, MapCloudLayout.OkinawaRest.Right1.RotationDegrees);
        }

        // ---------- the derived cover layout provides full coverage, not a scattered guess ----------

        [Test]
        public void Cover_Right1AloneSpansTheFullViewportHeight()
        {
            // The primary covering cloud: its box alone must reach from at or
            // above y=0 to at or below y=1, so height coverage never depends
            // on perfect alignment between multiple clouds.
            var right1 = MapCloudLayout.Cover.Right1;
            Assert.LessOrEqual(right1.NormalizedY, 0f);
            Assert.GreaterOrEqual(right1.NormalizedY + right1.NormalizedHeight, 1f);
        }

        [Test]
        public void Cover_Left1AndRight1Together_SpanTheFullViewportWidth_WithNoGap()
        {
            var left1 = MapCloudLayout.Cover.Left1;
            var right1 = MapCloudLayout.Cover.Right1;
            Assert.LessOrEqual(left1.NormalizedX, 0f, "Left1 must start at or before the left edge.");
            Assert.GreaterOrEqual(right1.NormalizedX + right1.NormalizedWidth, 1f, "Right1 must end at or after the right edge.");
            Assert.LessOrEqual(right1.NormalizedX, left1.NormalizedX + left1.NormalizedWidth,
                "Right1 must start before Left1's right edge, so their boxes overlap with no gap between them.");
        }

        [Test]
        public void Cover_PreservesRestingRotation_NoRotationAnimation()
        {
            Assert.AreEqual(0f, MapCloudLayout.Cover.Left1.RotationDegrees);
            Assert.AreEqual(0f, MapCloudLayout.Cover.Left2.RotationDegrees);
            Assert.AreEqual(0f, MapCloudLayout.Cover.Right1.RotationDegrees);
            Assert.AreEqual(-180f, MapCloudLayout.Cover.Bottom1.RotationDegrees);
        }

        [Test]
        public void Cover_SizesAreOnlyASubtleIncreaseOverRest_NeverMoreThan30Percent()
        {
            // "A small scale increase... is acceptable only if required... keep
            // any scale change subtle" — guards against an implementation that
            // scales clouds up dramatically instead of primarily moving them.
            AssertSubtleScale(MapCloudLayout.JapanRest.Left1, MapCloudLayout.Cover.Left1);
            AssertSubtleScale(MapCloudLayout.JapanRest.Left2, MapCloudLayout.Cover.Left2);
            AssertSubtleScale(MapCloudLayout.JapanRest.Right1, MapCloudLayout.Cover.Right1);
            AssertSubtleScale(MapCloudLayout.JapanRest.Bottom1, MapCloudLayout.Cover.Bottom1);
        }

        private static void AssertSubtleScale(CloudLayout rest, CloudLayout cover)
        {
            Assert.LessOrEqual(cover.NormalizedWidth / rest.NormalizedWidth, 1.3f);
            Assert.LessOrEqual(cover.NormalizedHeight / rest.NormalizedHeight, 1.3f);
        }

        private static void AssertCloud(CloudLayout cloud, float x, float y, float w, float h, float rotation)
        {
            Assert.AreEqual(x, cloud.NormalizedX, Tolerance);
            Assert.AreEqual(y, cloud.NormalizedY, Tolerance);
            Assert.AreEqual(w, cloud.NormalizedWidth, Tolerance);
            Assert.AreEqual(h, cloud.NormalizedHeight, Tolerance);
            Assert.AreEqual(rotation, cloud.RotationDegrees, Tolerance);
        }
    }
}
