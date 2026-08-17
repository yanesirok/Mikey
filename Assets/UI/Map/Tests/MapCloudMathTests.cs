using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapCloudMath: smooth ease-in-out motion (no
    /// bounce/elastic/overshoot), anchor-preserving expansion (the dominant
    /// transition motion — never a slide toward the viewport center), and
    /// correct field-by-field interpolation (including opacity) between two
    /// CloudLayout endpoints.
    /// </summary>
    public class MapCloudMathTests
    {
        private const float Tolerance = 0.0005f;

        // ---------- EaseInOutCubic ----------

        [Test]
        public void EaseInOutCubic_BoundariesAreExact()
        {
            Assert.AreEqual(0f, MapCloudMath.EaseInOutCubic(0f), 0.0001f);
            Assert.AreEqual(1f, MapCloudMath.EaseInOutCubic(1f), 0.0001f);
        }

        [Test]
        public void EaseInOutCubic_Midpoint_IsExactlyHalf_Symmetric()
        {
            Assert.AreEqual(0.5f, MapCloudMath.EaseInOutCubic(0.5f), 0.0001f);
        }

        [Test]
        public void EaseInOutCubic_NeverOvershoots_StaysWithin0And1()
        {
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                float eased = MapCloudMath.EaseInOutCubic(t);
                Assert.GreaterOrEqual(eased, 0f, $"t={t}");
                Assert.LessOrEqual(eased, 1f, $"t={t}");
            }
        }

        [Test]
        public void EaseInOutCubic_IsMonotonicallyIncreasing_NoBounce()
        {
            float previous = MapCloudMath.EaseInOutCubic(0f);
            for (float t = 0.05f; t <= 1f; t += 0.05f)
            {
                float current = MapCloudMath.EaseInOutCubic(t);
                Assert.GreaterOrEqual(current, previous, $"t={t}");
                previous = current;
            }
        }

        [Test]
        public void EaseInOutCubic_NonFiniteInput_FallsBackSafely()
        {
            Assert.AreEqual(0f, MapCloudMath.EaseInOutCubic(float.NaN), 0.0001f);
        }

        // ---------- EaseInOutQuart ----------

        [Test]
        public void EaseInOutQuart_BoundariesAreExact()
        {
            Assert.AreEqual(0f, MapCloudMath.EaseInOutQuart(0f), 0.0001f);
            Assert.AreEqual(1f, MapCloudMath.EaseInOutQuart(1f), 0.0001f);
        }

        [Test]
        public void EaseInOutQuart_Midpoint_IsExactlyHalf_Symmetric()
        {
            Assert.AreEqual(0.5f, MapCloudMath.EaseInOutQuart(0.5f), 0.0001f);
        }

        [Test]
        public void EaseInOutQuart_NeverOvershoots_StaysWithin0And1()
        {
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                float eased = MapCloudMath.EaseInOutQuart(t);
                Assert.GreaterOrEqual(eased, 0f, $"t={t}");
                Assert.LessOrEqual(eased, 1f, $"t={t}");
            }
        }

        [Test]
        public void EaseInOutQuart_IsMonotonicallyIncreasing_NoBounce()
        {
            float previous = MapCloudMath.EaseInOutQuart(0f);
            for (float t = 0.05f; t <= 1f; t += 0.05f)
            {
                float current = MapCloudMath.EaseInOutQuart(t);
                Assert.GreaterOrEqual(current, previous, $"t={t}");
                previous = current;
            }
        }

        [Test]
        public void EaseInOutQuart_NonFiniteInput_FallsBackSafely()
        {
            Assert.AreEqual(0f, MapCloudMath.EaseInOutQuart(float.NaN), 0.0001f);
        }

        [Test]
        public void EaseInOutQuart_IsSteeperThanCubic_NearTheMiddle()
        {
            // The whole point of adding a quartic option: a more dramatic,
            // pronounced spread through the middle of the close phase than
            // the gentler cubic reveal — at t=0.25 (still in the
            // slow-start half for both), quart must be further behind (a
            // slower start) than cubic.
            Assert.Less(MapCloudMath.EaseInOutQuart(0.25f), MapCloudMath.EaseInOutCubic(0.25f));
            // ... but by t=0.75 (already in the fast-catch-up half for
            // both), quart must have overtaken cubic's progress.
            Assert.Greater(MapCloudMath.EaseInOutQuart(0.75f), MapCloudMath.EaseInOutCubic(0.75f));
        }

        // ---------- LocalProgress ----------

        [Test]
        public void LocalProgress_AtElapsedZero_IsZero()
        {
            Assert.AreEqual(0f, MapCloudMath.LocalProgress(0f, 0f, 0.75f), 0.0001f);
        }

        [Test]
        public void LocalProgress_AfterFullDuration_IsOne()
        {
            Assert.AreEqual(1f, MapCloudMath.LocalProgress(0.75f, 0f, 0.75f), 0.0001f);
        }

        [Test]
        public void LocalProgress_Midway_IsHalf()
        {
            Assert.AreEqual(0.5f, MapCloudMath.LocalProgress(0.375f, 0f, 0.75f), 0.0001f);
        }

        [Test]
        public void LocalProgress_NonPositiveDuration_FallsBackToZero_NoDivideByZero()
        {
            float result = MapCloudMath.LocalProgress(0.5f, 0f, 0f);
            Assert.IsFalse(float.IsNaN(result));
            Assert.IsFalse(float.IsInfinity(result));
        }

        // ---------- ComputeClosedLayout: LeftEdge anchor (Left1/Left2) ----------

        [Test]
        public void ComputeClosedLayout_LeftEdge_KeepsXFixed_GrowsWidthOnly()
        {
            var rest = new CloudLayout(-0.02781f, -0.22051f, 0.62014f, 0.72051f, 0f, 0.74f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.LeftEdge, MapCloudLayout.CloseExpansionFactor, 1f);

            Assert.AreEqual(rest.NormalizedX, closed.NormalizedX, Tolerance, "Left edge must stay fixed.");
            Assert.AreEqual(rest.NormalizedY, closed.NormalizedY, Tolerance, "Y must not change during a LeftEdge expansion.");
            Assert.AreEqual(rest.NormalizedHeight, closed.NormalizedHeight, Tolerance, "Height must not stretch during a LeftEdge expansion.");
            Assert.AreEqual(rest.NormalizedWidth * MapCloudLayout.CloseExpansionFactor, closed.NormalizedWidth, Tolerance);
        }

        [Test]
        public void ComputeClosedLayout_LeftEdge_GrowsRightward()
        {
            var rest = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0.74f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.LeftEdge, 1.5f, 1f);
            float restRightEdge = rest.NormalizedX + rest.NormalizedWidth;
            float closedRightEdge = closed.NormalizedX + closed.NormalizedWidth;
            Assert.Greater(closedRightEdge, restRightEdge, "The right edge must move further right as the cloud expands.");
        }

        // ---------- ComputeClosedLayout: RightEdge anchor (Right1) ----------

        [Test]
        public void ComputeClosedLayout_RightEdge_KeepsRightEdgeFixed_GrowsWidthOnly()
        {
            var rest = new CloudLayout(0.54560f, -0.11901f, 0.82595f, 1.03076f, 0f, 1.00f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.RightEdge, MapCloudLayout.CloseExpansionFactor, 1f);

            float restRightEdge = rest.NormalizedX + rest.NormalizedWidth;
            float closedRightEdge = closed.NormalizedX + closed.NormalizedWidth;
            Assert.AreEqual(restRightEdge, closedRightEdge, Tolerance, "Right edge must stay fixed.");
            Assert.AreEqual(rest.NormalizedY, closed.NormalizedY, Tolerance, "Y must not change during a RightEdge expansion.");
            Assert.AreEqual(rest.NormalizedHeight, closed.NormalizedHeight, Tolerance, "Height must not stretch during a RightEdge expansion.");
            Assert.AreEqual(rest.NormalizedWidth * MapCloudLayout.CloseExpansionFactor, closed.NormalizedWidth, Tolerance);
        }

        [Test]
        public void ComputeClosedLayout_RightEdge_MatchesTheDerivedJapanConcept()
        {
            // 1689.9 @ x1116.3 on the 2046-wide Japan reference canvas ->
            // approximately 2806.1 @ x0 (see MapCloudLayout.CloseExpansionFactor).
            var rest = new CloudLayout(1116.3f / 2046f, 0f, 1689.9f / 2046f, 1f, 0f, 1f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.RightEdge, MapCloudLayout.CloseExpansionFactor, 1f);
            Assert.AreEqual(0f, closed.NormalizedX, 0.001f, "X should land at ~0, matching the user's concept.");
            Assert.AreEqual(2806.1f / 2046f, closed.NormalizedWidth, 0.002f);
        }

        [Test]
        public void ComputeClosedLayout_RightEdge_GrowsLeftward()
        {
            var rest = new CloudLayout(0.6f, 0.2f, 0.3f, 0.4f, 0f, 1.00f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.RightEdge, 1.5f, 1f);
            Assert.Less(closed.NormalizedX, rest.NormalizedX, "The left edge must move further left as the cloud expands.");
        }

        // ---------- ComputeClosedLayout: BottomRightCorner anchor (Bottom1) ----------

        [Test]
        public void ComputeClosedLayout_BottomRightCorner_KeepsCornerFixed_GrowsBothDimensions()
        {
            var rest = new CloudLayout(0.38255f, 0.39631f, 0.85611f, 0.63445f, -180f, 0.66f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.BottomRightCorner, MapCloudLayout.CloseExpansionFactor, 1f);

            float restRightEdge = rest.NormalizedX + rest.NormalizedWidth;
            float restBottomEdge = rest.NormalizedY + rest.NormalizedHeight;
            float closedRightEdge = closed.NormalizedX + closed.NormalizedWidth;
            float closedBottomEdge = closed.NormalizedY + closed.NormalizedHeight;

            Assert.AreEqual(restRightEdge, closedRightEdge, Tolerance, "Right edge (part of the bottom-right corner) must stay fixed.");
            Assert.AreEqual(restBottomEdge, closedBottomEdge, Tolerance, "Bottom edge (part of the bottom-right corner) must stay fixed.");
            Assert.AreEqual(rest.NormalizedWidth * MapCloudLayout.CloseExpansionFactor, closed.NormalizedWidth, Tolerance);
            Assert.AreEqual(rest.NormalizedHeight * MapCloudLayout.CloseExpansionFactor, closed.NormalizedHeight, Tolerance);
        }

        [Test]
        public void ComputeClosedLayout_BottomRightCorner_GrowsTowardUpperLeft()
        {
            var rest = new CloudLayout(0.4f, 0.4f, 0.3f, 0.3f, -180f, 0.66f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.BottomRightCorner, 1.5f, 1f);
            Assert.Less(closed.NormalizedX, rest.NormalizedX, "Left edge must move further left (toward upper-left).");
            Assert.Less(closed.NormalizedY, rest.NormalizedY, "Top edge must move further up (toward upper-left).");
        }

        [Test]
        public void ComputeClosedLayout_BottomRightCorner_PreservesRotation_NeverAnimated()
        {
            var rest = new CloudLayout(0.4f, 0.4f, 0.3f, 0.3f, -180f, 0.66f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.BottomRightCorner, MapCloudLayout.CloseExpansionFactor, 1f);
            Assert.AreEqual(-180f, closed.RotationDegrees, 0.0001f);
        }

        // ---------- ComputeClosedLayout: shared behavior across anchors ----------

        [Test]
        public void ComputeClosedLayout_AlwaysSetsTheRequestedClosedOpacity()
        {
            var rest = new CloudLayout(0.1f, 0.1f, 0.3f, 0.3f, 0f, 0.66f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.LeftEdge, MapCloudLayout.CloseExpansionFactor, 1.0f);
            Assert.AreEqual(1.0f, closed.Opacity, 0.0001f, "Full cover always reads fully opaque, regardless of the cloud's own rest opacity.");
        }

        [Test]
        public void ComputeClosedLayout_NonPositiveExpansionFactor_FallsBackToNoGrowth()
        {
            var rest = new CloudLayout(0.1f, 0.1f, 0.3f, 0.3f, 0f, 0.74f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.LeftEdge, 0f, 1f);
            Assert.AreEqual(rest.NormalizedWidth, closed.NormalizedWidth, Tolerance);
        }

        // ---------- Lerp (includes opacity) ----------

        [Test]
        public void Lerp_AtT0_EqualsFrom_IncludingOpacity()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0.74f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f, 1.0f);
            var result = MapCloudMath.Lerp(from, to, 0f);
            Assert.AreEqual(from.NormalizedX, result.NormalizedX, 0.0001f);
            Assert.AreEqual(from.NormalizedY, result.NormalizedY, 0.0001f);
            Assert.AreEqual(from.NormalizedWidth, result.NormalizedWidth, 0.0001f);
            Assert.AreEqual(from.NormalizedHeight, result.NormalizedHeight, 0.0001f);
            Assert.AreEqual(from.RotationDegrees, result.RotationDegrees, 0.0001f);
            Assert.AreEqual(from.Opacity, result.Opacity, 0.0001f);
        }

        [Test]
        public void Lerp_AtT1_EqualsTo_IncludingOpacity()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0.74f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f, 1.0f);
            var result = MapCloudMath.Lerp(from, to, 1f);
            Assert.AreEqual(to.NormalizedX, result.NormalizedX, 0.0001f);
            Assert.AreEqual(to.NormalizedY, result.NormalizedY, 0.0001f);
            Assert.AreEqual(to.NormalizedWidth, result.NormalizedWidth, 0.0001f);
            Assert.AreEqual(to.NormalizedHeight, result.NormalizedHeight, 0.0001f);
            Assert.AreEqual(to.RotationDegrees, result.RotationDegrees, 0.0001f);
            Assert.AreEqual(to.Opacity, result.Opacity, 0.0001f);
        }

        [Test]
        public void Lerp_OpacityRisesTowardFullyOpaque_PartwayThroughClose()
        {
            var rest = new CloudLayout(0.1f, 0.1f, 0.3f, 0.3f, 0f, 0.66f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.LeftEdge, MapCloudLayout.CloseExpansionFactor, 1.0f);
            var midway = MapCloudMath.Lerp(rest, closed, 0.5f);
            Assert.Greater(midway.Opacity, rest.Opacity);
            Assert.Less(midway.Opacity, 1.0f);
        }

        [Test]
        public void Lerp_PreservesRotation_WhenFromAndToShareTheSameRotation()
        {
            var from = new CloudLayout(0.38255f, 0.39631f, 0.85611f, 0.63445f, -180f, 0.66f);
            var to = new CloudLayout(0.01f, 0.14f, 0.98f, 0.73f, -180f, 1.0f);
            var result = MapCloudMath.Lerp(from, to, 0.5f);
            Assert.AreEqual(-180f, result.RotationDegrees, 0.0001f);
        }

        // ---------- LerpWithEasing: injectable curve (close uses quart, reveal uses cubic) ----------

        [Test]
        public void LerpWithEasing_UsingCubic_MatchesPlainLerp()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0.74f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f, 1.0f);
            for (float t = 0f; t <= 1f; t += 0.25f)
            {
                var viaLerp = MapCloudMath.Lerp(from, to, t);
                var viaEasing = MapCloudMath.LerpWithEasing(from, to, t, MapCloudMath.EaseInOutCubic);
                Assert.AreEqual(viaLerp.NormalizedX, viaEasing.NormalizedX, 0.0001f, $"t={t}");
                Assert.AreEqual(viaLerp.Opacity, viaEasing.Opacity, 0.0001f, $"t={t}");
            }
        }

        [Test]
        public void LerpWithEasing_AtT0_EqualsFrom_RegardlessOfEasing()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0.74f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f, 1.0f);
            var result = MapCloudMath.LerpWithEasing(from, to, 0f, MapCloudMath.EaseInOutQuart);
            Assert.AreEqual(from.NormalizedX, result.NormalizedX, 0.0001f);
            Assert.AreEqual(from.Opacity, result.Opacity, 0.0001f);
        }

        [Test]
        public void LerpWithEasing_AtT1_EqualsTo_RegardlessOfEasing()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0.74f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f, 1.0f);
            var result = MapCloudMath.LerpWithEasing(from, to, 1f, MapCloudMath.EaseInOutQuart);
            Assert.AreEqual(to.NormalizedX, result.NormalizedX, 0.0001f);
            Assert.AreEqual(to.Opacity, result.Opacity, 0.0001f);
        }

        [Test]
        public void LerpWithEasing_QuartVsCubic_ProduceDifferentMidwayResults()
        {
            // The whole reason MapCloudTransitionController uses a
            // different easing per phase — a quart-eased close must not be
            // indistinguishable from the old cubic-eased motion partway
            // through.
            var rest = new CloudLayout(0.1f, 0.1f, 0.3f, 0.3f, 0f, 0.66f);
            var closed = MapCloudMath.ComputeClosedLayout(rest, CloudExpansionAnchor.LeftEdge, MapCloudLayout.CloseExpansionFactor, 1.0f);
            var viaCubic = MapCloudMath.LerpWithEasing(rest, closed, 0.25f, MapCloudMath.EaseInOutCubic);
            var viaQuart = MapCloudMath.LerpWithEasing(rest, closed, 0.25f, MapCloudMath.EaseInOutQuart);
            Assert.AreNotEqual(viaCubic.NormalizedWidth, viaQuart.NormalizedWidth);
        }

        [Test]
        public void LerpWithEasing_NullEasing_FallsBackToCubic()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f, 0.74f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f, 1.0f);
            var viaNull = MapCloudMath.LerpWithEasing(from, to, 0.3f, null);
            var viaCubic = MapCloudMath.LerpWithEasing(from, to, 0.3f, MapCloudMath.EaseInOutCubic);
            Assert.AreEqual(viaCubic.NormalizedX, viaNull.NormalizedX, 0.0001f);
        }
    }
}
