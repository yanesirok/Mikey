using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Математика ветровых полос. Класс чист и детерминирован, поэтому здесь
    /// настоящие тесты значений, а не проверки по тексту исходника.
    /// </summary>
    public class MapWindMathTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void Frac_HandlesNegativeAndWholeInput()
        {
            Assert.AreEqual(0.25f, MapWindMath.Frac(3.25f), Tolerance);
            Assert.AreEqual(0.75f, MapWindMath.Frac(-3.25f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Frac(4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Frac(float.NaN), Tolerance);
        }

        [Test]
        public void Frac_NeverReturnsOne()
        {
            // Округление при большом отрицательном вводе умеет вернуть ровно 1,
            // а единица в доле пути означала бы облако за правым краем в момент,
            // который код считает стартом полосы.
            for (int i = 1; i <= 200; i++)
            {
                float value = MapWindMath.Frac(-i * 0.9999f);
                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f);
            }
        }

        [Test]
        public void LaneProgress_WrapsOncePerCrossing()
        {
            Assert.AreEqual(0f, MapWindMath.LaneProgress(0f, 100f, 0f), Tolerance);
            Assert.AreEqual(0.5f, MapWindMath.LaneProgress(50f, 100f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneProgress(100f, 100f, 0f), Tolerance);
            Assert.AreEqual(0.25f, MapWindMath.LaneProgress(125f, 100f, 0f), Tolerance);
        }

        [Test]
        public void LaneProgress_AppliesPhase()
        {
            Assert.AreEqual(0.4f, MapWindMath.LaneProgress(0f, 100f, 0.4f), Tolerance);
            Assert.AreEqual(0.1f, MapWindMath.LaneProgress(70f, 100f, 0.4f), Tolerance);
        }

        [Test]
        public void LaneProgress_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.LaneProgress(10f, 0f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneProgress(10f, -5f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneProgress(float.NaN, 100f, 0.4f), Tolerance);
        }

        [Test]
        public void LaneOffsetX_ClearsBothEdgesAtTheWrapPoint()
        {
            const float canvas = 1000f;
            const float cloud = 340f;

            float atStart = MapWindMath.LaneOffsetX(0f, canvas, cloud);
            Assert.AreEqual(-cloud, atStart, Tolerance,
                "В нуле пути облако обязано быть целиком за левым краем.");
            Assert.LessOrEqual(atStart + cloud, 0f + Tolerance);

            float atEnd = MapWindMath.LaneOffsetX(1f, canvas, cloud);
            Assert.GreaterOrEqual(atEnd, canvas - Tolerance,
                "В конце пути облако обязано быть целиком за правым краем.");
        }

        [Test]
        public void LaneOffsetX_IsMonotonic()
        {
            float previous = float.NegativeInfinity;
            for (int i = 0; i <= 50; i++)
            {
                float value = MapWindMath.LaneOffsetX(i / 50f, 1000f, 340f);
                Assert.Greater(value, previous);
                previous = value;
            }
        }

        [Test]
        public void LaneOffsetX_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(0.5f, 0f, 340f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(0.5f, 1000f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(float.NaN, 1000f, 340f), Tolerance);
        }

        [Test]
        public void EdgeFade_IsZeroAtBothEndsAndOneInTheMiddle()
        {
            Assert.AreEqual(0f, MapWindMath.EdgeFade(0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.EdgeFade(1f), Tolerance);
            Assert.AreEqual(1f, MapWindMath.EdgeFade(0.5f), Tolerance);
            Assert.AreEqual(1f, MapWindMath.EdgeFade(MapWindMath.FadeEdge), Tolerance);
            Assert.AreEqual(1f, MapWindMath.EdgeFade(1f - MapWindMath.FadeEdge), Tolerance);
        }

        [Test]
        public void EdgeFade_RisesMonotonicallyOnBothSlopes()
        {
            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float value = MapWindMath.EdgeFade(i * MapWindMath.FadeEdge / 20f);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }

            // Второй склон обходится от правого конца внутрь, поэтому затухание
            // здесь так же растёт, как и на первом: "монотонно на каждом склоне"
            // из дизайна — это рост в сторону середины, а не падение по индексу.
            previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float value = MapWindMath.EdgeFade(1f - i * MapWindMath.FadeEdge / 20f);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }
        }

        [Test]
        public void Swell_StaysInsideItsDeclaredRange()
        {
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.Swell(i * 0.5f, 140f, 0.55f);
                Assert.GreaterOrEqual(value, 1f - Tolerance);
                Assert.LessOrEqual(value, 1f + MapWindMath.SwellAmplitude + Tolerance);
            }
        }

        /// <summary>
        /// Главный тест против возврата к MapAmbientMath.Breath: она не берёт
        /// фазу, и три облака дальней полосы, делящие один crossSeconds,
        /// набухали бы строем — ровно то, от чего уходит весь дизайн.
        /// </summary>
        [Test]
        public void Swell_IsPhaseAware()
        {
            float a = MapWindMath.Swell(7f, 200f, 0.00f);
            float b = MapWindMath.Swell(7f, 200f, 0.41f);
            float c = MapWindMath.Swell(7f, 200f, 0.73f);

            Assert.Greater(System.Math.Abs(a - b), Tolerance);
            Assert.Greater(System.Math.Abs(b - c), Tolerance);
            Assert.Greater(System.Math.Abs(a - c), Tolerance);
        }

        [Test]
        public void Swell_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(1f, MapWindMath.Swell(7f, 0f, 0.4f), Tolerance);
            Assert.AreEqual(1f, MapWindMath.Swell(float.NaN, 140f, 0.4f), Tolerance);
        }

        [Test]
        public void RollDegrees_StaysInsideItsAmplitude()
        {
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.RollDegrees(i * 0.5f, 140f, 0.55f);
                Assert.GreaterOrEqual(value, -MapWindMath.RollAmplitudeDegrees - Tolerance);
                Assert.LessOrEqual(value, MapWindMath.RollAmplitudeDegrees + Tolerance);
            }
        }

        [Test]
        public void Bob_StaysInsideItsAmplitude()
        {
            const float height = 800f;
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.Bob(i * 0.5f, 140f, 0.55f, height);
                Assert.LessOrEqual(System.Math.Abs(value), height * MapWindMath.BobAmplitude + Tolerance);
            }
        }

        [Test]
        public void Bob_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 140f, 0.4f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 140f, 0.4f, float.NaN), Tolerance);
        }

        /// <summary>
        /// Истинная граница прозрачности: пульсация двусторонняя, поэтому
        /// значение ходит по огибающей ВОКРУГ покоя полосы, а не под ним.
        /// Нижняя граница спрашивается только там, где краевое затухание равно
        /// единице — на концах полосы облако гасится в ноль штатно.
        /// </summary>
        [Test]
        public void Opacity_StaysInsideItsEnvelopeAroundTheLaneRest()
        {
            const float rest = 0.24f;
            float peak = rest * (1f + MapWindMath.OpacityAmplitude);
            float trough = rest * (1f - MapWindMath.OpacityAmplitude);
            int sampledAtFullFade = 0;
            float highest = 0f;

            for (int i = 0; i <= 400; i++)
            {
                float t = i * 0.5f;
                float progress = MapWindMath.LaneProgress(t, 140f, 0.55f);
                float value = MapWindMath.Opacity(rest, progress, t, 140f, 0.55f, 1f);

                Assert.GreaterOrEqual(value, 0f, "Прозрачность никогда не отрицательна.");
                if (value > highest)
                    highest = value;
                Assert.LessOrEqual(value, peak + Tolerance,
                    "Пик огибающей — потолок прозрачности на всём периоде.");

                if (MapWindMath.EdgeFade(progress) >= 1f - Tolerance)
                {
                    sampledAtFullFade++;
                    Assert.GreaterOrEqual(value, trough - Tolerance,
                        "Вне краевого затухания провал огибающей — пол прозрачности.");
                }
            }

            Assert.Greater(sampledAtFullFade, 0,
                "Выборка обязана попасть в середину полосы, иначе нижняя граница не проверена вовсе.");
            Assert.Greater(highest, rest + Tolerance,
                "Пульсация двусторонняя: облако обязано уметь и подсвечиваться. "
                + "Односторонняя форма вниз пролезла бы через обе границы выше незамеченной.");
        }

        /// <summary>
        /// Настоящий инвариант глубины: полосы не меняются местами. Проверяется
        /// по огибающим, а не по одному мгновению — пульсация двусторонняя, и
        /// совпадение фаз ничего не гарантирует.
        ///
        /// <para>
        /// Покои 0.14 и 0.24 стоят здесь константами намеренно: MapWindLayout
        /// появится только в следующей задаче, а инвариант обязан охраняться уже
        /// сейчас. Когда раскладка появится, значения возьмутся из неё.
        /// </para>
        /// </summary>
        [Test]
        public void FarBandCanNeverOutshineTheMidBand()
        {
            const float far = 0.14f;
            const float mid = 0.24f;
            float farPeak = far * (1f + MapWindMath.OpacityAmplitude);
            float midTrough = mid * (1f - MapWindMath.OpacityAmplitude);

            Assert.Less(farPeak, midTrough,
                $"Дальняя полоса в пике ({farPeak:F3}) обязана оставаться бледнее средней в провале ({midTrough:F3}).");
        }

        [Test]
        public void Opacity_IsZeroAtBothLaneEnds()
        {
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, 0f, 0f, 140f, 0f, 1f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, 1f, 0f, 140f, 0f, 1f), Tolerance);
        }

        [Test]
        public void Opacity_ScalesWithSettle()
        {
            float full = MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.55f, 1f);
            float half = MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.55f, 0.5f);
            float none = MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.55f, 0f);

            Assert.AreEqual(full * 0.5f, half, Tolerance);
            Assert.AreEqual(0f, none, Tolerance);
        }

        [Test]
        public void Opacity_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.Opacity(float.NaN, 0.5f, 7f, 140f, 0.4f, 1f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Opacity(-1f, 0.5f, 7f, 140f, 0.4f, 1f), Tolerance);
        }
    }
}
