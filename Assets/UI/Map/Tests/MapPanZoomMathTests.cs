using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for <see cref="MapPanZoomMath"/>: zoom never drops low enough to
    /// expose background around the map art (a fixed floor of 1, provably
    /// aspect-ratio-independent given the canvas art's cover-fit rendering — see
    /// <see cref="MapPanZoomMath"/>'s class remarks), the default entry zoom
    /// starts subtly above that floor, and pan is clamped to exactly the excess
    /// the current zoom provides — zero excess (and so zero pan) right at the
    /// floor, growing linearly above it — for any viewport size or aspect ratio.
    /// </summary>
    public class MapPanZoomMathTests
    {
        // ---------- zoom ----------

        [Test]
        public void MinZoom_Is1_TheCoverFitFloor()
        {
            Assert.AreEqual(1f, MapPanZoomMath.MinZoom,
                "1 is the true minimum: the canvas art already cover-fits its box (scale-and-crop) at transform-scale 1, for any aspect ratio; going below 1 shrinks an already-viewport-covering box.");
        }

        [Test]
        public void DefaultZoom_IsAboveMinZoom_SoTheMapStartsExplorable()
        {
            Assert.Greater(MapPanZoomMath.DefaultZoom, MapPanZoomMath.MinZoom);
            Assert.AreEqual(MapPanZoomMath.MinZoom * MapPanZoomMath.InitialZoomMultiplier, MapPanZoomMath.DefaultZoom, 0.0001f);
        }

        // Both MapPanZoomController(screenId="map") and
        // MapPanZoomController(screenId="mapOkinawa") call ResetTransform() on
        // entry, which sets _zoom = MapPanZoomMath.DefaultZoom (see
        // MapPanZoomControllerSourceTests.EnteringItsConfiguredScreen_ResetsPanAndZoom
        // and MapPanZoomControllerSceneTests for the two configured scene
        // instances) — so this one shared constant is exactly the Japan and
        // Okinawa initial zoom.
        [Test]
        public void JapanInitialZoom_IsAboveMinimumCoverZoom()
        {
            Assert.Greater(MapPanZoomMath.DefaultZoom, MapPanZoomMath.MinZoom);
        }

        [Test]
        public void OkinawaInitialZoom_IsAboveMinimumCoverZoom()
        {
            Assert.Greater(MapPanZoomMath.DefaultZoom, MapPanZoomMath.MinZoom);
        }

        [Test]
        public void ClampZoom_WithinRange_IsUnchanged()
        {
            Assert.AreEqual(1.4f, MapPanZoomMath.ClampZoom(1.4f));
        }

        [TestCase(0.1f, MapPanZoomMath.MinZoom)]
        [TestCase(0.6f, MapPanZoomMath.MinZoom)]
        [TestCase(10f, MapPanZoomMath.MaxZoom)]
        [TestCase(float.NaN, MapPanZoomMath.DefaultZoom)]
        [TestCase(float.PositiveInfinity, MapPanZoomMath.DefaultZoom)]
        [TestCase(float.NegativeInfinity, MapPanZoomMath.DefaultZoom)]
        public void ClampZoom_OutOfRangeOrInvalid_ClampsAtCoverScaleOrFallsBackToDefault(float input, float expected)
        {
            Assert.AreEqual(expected, MapPanZoomMath.ClampZoom(input));
        }

        [Test]
        public void ApplyZoomDelta_PositiveDelta_ZoomsIn()
        {
            float result = MapPanZoomMath.ApplyZoomDelta(1.2f, 0.2f);
            Assert.AreEqual(1.4f, result, 0.0001f);
        }

        [Test]
        public void ApplyZoomDelta_NegativeDelta_ZoomsOut()
        {
            float result = MapPanZoomMath.ApplyZoomDelta(1.5f, -0.3f);
            Assert.AreEqual(1.2f, result, 0.0001f);
        }

        [Test]
        public void ApplyZoomDelta_ClampsAtBounds()
        {
            Assert.AreEqual(MapPanZoomMath.MaxZoom, MapPanZoomMath.ApplyZoomDelta(MapPanZoomMath.MaxZoom, 5f));
            Assert.AreEqual(MapPanZoomMath.MinZoom, MapPanZoomMath.ApplyZoomDelta(MapPanZoomMath.MinZoom, -5f));
        }

        [Test]
        public void ApplyZoomDelta_NonFiniteCurrentZoom_TreatsAsDefault()
        {
            float result = MapPanZoomMath.ApplyZoomDelta(float.NaN, 0.1f);
            Assert.AreEqual(MapPanZoomMath.DefaultZoom + 0.1f, result, 0.0001f);
        }

        // ---------- pan: must never reveal background, at any viewport aspect ratio ----------

        // 16:9-ish (1280x720) and a wide landscape Android phone (2400x1080) —
        // deliberately different aspect ratios to prove the invariant holds for
        // both, not just one screen shape.
        [TestCase(1280f, 720f)]
        [TestCase(2400f, 1080f)]
        [TestCase(1920f, 1080f)]
        public void AtMinZoom_MaxPanIsZero_ForAnyViewportAspectRatio(float viewportWidth, float viewportHeight)
        {
            Assert.AreEqual(0f, MapPanZoomMath.MaxPanForZoom(MapPanZoomMath.MinZoom, viewportWidth),
                $"At the cover-fit floor there is zero excess canvas, so panning by even one pixel on a {viewportWidth}x{viewportHeight} viewport would expose background.");
            Assert.AreEqual(0f, MapPanZoomMath.MaxPanForZoom(MapPanZoomMath.MinZoom, viewportHeight));
        }

        [TestCase(1280f, 720f)]
        [TestCase(2400f, 1080f)]
        public void AtMinZoom_ClampPan_ForcesAnyPanToZero_ForAnyViewportAspectRatio(float viewportWidth, float viewportHeight)
        {
            Assert.AreEqual(0f, MapPanZoomMath.ClampPan(9999f, MapPanZoomMath.MinZoom, viewportWidth));
            Assert.AreEqual(0f, MapPanZoomMath.ClampPan(-9999f, MapPanZoomMath.MinZoom, viewportHeight));
        }

        [Test]
        public void AboveMinZoom_MaxPanGrowsWithZoom_HalfTheExcessOnEachAxis()
        {
            // z = 1.5, viewport = 1280 -> excess = (1.5-1)*1280 = 640, half = 320.
            Assert.AreEqual(320f, MapPanZoomMath.MaxPanForZoom(1.5f, 1280f), 0.001f);
            // z = 2.0 (MaxZoom-adjacent), viewport = 1280 -> excess = 1280, half = 640.
            Assert.AreEqual(640f, MapPanZoomMath.MaxPanForZoom(2f, 1280f), 0.001f);
        }

        [Test]
        public void ClampPan_AtHigherZoom_ClampsToTheComputedRange_NotBeyondIt()
        {
            // z = 1.5, viewport = 1280 -> max = 320.
            Assert.AreEqual(320f, MapPanZoomMath.ClampPan(9999f, 1.5f, 1280f));
            Assert.AreEqual(-320f, MapPanZoomMath.ClampPan(-9999f, 1.5f, 1280f));
            Assert.AreEqual(150f, MapPanZoomMath.ClampPan(150f, 1.5f, 1280f), "A pan within range must pass through unchanged.");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ClampPan_NonFinitePan_FallsBackToZero(float invalidPan)
        {
            Assert.AreEqual(0f, MapPanZoomMath.ClampPan(invalidPan, 1.5f, 1000f));
        }

        [TestCase(0f)]
        [TestCase(-100f)]
        [TestCase(float.NaN)]
        public void MaxPanForZoom_InvalidOrNonPositiveViewport_IsZero(float invalidViewport)
        {
            Assert.AreEqual(0f, MapPanZoomMath.MaxPanForZoom(1.5f, invalidViewport));
        }

        [Test]
        public void MaxPanForZoom_NonFiniteZoom_IsZero()
        {
            Assert.AreEqual(0f, MapPanZoomMath.MaxPanForZoom(float.NaN, 1280f));
        }
    }
}
