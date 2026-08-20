using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Direct behavioral contract for MapCloudLayout (Map Pass 3D, cleanup):
    /// the one centralized source of truth for the 4 decorative cloud
    /// elements' STATIC resting compositions and rest opacity — there is no
    /// expansion/closed state any more (see
    /// MapCloudTransitionControllerSourceTests for proof the transition is
    /// camera-only). Exact-value tests guard against silent drift away from
    /// the user's supplied Canva reference layouts, mirroring
    /// MapMarkerLayoutTests' exact-coordinate tests for the same reason.
    /// </summary>
    public class MapCloudLayoutTests
    {
        private const float Tolerance = 0.00005f;

        // ---------- Japan rest: exact values from the user's Canva reference (2046x868) ----------

        [Test]
        public void JapanRest_Left1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Left1, -0.02781f, -0.22051f, 0.62014f, 0.72051f, 0f, 0.74f);
        }

        [Test]
        public void JapanRest_Left2_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Left2, -0.02781f, -0.39320f, 0.35792f, 0.47454f, 0f, 0.77f);
        }

        [Test]
        public void JapanRest_Right1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Right1, 0.54560f, -0.11901f, 0.82595f, 1.03076f, 0f, 1.00f);
        }

        [Test]
        public void JapanRest_Bottom1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.JapanRest.Bottom1, 0.38255f, 0.39631f, 0.85611f, 0.63445f, -180f, 0.66f);
        }

        // ---------- Okinawa rest: exact values from the user's Canva reference (2048x869.1) ----------

        [Test]
        public void OkinawaRest_Left1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Left1, 0.00000f, -0.22023f, 0.62007f, 0.72017f, 0f, 0.74f);
        }

        [Test]
        public void OkinawaRest_Left2_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Left2, -0.01514f, -0.49937f, 0.35757f, 0.47394f, 0f, 0.77f);
        }

        [Test]
        public void OkinawaRest_Right1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Right1, 0.61313f, -0.26602f, 0.82515f, 1.02946f, 0f, 1.00f);
        }

        [Test]
        public void OkinawaRest_Bottom1_MatchesCanvaReference()
        {
            AssertCloud(MapCloudLayout.OkinawaRest.Bottom1, 0.45234f, 0.45702f, 0.85527f, 0.63364f, -180f, 0.66f);
        }

        // ---------- rest opacities (section 4/17/18 of the correction spec) ----------

        [Test]
        public void RestOpacity_IsIdenticalAcrossBothChapters()
        {
            Assert.AreEqual(MapCloudLayout.JapanRest.Left1.Opacity, MapCloudLayout.OkinawaRest.Left1.Opacity);
            Assert.AreEqual(MapCloudLayout.JapanRest.Left2.Opacity, MapCloudLayout.OkinawaRest.Left2.Opacity);
            Assert.AreEqual(MapCloudLayout.JapanRest.Right1.Opacity, MapCloudLayout.OkinawaRest.Right1.Opacity);
            Assert.AreEqual(MapCloudLayout.JapanRest.Bottom1.Opacity, MapCloudLayout.OkinawaRest.Bottom1.Opacity);
        }

        [Test]
        public void Right1_IsFullyOpaqueAtRest()
        {
            Assert.AreEqual(1.00f, MapCloudLayout.JapanRest.Right1.Opacity, Tolerance);
            Assert.AreEqual(1.00f, MapCloudLayout.OkinawaRest.Right1.Opacity, Tolerance);
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

        // ---------- no expansion/closed-state concept remains (Map Pass 3D cleanup) ----------

        [Test]
        public void MapCloudLayout_HasNoCloseExpansionFactor_NoClosedStateConceptRemains()
        {
            string source = System.IO.File.ReadAllText("Assets/UI/Map/MapCloudLayout.cs");
            StringAssert.DoesNotContain("CloseExpansionFactor", source);
            StringAssert.DoesNotContain("CloudExpansionAnchor", source);
            StringAssert.DoesNotContain("Anchor", source);
        }

        private static void AssertCloud(CloudLayout cloud, float x, float y, float w, float h, float rotation, float opacity)
        {
            Assert.AreEqual(x, cloud.NormalizedX, Tolerance);
            Assert.AreEqual(y, cloud.NormalizedY, Tolerance);
            Assert.AreEqual(w, cloud.NormalizedWidth, Tolerance);
            Assert.AreEqual(h, cloud.NormalizedHeight, Tolerance);
            Assert.AreEqual(rotation, cloud.RotationDegrees, Tolerance);
            Assert.AreEqual(opacity, cloud.Opacity, Tolerance);
        }
    }
}
