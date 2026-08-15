using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for <see cref="MapPanZoomMath"/>: zoom stays within [0.6, 2.5],
    /// non-finite input falls back to the safe default, deltas accumulate
    /// correctly, and pan is clamped to a fixed fraction of the viewport size
    /// (never fully losing the map, at any zoom level).
    /// </summary>
    public class MapPanZoomMathTests
    {
        [Test]
        public void ClampZoom_WithinRange_IsUnchanged()
        {
            Assert.AreEqual(1.4f, MapPanZoomMath.ClampZoom(1.4f));
        }

        [TestCase(0.1f, MapPanZoomMath.MinZoom)]
        [TestCase(10f, MapPanZoomMath.MaxZoom)]
        [TestCase(float.NaN, MapPanZoomMath.DefaultZoom)]
        [TestCase(float.PositiveInfinity, MapPanZoomMath.DefaultZoom)]
        [TestCase(float.NegativeInfinity, MapPanZoomMath.DefaultZoom)]
        public void ClampZoom_OutOfRangeOrInvalid_ClampsOrFallsBackToDefault(float input, float expected)
        {
            Assert.AreEqual(expected, MapPanZoomMath.ClampZoom(input));
        }

        [Test]
        public void ApplyZoomDelta_PositiveDelta_ZoomsIn()
        {
            float result = MapPanZoomMath.ApplyZoomDelta(1f, 0.2f);
            Assert.AreEqual(1.2f, result, 0.0001f);
        }

        [Test]
        public void ApplyZoomDelta_NegativeDelta_ZoomsOut()
        {
            float result = MapPanZoomMath.ApplyZoomDelta(1f, -0.3f);
            Assert.AreEqual(0.7f, result, 0.0001f);
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

        [Test]
        public void ClampPan_WithinBounds_IsUnchanged()
        {
            Assert.AreEqual(50f, MapPanZoomMath.ClampPan(50f, 1000f));
        }

        [Test]
        public void ClampPan_ClampsToFractionOfViewport()
        {
            float viewport = 1000f;
            float max = viewport * MapPanZoomMath.MaxPanFractionOfViewport;

            Assert.AreEqual(max, MapPanZoomMath.ClampPan(9999f, viewport));
            Assert.AreEqual(-max, MapPanZoomMath.ClampPan(-9999f, viewport));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ClampPan_NonFinitePan_FallsBackToZero(float invalidPan)
        {
            Assert.AreEqual(0f, MapPanZoomMath.ClampPan(invalidPan, 1000f));
        }

        [TestCase(0f)]
        [TestCase(-100f)]
        [TestCase(float.NaN)]
        public void ClampPan_InvalidOrNonPositiveViewport_FallsBackToZero(float invalidViewport)
        {
            Assert.AreEqual(0f, MapPanZoomMath.ClampPan(50f, invalidViewport));
        }
    }
}
