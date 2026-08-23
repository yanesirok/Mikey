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
            MapAmbientMath.CloudDrift(-1, 7f, 1000f, 500f, out float dx, out float dy, out float dOpacity);
            Assert.AreEqual(0f, dx, Tolerance);
            Assert.AreEqual(0f, dy, Tolerance);
            Assert.AreEqual(0f, dOpacity, Tolerance);
        }
    }
}
