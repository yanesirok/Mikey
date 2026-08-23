using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Таблица ветровых полос. Главные тесты здесь — упорядоченность по
    /// глубине и потолок площади: первое отвечает за то, что глубина вообще
    /// читается, второе за то, что слой не съест кадровый бюджет карты.
    /// </summary>
    public class MapWindLayoutTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void TableHasSevenClouds()
        {
            Assert.AreEqual(7, MapWindLayout.Clouds.Length);
        }

        [Test]
        public void EveryPhaseIsInsideTheLane()
        {
            foreach (WindCloud cloud in MapWindLayout.Clouds)
            {
                Assert.GreaterOrEqual(cloud.Phase, 0f);
                Assert.Less(cloud.Phase, 1f);
            }
        }

        [Test]
        public void EveryPhaseIsDistinct()
        {
            // Совпавшие фазы означали бы два облака, идущие вплотную друг за
            // другом весь сеанс — дыра в одном месте полосы и сгусток в другом.
            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                for (int j = i + 1; j < MapWindLayout.Clouds.Length; j++)
                {
                    // Assert.AreNotEqual не имеет перегрузки с допуском — только
                    // ограничение Within выражает «различны с точностью до Tolerance».
                    Assert.That(MapWindLayout.Clouds[i].Phase,
                        Is.Not.EqualTo(MapWindLayout.Clouds[j].Phase).Within(Tolerance),
                        $"Облака {i} и {j} стартуют из одной точки полосы.");
                }
            }
        }

        [Test]
        public void BandsAreOrderedByDepth()
        {
            // Дальнее обязано двигаться МЕНЬШЕ карты, ближнее БОЛЬШЕ. Именно
            // отсутствие этого разброса (1.04..1.12 у рамки) и делало небо
            // плоским.
            float far = FindByCross(MapWindLayout.FarCrossSeconds).ParallaxFactor;
            float mid = FindByCross(MapWindLayout.MidCrossSeconds).ParallaxFactor;
            float near = FindByCross(MapWindLayout.NearCrossSeconds).ParallaxFactor;

            Assert.Less(far, mid);
            Assert.Less(mid, near);
            Assert.Less(far, 1f, "Дальняя полоса обязана отставать от карты.");
            Assert.Greater(near, 1f, "Ближняя полоса обязана обгонять карту.");
        }

        [Test]
        public void FartherBandsAreSlowerAndSmaller()
        {
            WindCloud far = FindByCross(MapWindLayout.FarCrossSeconds);
            WindCloud mid = FindByCross(MapWindLayout.MidCrossSeconds);
            WindCloud near = FindByCross(MapWindLayout.NearCrossSeconds);

            Assert.Greater(MapWindLayout.FarCrossSeconds, MapWindLayout.MidCrossSeconds);
            Assert.Greater(MapWindLayout.MidCrossSeconds, MapWindLayout.NearCrossSeconds);
            Assert.Less(far.WidthFraction, mid.WidthFraction);
            Assert.Less(mid.WidthFraction, near.WidthFraction);
            Assert.Less(far.RestOpacity, mid.RestOpacity);
        }

        [Test]
        public void HeightFractionPreservesTheSourceAspect()
        {
            // Ширина 0.5 канваса на квадратном канвасе даёт высоту 0.5/2.525.
            float height = MapWindLayout.HeightFraction(0.5f, 1000f, 1000f);
            Assert.AreEqual(0.5f / MapWindLayout.SourceAspect, height, Tolerance);
        }

        [Test]
        public void HeightFractionIsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 1000f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 0f, 1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(float.NaN, 1000f, 1000f), Tolerance);
        }

        /// <summary>
        /// Потолок стоимости слоя. GPU растеризует и смешивает весь квад, а не
        /// только видимую тушь, поэтому цена считается по площади
        /// прямоугольников. Рамка стоит около 2.0 экранов; ветер обязан
        /// остаться дешевле неё.
        /// </summary>
        [Test]
        public void TotalQuadAreaStaysInsideTheBudget()
        {
            float total = 0f;
            foreach (WindCloud cloud in MapWindLayout.Clouds)
            {
                float height = MapWindLayout.HeightFraction(
                    cloud.WidthFraction, MapWindLayout.BudgetCanvasAspect, 1f);
                total += cloud.WidthFraction * height;
            }

            Assert.Greater(total, 0f, "Тест обязан был что-то посчитать.");
            Assert.LessOrEqual(total, MapWindLayout.AreaBudgetScreens,
                $"Суммарная площадь ветровых квадов {total:F3} экрана превышает потолок " +
                $"{MapWindLayout.AreaBudgetScreens}. Это прямая плата кадром на телефоне.");
        }

        [Test]
        public void EveryTextureClassIsOneOfTheFourKnownOnes()
        {
            string[] allowed =
            {
                "map-wind--left-01",
                "map-wind--left-02",
                "map-wind--right-01",
                "map-wind--bottom-01",
            };

            foreach (WindCloud cloud in MapWindLayout.Clouds)
                CollectionAssert.Contains(allowed, cloud.TextureClass);
        }

        private static WindCloud FindByCross(float crossSeconds)
        {
            foreach (WindCloud cloud in MapWindLayout.Clouds)
            {
                if (System.Math.Abs(cloud.CrossSeconds - crossSeconds) < Tolerance)
                    return cloud;
            }

            Assert.Fail($"В таблице нет облака с периодом пересечения {crossSeconds}.");
            return default;
        }
    }
}
