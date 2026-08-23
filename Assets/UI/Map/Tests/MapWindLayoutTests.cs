using NUnit.Framework;
using UnityEngine.UIElements;

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

            Assert.AreEqual(0.5f * 2f / MapWindLayout.SourceAspect,
                MapWindLayout.HeightFraction(0.5f, 2000f, 1000f), Tolerance,
                "Множитель пропорции канваса обязан участвовать: на квадратном канвасе он " +
                "тождественная единица и его пропажу не видно.");
        }

        [Test]
        public void HeightFractionIsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 1000f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 0f, 1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(float.NaN, 1000f, 1000f), Tolerance);

            // Отрицательный размер ловит проверка на неположительность, а вот
            // ПОЛОЖИТЕЛЬНАЯ бесконечность проходит её насквозь: ловит её только
            // IsFinite, и без этих трёх строк её снятие осталось бы незамеченным.
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, -1000f, 1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 1000f, -1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, float.PositiveInfinity, 1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 1000f, float.PositiveInfinity), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(float.PositiveInfinity, 1000f, 1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, float.NaN, 1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 1000f, float.NaN), Tolerance);
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

        /// <summary>
        /// Вся таблица построчно, литеральными ожиданиями из раздела 5 спеки.
        /// Остальные тесты этого файла смотрят на полосу через FindByCross,
        /// который отдаёт ПЕРВОЕ совпадение по периоду, — то есть строки 1, 2,
        /// 4 и 5 не проверял никто, а верх, фаза и текстура конкретной строки
        /// не проверялись вовсе. Ожидания намеренно литеральные, а не через
        /// константы MapWindLayout: сверка таблицы с собой ничего не стоит.
        /// </summary>
        [Test]
        public void TableMatchesTheSpecRowForRow()
        {
            WindCloud[] expected =
            {
                new WindCloud(0.34f, 0.04f, 0.00f, 200f, 0.14f, 0.40f, "map-wind--left-01"),
                new WindCloud(0.34f, 0.19f, 0.41f, 200f, 0.14f, 0.40f, "map-wind--right-01"),
                new WindCloud(0.34f, 0.11f, 0.73f, 200f, 0.14f, 0.40f, "map-wind--left-02"),
                new WindCloud(0.52f, 0.26f, 0.17f, 140f, 0.24f, 0.75f, "map-wind--right-01"),
                new WindCloud(0.52f, 0.44f, 0.55f, 140f, 0.24f, 0.75f, "map-wind--left-01"),
                new WindCloud(0.52f, 0.35f, 0.88f, 140f, 0.24f, 0.75f, "map-wind--bottom-01"),
                new WindCloud(0.78f, 0.30f, 0.31f, 95f, 0.18f, 1.30f, "map-wind--left-01"),
            };

            Assert.AreEqual(expected.Length, MapWindLayout.Clouds.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                WindCloud want = expected[i];
                WindCloud got = MapWindLayout.Clouds[i];

                Assert.AreEqual(want.WidthFraction, got.WidthFraction, Tolerance, $"Строка {i}: ширина.");
                Assert.AreEqual(want.TopFraction, got.TopFraction, Tolerance, $"Строка {i}: верх.");
                Assert.AreEqual(want.Phase, got.Phase, Tolerance, $"Строка {i}: фаза.");
                Assert.AreEqual(want.CrossSeconds, got.CrossSeconds, Tolerance, $"Строка {i}: период пересечения.");
                Assert.AreEqual(want.RestOpacity, got.RestOpacity, Tolerance, $"Строка {i}: прозрачность покоя.");
                Assert.AreEqual(want.ParallaxFactor, got.ParallaxFactor, Tolerance, $"Строка {i}: параллакс.");
                Assert.AreEqual(want.TextureClass, got.TextureClass, $"Строка {i}: класс текстуры.");
            }
        }

        /// <summary>
        /// Единственный в проекте писатель геометрии плывущих облаков. Без
        /// этого теста и правило нулевого left, и сами четыре свойства
        /// охранялись бы только чтением кода.
        /// </summary>
        [Test]
        public void ApplyWritesTheFourGeometryProperties()
        {
            var element = new VisualElement();

            // Ближнее облако на альбомном телефоне 2.17:1 — та же строка, по
            // которой считался бюджет площади в разделе 9 спеки.
            MapWindLayout.Apply(element, MapWindLayout.Clouds[6], 2170f, 1000f);

            AssertPercent(78f, element.style.width, "width");
            AssertPercent(67.034f, element.style.height, "height");
            AssertPercent(30f, element.style.top, "top");
            AssertPercent(0f, element.style.left, "left",
                "left обязан быть ровно нулём: весь путь по горизонтали несёт translate из тика, "
                + "и ненулевой left означал бы второго писателя горизонтали.");
        }

        [Test]
        public void ApplyIgnoresANullElement()
        {
            Assert.DoesNotThrow(() => MapWindLayout.Apply(null, MapWindLayout.Clouds[0], 2170f, 1000f));
        }

        private static void AssertPercent(float expectedPercent, StyleLength actual, string name, string why = null)
        {
            Assert.AreEqual(LengthUnit.Percent, actual.value.unit,
                $"{name} обязан быть в процентах канваса, а не в пикселях.");
            Assert.AreEqual(expectedPercent, actual.value.value, 0.01f, why ?? $"{name}.");
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
