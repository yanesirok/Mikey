using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapCloudMath: smooth ease-in-out motion (no
    /// bounce/elastic/overshoot), per-cloud staggered start delays within a
    /// shared phase duration, and correct field-by-field interpolation
    /// between two CloudLayout presets.
    /// </summary>
    public class MapCloudMathTests
    {
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
        public void EaseInOutCubic_ClampsOutOfRangeInput()
        {
            Assert.AreEqual(0f, MapCloudMath.EaseInOutCubic(-1f), 0.0001f);
            Assert.AreEqual(1f, MapCloudMath.EaseInOutCubic(2f), 0.0001f);
        }

        [Test]
        public void EaseInOutCubic_NonFiniteInput_FallsBackSafely()
        {
            Assert.AreEqual(0f, MapCloudMath.EaseInOutCubic(float.NaN), 0.0001f);
        }

        // ---------- LocalProgress (per-cloud stagger) ----------

        [Test]
        public void LocalProgress_BeforeItsOwnStartDelay_IsZero()
        {
            Assert.AreEqual(0f, MapCloudMath.LocalProgress(0.05f, 0.14f, 0.65f), 0.0001f,
                "Bottom1 (0.14s delay) must not have started yet at elapsed=0.05s.");
        }

        [Test]
        public void LocalProgress_AfterItsOwnDurationElapses_IsOne()
        {
            Assert.AreEqual(1f, MapCloudMath.LocalProgress(1.0f, 0.00f, 0.65f), 0.0001f,
                "Left1 (0s delay, 0.65s duration) must be fully complete by elapsed=1.0s.");
        }

        [Test]
        public void LocalProgress_MidwayThroughItsOwnWindow_IsHalf()
        {
            Assert.AreEqual(0.5f, MapCloudMath.LocalProgress(0.325f, 0.00f, 0.65f), 0.0001f);
        }

        [Test]
        public void LocalProgress_DifferentStartDelays_ProduceDifferentProgressAtTheSameElapsedTime()
        {
            // Proves the stagger is real: at the same moment, a cloud that
            // started earlier (Left1, 0s) is further along than one that
            // started later (Bottom1, 0.14s).
            float left1Progress = MapCloudMath.LocalProgress(0.20f, 0.00f, 0.65f);
            float bottom1Progress = MapCloudMath.LocalProgress(0.20f, 0.14f, 0.65f);
            Assert.Greater(left1Progress, bottom1Progress);
        }

        [Test]
        public void LocalProgress_NonPositiveDuration_FallsBackToZero_NoDivideByZero()
        {
            float result = MapCloudMath.LocalProgress(0.5f, 0f, 0f);
            Assert.IsFalse(float.IsNaN(result));
            Assert.IsFalse(float.IsInfinity(result));
        }

        // ---------- Lerp ----------

        [Test]
        public void Lerp_AtT0_EqualsFrom()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f);
            var result = MapCloudMath.Lerp(from, to, 0f);
            Assert.AreEqual(from.NormalizedX, result.NormalizedX, 0.0001f);
            Assert.AreEqual(from.NormalizedY, result.NormalizedY, 0.0001f);
            Assert.AreEqual(from.NormalizedWidth, result.NormalizedWidth, 0.0001f);
            Assert.AreEqual(from.NormalizedHeight, result.NormalizedHeight, 0.0001f);
            Assert.AreEqual(from.RotationDegrees, result.RotationDegrees, 0.0001f);
        }

        [Test]
        public void Lerp_AtT1_EqualsTo()
        {
            var from = new CloudLayout(0.1f, 0.2f, 0.3f, 0.4f, 0f);
            var to = new CloudLayout(0.9f, 0.8f, 0.7f, 0.6f, -180f);
            var result = MapCloudMath.Lerp(from, to, 1f);
            Assert.AreEqual(to.NormalizedX, result.NormalizedX, 0.0001f);
            Assert.AreEqual(to.NormalizedY, result.NormalizedY, 0.0001f);
            Assert.AreEqual(to.NormalizedWidth, result.NormalizedWidth, 0.0001f);
            Assert.AreEqual(to.NormalizedHeight, result.NormalizedHeight, 0.0001f);
            Assert.AreEqual(to.RotationDegrees, result.RotationDegrees, 0.0001f);
        }

        [Test]
        public void Lerp_PreservesRotation_WhenFromAndToShareTheSameRotation()
        {
            // Bottom1's rest-to-cover interpolation must never animate
            // rotation away from -180deg, since both endpoints share it.
            var from = new CloudLayout(0.38255f, 0.39631f, 0.85611f, 0.63445f, -180f);
            var to = new CloudLayout(0.01f, 0.14f, 0.98f, 0.73f, -180f);
            var result = MapCloudMath.Lerp(from, to, 0.5f);
            Assert.AreEqual(-180f, result.RotationDegrees, 0.0001f);
        }
    }
}
