using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    public class MapAmbientMathTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void Wave_StartsAtZeroAndPeaksAtQuarterPeriod()
        {
            Assert.AreEqual(0f, MapAmbientMath.Wave(0f, 20f, 0f), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.Wave(5f, 20f, 0f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.Wave(10f, 20f, 0f), Tolerance);
            Assert.AreEqual(-1f, MapAmbientMath.Wave(15f, 20f, 0f), Tolerance);
        }

        [Test]
        public void Wave_RepeatsEveryPeriod()
        {
            Assert.AreEqual(MapAmbientMath.Wave(3f, 20f, 0f), MapAmbientMath.Wave(23f, 20f, 0f), Tolerance);
        }

        [Test]
        public void Wave_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapAmbientMath.Wave(1f, 0f, 0f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.Wave(1f, -5f, 0f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.Wave(float.NaN, 20f, 0f), Tolerance);
        }

        [Test]
        public void Breath_StaysInsideOneToOnePlusAmplitude()
        {
            for (int i = 0; i <= 40; i++)
            {
                float value = MapAmbientMath.Breath(i * 0.25f, 3.2f, 0.035f);
                Assert.GreaterOrEqual(value, 1f - Tolerance);
                Assert.LessOrEqual(value, 1.035f + Tolerance);
            }
        }

        [Test]
        public void Breath_StartsAtRest()
        {
            Assert.AreEqual(1f, MapAmbientMath.Breath(0f, 3.2f, 0.035f), Tolerance);
        }

        [Test]
        public void CloudDrift_StaysInsideItsAmplitudeBudget()
        {
            for (int index = 0; index < MapAmbientMath.CloudCount; index++)
            {
                for (int step = 0; step <= 100; step++)
                {
                    MapAmbientMath.CloudDrift(index, step * 0.5f, 1000f, 500f,
                        out float dx, out float dy, out float dOpacity);

                    Assert.LessOrEqual(System.Math.Abs(dx), 1000f * MapAmbientMath.DriftAmplitudeX + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dy), 500f * MapAmbientMath.DriftAmplitudeY + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dOpacity), MapAmbientMath.DriftOpacityAmplitude + Tolerance);
                }
            }
        }

        [Test]
        public void CloudDrift_GivesEachCloudItsOwnMotion()
        {
            MapAmbientMath.CloudDrift(0, 7f, 1000f, 500f, out float x0, out _, out _);
            MapAmbientMath.CloudDrift(1, 7f, 1000f, 500f, out float x1, out _, out _);
            Assert.AreNotEqual(x0, x1, "Облака не должны ходить синхронно — иначе это читается как единый слайд.");
        }

        [Test]
        public void CloudDrift_IsSafeOnOutOfRangeIndex()
        {
            MapAmbientMath.CloudDrift(-1, 7f, 1000f, 500f, out float dxLow, out float dyLow, out float dOpacityLow);
            Assert.AreEqual(0f, dxLow, Tolerance);
            Assert.AreEqual(0f, dyLow, Tolerance);
            Assert.AreEqual(0f, dOpacityLow, Tolerance);

            // Код защищает обе стороны диапазона — верхняя граница проверяется
            // так же, как и нижняя.
            MapAmbientMath.CloudDrift(MapAmbientMath.CloudCount, 7f, 1000f, 500f, out float dxHigh, out float dyHigh, out float dOpacityHigh);
            Assert.AreEqual(0f, dxHigh, Tolerance);
            Assert.AreEqual(0f, dyHigh, Tolerance);
            Assert.AreEqual(0f, dOpacityHigh, Tolerance);
        }

        [Test]
        public void CloudDrift_IsSafeOnDegenerateCanvasSize()
        {
            AssertCloudDriftIsZero(0f, 500f);
            AssertCloudDriftIsZero(1000f, 0f);
            AssertCloudDriftIsZero(-1000f, 500f);
            AssertCloudDriftIsZero(1000f, -500f);
            AssertCloudDriftIsZero(float.NaN, 500f);
            AssertCloudDriftIsZero(1000f, float.NaN);
        }

        private static void AssertCloudDriftIsZero(float canvasWidth, float canvasHeight)
        {
            MapAmbientMath.CloudDrift(0, 7f, canvasWidth, canvasHeight, out float dx, out float dy, out float dOpacity);
            Assert.AreEqual(0f, dx, Tolerance);
            Assert.AreEqual(0f, dy, Tolerance);
            Assert.AreEqual(0f, dOpacity, Tolerance);
        }

        [Test]
        public void ParallaxOffset_IsZeroWhenFactorIsOne()
        {
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(250f, 1f), Tolerance);
        }

        [Test]
        public void ParallaxOffset_MovesWithThePanForFactorsAboveOne()
        {
            float offset = MapAmbientMath.ParallaxOffset(100f, 1.1f);
            Assert.Greater(offset, 0f, "Ближнее облако должно уходить в ту же сторону, что и пан, но дальше.");
            Assert.AreEqual(10f, offset, Tolerance);
        }

        [Test]
        public void ParallaxOffset_IsCapped()
        {
            float offset = MapAmbientMath.ParallaxOffset(100000f, 1.12f);
            Assert.AreEqual(MapAmbientMath.MaxParallaxOffsetPixels, offset, Tolerance);
            Assert.AreEqual(-MapAmbientMath.MaxParallaxOffsetPixels, MapAmbientMath.ParallaxOffset(-100000f, 1.12f), Tolerance);
        }

        [Test]
        public void ParallaxFactors_AreDefinedForEveryCloudAndAboveOne()
        {
            Assert.AreEqual(MapAmbientMath.CloudCount, MapAmbientMath.CloudParallaxFactors.Length);
            foreach (float factor in MapAmbientMath.CloudParallaxFactors)
                Assert.Greater(factor, 1f);
        }
    }
}
