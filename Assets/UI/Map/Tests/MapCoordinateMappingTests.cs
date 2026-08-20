using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for <see cref="MapCoordinateMapping"/>: a source-image-normalized
    /// coordinate must be re-projected through the viewport's cover-fit
    /// crop (see the class remarks) to land at the correct viewport
    /// percentage. Exercised directly against the real chapter coordinates
    /// (MapMarkerLayout.Chapters) at two representative viewport sizes with
    /// deliberately different aspect ratios relative to the 6336x2688 source
    /// image (2.3571:1) — one narrower (crops horizontally), one wider
    /// (crops vertically) — so this doesn't only happen to work at one
    /// resolution. Expected values were computed independently in Python
    /// (double precision) and cross-checked against this class's float
    /// implementation.
    /// </summary>
    public class MapCoordinateMappingTests
    {
        private const float SourceWidth = MapMarkerLayout.SourceImageWidth;
        private const float SourceHeight = MapMarkerLayout.SourceImageHeight;
        private const float Tolerance = 0.0005f;

        // A landscape-phone-like viewport NARROWER than the source image's
        // 2.3571:1 aspect (2340/1080 = 2.1667) — cover-fit is
        // height-constrained, so it crops HORIZONTALLY; Y should come out
        // unchanged from the source value, X should not.
        private const float NarrowViewportWidth = 2340f;
        private const float NarrowViewportHeight = 1080f;

        // A wide window/editor-viewport-like size WIDER than the source
        // image's aspect (2560/960 = 2.6667) — cover-fit is
        // width-constrained, so it crops VERTICALLY; X should come out
        // unchanged from the source value, Y should not. This is the
        // regime that produced the reported "Y visibly too high" bug.
        private const float WideViewportWidth = 2560f;
        private const float WideViewportHeight = 960f;

        // ---------- identity case: no crop when aspect ratios match ----------

        [Test]
        public void MatchingAspectRatio_MapsSourceNormalizedDirectlyToViewportNormalized()
        {
            // Any viewport that shares the source image's own aspect ratio
            // has zero crop on both axes — the conversion must be a no-op.
            MapCoordinateMapping.SourceToViewport(
                0.27809f, 0.81250f, SourceWidth, SourceHeight,
                SourceWidth * 0.5f, SourceHeight * 0.5f,
                out float vx, out float vy);
            Assert.AreEqual(0.27809f, vx, Tolerance);
            Assert.AreEqual(0.81250f, vy, Tolerance);
        }

        [Test]
        public void SourceCenter_AlwaysMapsToViewportCenter_RegardlessOfAspectRatio()
        {
            // Cover-fit centers the scaled image within the viewport (no
            // background-position override anywhere in Map.uss), so the
            // exact source-image center must always land at the exact
            // viewport center, for any crop.
            MapCoordinateMapping.SourceToViewport(0.5f, 0.5f, SourceWidth, SourceHeight, NarrowViewportWidth, NarrowViewportHeight, out float nx, out float ny);
            Assert.AreEqual(0.5f, nx, Tolerance);
            Assert.AreEqual(0.5f, ny, Tolerance);

            MapCoordinateMapping.SourceToViewport(0.5f, 0.5f, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out float wx, out float wy);
            Assert.AreEqual(0.5f, wx, Tolerance);
            Assert.AreEqual(0.5f, wy, Tolerance);
        }

        // ---------- narrower-than-image viewport: horizontal crop ----------

        [Test]
        public void NarrowViewport_Okinawa_MapsCorrectly_XCropped_YUnchanged()
        {
            MapCoordinateMapping.SourceToViewport(0.27809f, 0.81250f, SourceWidth, SourceHeight, NarrowViewportWidth, NarrowViewportHeight, out float vx, out float vy);
            Assert.AreEqual(0.25858f, vx, Tolerance, "X must shift away from the raw source fraction once horizontal crop is accounted for.");
            Assert.AreEqual(0.81250f, vy, Tolerance, "Y is the uncropped axis here — must equal the raw source fraction exactly.");
        }

        [Test]
        public void NarrowViewport_Fukuoka_MapsCorrectly_XCropped_YUnchanged()
        {
            MapCoordinateMapping.SourceToViewport(0.37484f, 0.50112f, SourceWidth, SourceHeight, NarrowViewportWidth, NarrowViewportHeight, out float vx, out float vy);
            Assert.AreEqual(0.36384f, vx, Tolerance);
            Assert.AreEqual(0.50112f, vy, Tolerance);
        }

        [Test]
        public void NarrowViewport_Hiroshima_MapsCorrectly_XCropped_YUnchanged()
        {
            MapCoordinateMapping.SourceToViewport(0.42140f, 0.46615f, SourceWidth, SourceHeight, NarrowViewportWidth, NarrowViewportHeight, out float vx, out float vy);
            Assert.AreEqual(0.41449f, vx, Tolerance);
            Assert.AreEqual(0.46615f, vy, Tolerance);
        }

        // ---------- wider-than-image viewport: vertical crop (the reported bug) ----------

        [Test]
        public void WideViewport_Okinawa_MapsCorrectly_XUnchanged_YCropped()
        {
            MapCoordinateMapping.SourceToViewport(0.27809f, 0.81250f, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out float vx, out float vy);
            Assert.AreEqual(0.27809f, vx, Tolerance, "X is the uncropped axis here — must equal the raw source fraction exactly.");
            Assert.AreEqual(0.85354f, vy, Tolerance, "Y must shift LOWER (larger normalized Y) than the raw source fraction — the old bug rendered it too high on screen.");
            Assert.Greater(vy, 0.81250f, "The fix must move the marker DOWN relative to the old (buggy, unconverted) Y — matching the reported 'too high' symptom.");
        }

        [Test]
        public void WideViewport_Fukuoka_MapsCorrectly_XUnchanged_YCropped()
        {
            MapCoordinateMapping.SourceToViewport(0.37484f, 0.50112f, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out float vx, out float vy);
            Assert.AreEqual(0.37484f, vx, Tolerance);
            Assert.AreEqual(0.50127f, vy, Tolerance);
        }

        [Test]
        public void WideViewport_Hiroshima_MapsCorrectly_XUnchanged_YCropped()
        {
            MapCoordinateMapping.SourceToViewport(0.42140f, 0.46615f, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out float vx, out float vy);
            Assert.AreEqual(0.42140f, vx, Tolerance);
            Assert.AreEqual(0.46171f, vy, Tolerance);
        }

        // ---------- source data is never mutated by the conversion ----------

        [Test]
        public void ConversionDoesNotMutateTheStoredSourceNormalizedCoordinates()
        {
            var okinawa = MapMarkerLayout.Chapters[0];
            Assert.AreEqual(MapMarkerLayout.OkinawaChapterId, okinawa.Id);
            float beforeX = okinawa.NormalizedX;
            float beforeY = okinawa.NormalizedY;

            MapCoordinateMapping.SourceToViewport(okinawa.NormalizedX, okinawa.NormalizedY, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out _, out _);

            // MapMarkerLayout.Chapters is a readonly struct array — the
            // conversion is purely a display-time transform layered on top,
            // never a rewrite of centralized data.
            Assert.AreEqual(beforeX, MapMarkerLayout.Chapters[0].NormalizedX);
            Assert.AreEqual(beforeY, MapMarkerLayout.Chapters[0].NormalizedY);
        }

        // ---------- degenerate input falls back gracefully, never NaN ----------

        [TestCase(0f, 100f)]
        [TestCase(100f, 0f)]
        [TestCase(-100f, 100f)]
        [TestCase(float.NaN, 100f)]
        public void DegenerateViewportSize_FallsBackToIdentityMapping_NeverNaNOrDivideByZero(float viewportWidth, float viewportHeight)
        {
            MapCoordinateMapping.SourceToViewport(0.27809f, 0.81250f, SourceWidth, SourceHeight, viewportWidth, viewportHeight, out float vx, out float vy);
            Assert.AreEqual(0.27809f, vx, Tolerance);
            Assert.AreEqual(0.81250f, vy, Tolerance);
            Assert.IsFalse(float.IsNaN(vx));
            Assert.IsFalse(float.IsNaN(vy));
        }

        // ---------- ViewportToSource: the exact inverse, used to capture "what is the player looking at" for cross-map spatial continuity ----------

        [Test]
        public void ViewportToSource_IsTheExactInverseOfSourceToViewport_RoundTrip_NarrowViewport()
        {
            foreach (var (sx, sy) in new[] { (0.27809f, 0.81250f), (0.37484f, 0.50112f), (0.1f, 0.1f), (0.5f, 0.5f), (0.9f, 0.9f) })
            {
                MapCoordinateMapping.SourceToViewport(sx, sy, SourceWidth, SourceHeight, NarrowViewportWidth, NarrowViewportHeight, out float vx, out float vy);
                MapCoordinateMapping.ViewportToSource(vx, vy, SourceWidth, SourceHeight, NarrowViewportWidth, NarrowViewportHeight, out float rx, out float ry);
                Assert.AreEqual(sx, rx, Tolerance, $"source=({sx},{sy})");
                Assert.AreEqual(sy, ry, Tolerance, $"source=({sx},{sy})");
            }
        }

        [Test]
        public void ViewportToSource_IsTheExactInverseOfSourceToViewport_RoundTrip_WideViewport()
        {
            foreach (var (sx, sy) in new[] { (0.27809f, 0.81250f), (0.42140f, 0.46615f), (0.1f, 0.1f), (0.5f, 0.5f), (0.9f, 0.9f) })
            {
                MapCoordinateMapping.SourceToViewport(sx, sy, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out float vx, out float vy);
                MapCoordinateMapping.ViewportToSource(vx, vy, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out float rx, out float ry);
                Assert.AreEqual(sx, rx, Tolerance, $"source=({sx},{sy})");
                Assert.AreEqual(sy, ry, Tolerance, $"source=({sx},{sy})");
            }
        }

        [Test]
        public void ViewportToSource_ViewportCenter_AlwaysMapsToSourceCenter_RegardlessOfAspectRatio()
        {
            // Inverse of SourceCenter_AlwaysMapsToViewportCenter_RegardlessOfAspectRatio.
            MapCoordinateMapping.ViewportToSource(0.5f, 0.5f, SourceWidth, SourceHeight, NarrowViewportWidth, NarrowViewportHeight, out float nx, out float ny);
            Assert.AreEqual(0.5f, nx, Tolerance);
            Assert.AreEqual(0.5f, ny, Tolerance);

            MapCoordinateMapping.ViewportToSource(0.5f, 0.5f, SourceWidth, SourceHeight, WideViewportWidth, WideViewportHeight, out float wx, out float wy);
            Assert.AreEqual(0.5f, wx, Tolerance);
            Assert.AreEqual(0.5f, wy, Tolerance);
        }

        [Test]
        public void ViewportToSource_MatchingAspectRatio_IsIdentity()
        {
            MapCoordinateMapping.ViewportToSource(
                0.27809f, 0.81250f, SourceWidth, SourceHeight,
                SourceWidth * 0.5f, SourceHeight * 0.5f,
                out float sx, out float sy);
            Assert.AreEqual(0.27809f, sx, Tolerance);
            Assert.AreEqual(0.81250f, sy, Tolerance);
        }

        [TestCase(0f, 100f)]
        [TestCase(100f, 0f)]
        [TestCase(-100f, 100f)]
        [TestCase(float.NaN, 100f)]
        public void ViewportToSource_DegenerateViewportSize_FallsBackToIdentityMapping_NeverNaNOrDivideByZero(float viewportWidth, float viewportHeight)
        {
            MapCoordinateMapping.ViewportToSource(0.27809f, 0.81250f, SourceWidth, SourceHeight, viewportWidth, viewportHeight, out float sx, out float sy);
            Assert.AreEqual(0.27809f, sx, Tolerance);
            Assert.AreEqual(0.81250f, sy, Tolerance);
            Assert.IsFalse(float.IsNaN(sx));
            Assert.IsFalse(float.IsNaN(sy));
        }
    }
}
