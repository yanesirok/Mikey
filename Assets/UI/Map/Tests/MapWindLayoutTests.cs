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
        /// прямоугольников — но именно ВИДИМЫХ и именно при реальном зуме.
        ///
        /// <para>
        /// Прежний вид этого теста складывал доли КАНВАСА и сравнивал сумму с
        /// потолком, то есть мерил состояние, которого не бывает: слой —
        /// ребёнок <c>.pan-canvas</c>, канвас рисуется с зумом, а площадь на
        /// экране растёт как КВАДРАТ зума. Зум не опускается ниже 1.4 на
        /// входе и стоит на 2.0 в покое Окинавы, так что «1.52 экрана» было
        /// занижением вдвое с лишним.
        /// </para>
        ///
        /// <para>
        /// Меряется пик по всему циклу полос и по всему разрешённому диапазону
        /// пана на зуме покоя Окинавы — самом глубоком, на котором карта
        /// когда-либо останавливается.
        /// </para>
        /// </summary>
        [Test]
        public void VisibleQuadAreaStaysInsideTheBudget()
        {
            float peak = PeakVisibleScreens(MapCloudTransitionController.OkinawaSettleZoom);

            Assert.Greater(peak, 0f, "Тест обязан был что-то посчитать.");
            Assert.LessOrEqual(peak, MapWindLayout.AreaBudgetScreens,
                $"Пик видимой площади ветровых квадов — {peak:F3} экрана на зуме покоя "
                + $"{MapCloudTransitionController.OkinawaSettleZoom:F1} — превышает потолок "
                + $"{MapWindLayout.AreaBudgetScreens}. Это прямая плата кадром на телефоне.");
        }

        /// <summary>
        /// Сырая сумма долей канваса — то, что мерил прежний тест, — обязана
        /// остаться СИЛЬНО ниже реального пика. Тест против отката к «мерим
        /// при единичном зуме»: без него подмена меры обратно на сырую сумму
        /// прошла бы зелёной, ведь 1.52 меньше любого разумного потолка.
        /// </summary>
        [Test]
        public void RawFractionSumUnderstatesTheRealCost()
        {
            float raw = 0f;
            foreach (WindCloud cloud in MapWindLayout.Clouds)
            {
                float height = MapWindLayout.HeightFraction(
                    cloud.WidthFraction, MapWindLayout.BudgetCanvasAspect, 1f);
                raw += cloud.WidthFraction * height;
            }

            float peak = PeakVisibleScreens(MapCloudTransitionController.OkinawaSettleZoom);

            // 1.3, а не 1.5: измеренный запас (2.452 против 1.518, то есть
            // 1.62) сам по себе свойство таблицы полос, и порог впритык к нему
            // краснел бы от безобидной правки ширины. От единицы — а именно
            // единицу дал бы возврат к сырой сумме — 1.3 отстоит уверенно.
            Assert.Greater(peak, raw * 1.3f,
                $"Сырая сумма {raw:F3} и измеренный пик {peak:F3} слишком близки — похоже, "
                + "мера снова считает доли канваса, а не видимую площадь при реальном зуме.");
        }

        /// <summary>
        /// Пик видимой площади всех семи квадов в долях ЭКРАНА при данном
        /// зуме. Канвас масштабируется вокруг своего центра и сдвигается на
        /// пан, поэтому в координатах канваса видно окно шириной
        /// <c>W / zoom</c>; площадь пересечения переводится на экран
        /// множителем <c>zoom * zoom</c>.
        ///
        /// <para>
        /// В геометрию входит набухание: <c>Swell</c> растит квад от центра, и
        /// растеризуется именно увеличенный прямоугольник. Крен (±1.6°) в
        /// расчёт не входит — на таком угле прирост охватывающего
        /// прямоугольника ниже процента и тонет в шаге выборки.
        /// </para>
        /// </summary>
        private static float PeakVisibleScreens(float zoom)
        {
            const float H = 1000f;
            const int TimeSamples = 6000;

            // Общий период трёх полос: НОК(200, 140, 95). Меньший интервал
            // оставил бы часть взаимных положений полос неопробованной.
            const float Span = 26600f;

            float W = H * MapWindLayout.BudgetCanvasAspect;
            float maxPanX = MapPanZoomMath.MaxPanForZoom(zoom, W);
            float maxPanY = MapPanZoomMath.MaxPanForZoom(zoom, H);

            float peak = 0f;
            for (int s = 0; s < TimeSamples; s++)
            {
                float t = s * (Span / TimeSamples);
                for (int ix = -2; ix <= 2; ix++)
                {
                    for (int iy = -2; iy <= 2; iy++)
                    {
                        float value = VisibleScreens(t, zoom, maxPanX * ix * 0.5f, maxPanY * iy * 0.5f, W, H);
                        if (value > peak)
                            peak = value;
                    }
                }
            }

            return peak;
        }

        /// <summary>Видимая площадь всех квадов в долях экрана в один момент времени при данных зуме и пане.</summary>
        private static float VisibleScreens(float t, float zoom, float panX, float panY, float W, float H)
        {
            float loX = W * 0.5f - (W * 0.5f + panX) / zoom;
            float hiX = W * 0.5f + (W * 0.5f - panX) / zoom;
            float loY = H * 0.5f - (H * 0.5f + panY) / zoom;
            float hiY = H * 0.5f + (H * 0.5f - panY) / zoom;

            float panLimitX = MapWindMath.ParallaxPanLimit(W);
            float panLimitY = MapWindMath.ParallaxPanLimit(H);

            float total = 0f;
            foreach (WindCloud lane in MapWindLayout.Clouds)
            {
                float width = lane.WidthFraction * W;
                float height = MapWindLayout.HeightFraction(lane.WidthFraction, W, H) * H;
                float progress = MapWindMath.LaneProgress(t, lane.CrossSeconds, lane.Phase);

                float x = MapWindMath.LaneOffsetX(progress, W, width)
                    + MapAmbientMath.ParallaxOffset(panX, lane.ParallaxFactor, panLimitX);
                float y = lane.TopFraction * H
                    + MapWindMath.Bob(t, lane.CrossSeconds, lane.Phase, H)
                    + MapAmbientMath.ParallaxOffset(panY, lane.ParallaxFactor, panLimitY);

                float swell = MapWindMath.Swell(t, lane.CrossSeconds, lane.Phase);
                float halfWidth = width * swell * 0.5f;
                float halfHeight = height * swell * 0.5f;
                float centerX = x + width * 0.5f;
                float centerY = y + height * 0.5f;

                float overlapX = Min(centerX + halfWidth, hiX) - Max(centerX - halfWidth, loX);
                float overlapY = Min(centerY + halfHeight, hiY) - Max(centerY - halfHeight, loY);
                if (overlapX <= 0f || overlapY <= 0f)
                    continue;

                total += overlapX * overlapY;
            }

            return total * zoom * zoom / (W * H);
        }

        private static float Min(float a, float b) => a < b ? a : b;

        private static float Max(float a, float b) => a > b ? a : b;

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
                new WindCloud(0.78f, 0.30f, 0.31f, 95f, 0.18f, 1.30f, "map-wind--left-02"),
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
