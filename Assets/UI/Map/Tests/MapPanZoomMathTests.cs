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

        [Test]
        public void DefaultZoom_IsApproximately1Point4()
        {
            Assert.AreEqual(1.4f, MapPanZoomMath.DefaultZoom, 0.0001f,
                "The intended default viewing zoom should read as noticeably close/immersive, not a bare cover-fit.");
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

        // ---------- opening zoom animation easing ----------

        [Test]
        public void EaseOutCubic_StartsAt0_EndsAt1()
        {
            Assert.AreEqual(0f, MapPanZoomMath.EaseOutCubic(0f), 0.0001f);
            Assert.AreEqual(1f, MapPanZoomMath.EaseOutCubic(1f), 0.0001f);
        }

        [Test]
        public void EaseOutCubic_IsMonotonicallyIncreasing_NoOvershootOrBounce()
        {
            float previous = MapPanZoomMath.EaseOutCubic(0f);
            for (float t = 0.05f; t <= 1f; t += 0.05f)
            {
                float current = MapPanZoomMath.EaseOutCubic(t);
                Assert.GreaterOrEqual(current, previous, $"Must never move backward at t={t}.");
                Assert.LessOrEqual(current, 1f, $"Must never overshoot past 1 at t={t}.");
                previous = current;
            }
        }

        [Test]
        public void EaseOutCubic_Decelerates_EarlyProgressExceedsLinear()
        {
            // "Ease-out" means fast at first, settling by the end — at the
            // midpoint, progress must be further along than a plain linear
            // ramp (0.5) would give.
            Assert.Greater(MapPanZoomMath.EaseOutCubic(0.5f), 0.5f);
        }

        [Test]
        public void EaseOutCubic_ClampsOutOfRangeInput()
        {
            Assert.AreEqual(0f, MapPanZoomMath.EaseOutCubic(-1f));
            Assert.AreEqual(1f, MapPanZoomMath.EaseOutCubic(2f));
            Assert.AreEqual(0f, MapPanZoomMath.EaseOutCubic(float.NaN));
        }

        // ---------- EaseInOutCubic: the chapter-transition's approach/settle camera easing (Map Pass 3D) ----------

        [Test]
        public void EaseInOutCubic_BoundariesAreExact()
        {
            Assert.AreEqual(0f, MapPanZoomMath.EaseInOutCubic(0f), 0.0001f);
            Assert.AreEqual(1f, MapPanZoomMath.EaseInOutCubic(1f), 0.0001f);
        }

        [Test]
        public void EaseInOutCubic_Midpoint_IsExactlyHalf_Symmetric()
        {
            Assert.AreEqual(0.5f, MapPanZoomMath.EaseInOutCubic(0.5f), 0.0001f);
        }

        [Test]
        public void EaseInOutCubic_NeverOvershoots_StaysWithin0And1()
        {
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                float eased = MapPanZoomMath.EaseInOutCubic(t);
                Assert.GreaterOrEqual(eased, 0f, $"t={t}");
                Assert.LessOrEqual(eased, 1f, $"t={t}");
            }
        }

        [Test]
        public void EaseInOutCubic_IsMonotonicallyIncreasing_NoBounce()
        {
            float previous = MapPanZoomMath.EaseInOutCubic(0f);
            for (float t = 0.05f; t <= 1f; t += 0.05f)
            {
                float current = MapPanZoomMath.EaseInOutCubic(t);
                Assert.GreaterOrEqual(current, previous, $"t={t}");
                previous = current;
            }
        }

        [Test]
        public void EaseInOutCubic_NonFiniteInput_FallsBackSafely()
        {
            Assert.AreEqual(0f, MapPanZoomMath.EaseInOutCubic(float.NaN), 0.0001f);
        }

        [Test]
        public void EaseInOutCubic_HasASlowStart_UnlikeEaseOutCubic()
        {
            // EaseInOutCubic starts slow (ease-in) then accelerates, unlike
            // EaseOutCubic which is fast from t=0 — at an early t, EaseInOutCubic
            // must be further BEHIND linear progress than EaseOutCubic.
            const float earlyT = 0.2f;
            Assert.Less(MapPanZoomMath.EaseInOutCubic(earlyT), earlyT, "Ease-in should lag behind linear progress early on.");
            Assert.Greater(MapPanZoomMath.EaseOutCubic(earlyT), earlyT, "Ease-out should lead linear progress early on.");
        }

        // ---------- PanForTarget / CanvasNormalizedAtViewportCenter: chapter-focus opening + cross-map spatial continuity ----------

        [Test]
        public void PanForTarget_CanvasCenterToViewportCenter_IsZero_AtAnyZoom()
        {
            // The canvas's own center point never needs a pan offset to
            // land at the viewport's center, regardless of zoom.
            Assert.AreEqual(0f, MapPanZoomMath.PanForTarget(0.5f, 0.5f, 1f, 1280f), 0.001f);
            Assert.AreEqual(0f, MapPanZoomMath.PanForTarget(0.5f, 0.5f, 1.4f, 1280f), 0.001f);
            Assert.AreEqual(0f, MapPanZoomMath.PanForTarget(0.5f, 0.5f, 2.5f, 1280f), 0.001f);
        }

        [Test]
        public void PanForTarget_OffCenterCanvasPoint_ToViewportCenter_GrowsWithZoom()
        {
            // A canvas point left of center (0.3) needs a positive
            // (rightward) pan to reach the viewport's center, and MORE so
            // at higher zoom (the canvas is magnified, so the same relative
            // offset covers more viewport pixels). This is the UNCLAMPED
            // ideal pan — SetPan (the only caller) still clamps it via
            // MapPanZoomMath.ClampPan, which is what actually enforces "zero
            // achievable pan at MinZoom" (MaxPanForZoom(1,_) == 0), a
            // separate, later step from this pure targeting formula.
            float panAt1 = MapPanZoomMath.PanForTarget(0.3f, 0.5f, 1f, 1000f);
            float panAt2 = MapPanZoomMath.PanForTarget(0.3f, 0.5f, 2f, 1000f);
            Assert.AreEqual(200f, panAt1, 0.001f, "1000*(0.5-0.3) = 200.");
            Assert.AreEqual(400f, panAt2, 0.001f, "1000*2*(0.5-0.3) = 400 — double the zoom, double the ideal pan.");
            Assert.Greater(panAt2, panAt1);
        }

        [Test]
        public void PanForTarget_NonCenterTarget_OffsetsByExactlyTheTargetDelta_AtMinZoom()
        {
            // At zoom==1 the canvas-normalized-to-target term still applies
            // even though the zoom*(0.5-canvasNormalized) term is zero for
            // canvasNormalized==0.5 — isolates the "comfortable offset"
            // term used by the chapter-focus opening (target != 0.5).
            float pan = MapPanZoomMath.PanForTarget(0.5f, 0.56f, 1f, 1000f);
            Assert.AreEqual(60f, pan, 0.001f, "(0.56-0.5)*1000 = 60.");
        }

        [Test]
        public void CanvasNormalizedAtViewportCenter_ZeroPan_IsCanvasCenter()
        {
            Assert.AreEqual(0.5f, MapPanZoomMath.CanvasNormalizedAtViewportCenter(0f, 1.4f, 1280f), 0.0001f);
        }

        [Test]
        public void CanvasNormalizedAtViewportCenter_IsTheExactInverseOfPanForTarget_RoundTrip()
        {
            // Capture (CanvasNormalizedAtViewportCenter) must exactly invert
            // apply (PanForTarget with target=0.5) — this is the round trip
            // MapCloudTransitionController relies on to reproduce a
            // captured view on the destination map.
            foreach (float canvasNormalized in new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
            {
                foreach (float zoom in new[] { 1f, 1.4f, 2.5f })
                {
                    float pan = MapPanZoomMath.PanForTarget(canvasNormalized, 0.5f, zoom, 1000f);
                    float recovered = MapPanZoomMath.CanvasNormalizedAtViewportCenter(pan, zoom, 1000f);
                    Assert.AreEqual(canvasNormalized, recovered, 0.001f, $"canvasNormalized={canvasNormalized}, zoom={zoom}");
                }
            }
        }

        [Test]
        public void PanForTarget_NonFiniteInput_FallsBackToZero()
        {
            Assert.AreEqual(0f, MapPanZoomMath.PanForTarget(float.NaN, 0.5f, 1.4f, 1000f));
            Assert.AreEqual(0f, MapPanZoomMath.PanForTarget(0.5f, 0.5f, float.PositiveInfinity, 1000f));
        }

        [Test]
        public void CanvasNormalizedAtViewportCenter_ZeroZoomOrViewport_FallsBackToCenter_NoDivideByZero()
        {
            float result = MapPanZoomMath.CanvasNormalizedAtViewportCenter(10f, 0f, 1000f);
            Assert.AreEqual(0.5f, result);
            Assert.IsFalse(float.IsNaN(result));
            Assert.IsFalse(float.IsInfinity(result));

            float result2 = MapPanZoomMath.CanvasNormalizedAtViewportCenter(10f, 1.4f, 0f);
            Assert.AreEqual(0.5f, result2);
        }
    }
}
