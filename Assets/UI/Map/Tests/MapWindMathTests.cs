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
            Assert.AreEqual(0f, MapWindMath.Frac(float.PositiveInfinity), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Frac(float.NegativeInfinity), Tolerance);

            // Округление до ровно 1.0f случается на КРОШЕЧНОМ отрицательном
            // вводе, а не на большом: 1 - 1e-9 ближайшим float не представимо
            // (шаг у единицы 1.19e-7). Без защиты Frac вернула бы здесь 1.
            Assert.AreEqual(0f, MapWindMath.Frac(-1e-9f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Frac(-1e-8f), Tolerance);
        }

        [Test]
        public void Frac_NeverReturnsOne()
        {
            // Округление умеет вернуть ровно 1 при КРОШЕЧНОМ отрицательном
            // вводе, а единица в доле пути означала бы облако за правым краем в
            // момент, который код считает стартом полосы. Перебор -i*0.9999 сам
            // по себе этого не ловит: его результаты лежат в [0.0001, 0.02] и к
            // единице не подходят — опасные порядки заведены отдельно.
            for (int i = 1; i <= 200; i++)
            {
                float value = MapWindMath.Frac(-i * 0.9999f);
                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f);
            }

            foreach (float tiny in new[] { -1e-9f, -1e-8f, -1e-7f, -1e-6f, -1e-5f })
            {
                float value = MapWindMath.Frac(tiny);
                Assert.GreaterOrEqual(value, 0f, $"Frac({tiny}) ушла ниже нуля.");
                Assert.Less(value, 1f, $"Frac({tiny}) вернула единицу — облако за правым краем на старте полосы.");
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
            Assert.AreEqual(0f, MapWindMath.LaneProgress(float.PositiveInfinity, 100f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneProgress(10f, float.PositiveInfinity, 0.4f), Tolerance);
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
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(float.PositiveInfinity, 1000f, 340f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(0.5f, float.PositiveInfinity, 340f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(0.5f, 1000f, float.PositiveInfinity), Tolerance);
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

        /// <summary>
        /// Локальный Clamp01 пропускает NaN насквозь — оба сравнения с NaN
        /// ложны. Без раннего выхода EdgeFade(NaN) вернула бы NaN, тот ушёл бы
        /// через Opacity прямо в style.opacity и сломал бы отрисовку элемента
        /// до конца сессии. Тест держит именно этот ранний выход.
        /// </summary>
        [Test]
        public void EdgeFade_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.EdgeFade(float.NaN), Tolerance);
            Assert.AreEqual(0f, MapWindMath.EdgeFade(float.PositiveInfinity), Tolerance);
            Assert.AreEqual(0f, MapWindMath.EdgeFade(float.NegativeInfinity), Tolerance);

            // Выход за [0, 1] клампится, а не даёт мусор.
            Assert.AreEqual(0f, MapWindMath.EdgeFade(-1f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.EdgeFade(2f), Tolerance);
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
            Assert.AreEqual(1f, MapWindMath.Swell(7f, float.PositiveInfinity, 0.4f), Tolerance);
        }

        [Test]
        public void RollDegrees_StaysInsideItsAmplitude()
        {
            float widest = 0f;
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.RollDegrees(i * 0.5f, 140f, 0.55f);
                Assert.GreaterOrEqual(value, -MapWindMath.RollAmplitudeDegrees - Tolerance);
                Assert.LessOrEqual(value, MapWindMath.RollAmplitudeDegrees + Tolerance);
                widest = System.Math.Max(widest, System.Math.Abs(value));
            }

            // Границы по модулю односторонни: ноль укладывается в любую из них,
            // поэтому "return 0f" вместо тела прошёл бы весь набор. Крен обязан
            // доказать, что вообще движется.
            Assert.Greater(widest, MapWindMath.RollAmplitudeDegrees * 0.5f,
                "Крен не выходит за половину своей амплитуды — похоже, он вообще не считается.");
        }

        [Test]
        public void RollDegrees_IsPhaseAware()
        {
            float a = MapWindMath.RollDegrees(7f, 200f, 0.00f);
            float b = MapWindMath.RollDegrees(7f, 200f, 0.41f);
            float c = MapWindMath.RollDegrees(7f, 200f, 0.73f);

            Assert.Greater(System.Math.Abs(a - b), Tolerance);
            Assert.Greater(System.Math.Abs(b - c), Tolerance);
            Assert.Greater(System.Math.Abs(a - c), Tolerance);
        }

        [Test]
        public void RollDegrees_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.RollDegrees(7f, 0f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.RollDegrees(float.NaN, 140f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.RollDegrees(7f, float.PositiveInfinity, 0.4f), Tolerance);
        }

        [Test]
        public void Bob_StaysInsideItsAmplitude()
        {
            const float height = 800f;
            float widest = 0f;
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.Bob(i * 0.5f, 140f, 0.55f, height);
                Assert.LessOrEqual(System.Math.Abs(value), height * MapWindMath.BobAmplitude + Tolerance);
                widest = System.Math.Max(widest, System.Math.Abs(value));
            }

            // Та же дыра, что у крена: без этой строки "return 0f" вместо тела
            // проходит весь набор, а Bob_IsSafeOnDegenerateInput ещё и
            // подтверждает мутанта.
            Assert.Greater(widest, height * MapWindMath.BobAmplitude * 0.5f,
                "Покачивание не выходит за половину своей амплитуды — похоже, оно вообще не считается.");
        }

        /// <summary>
        /// Амплитуда задана ДОЛЕЙ высоты канваса, а не пикселями — по той же
        /// причине, что и все амплитуды MapAmbientMath. Без этой проверки
        /// потерянный множитель canvasHeight прошёл бы мимо: границы теста
        /// диапазона он бы не нарушил.
        /// </summary>
        [Test]
        public void Bob_ScalesWithCanvasHeight()
        {
            float single = MapWindMath.Bob(7f, 140f, 0.55f, 800f);
            float doubled = MapWindMath.Bob(7f, 140f, 0.55f, 1600f);

            Assert.Greater(System.Math.Abs(single), Tolerance,
                "Опорная точка обязана быть отлична от нуля, иначе удвоение ничего не доказывает.");
            Assert.AreEqual(single * 2f, doubled, Tolerance);
        }

        [Test]
        public void Bob_IsPhaseAware()
        {
            float a = MapWindMath.Bob(7f, 200f, 0.00f, 800f);
            float b = MapWindMath.Bob(7f, 200f, 0.41f, 800f);
            float c = MapWindMath.Bob(7f, 200f, 0.73f, 800f);

            Assert.Greater(System.Math.Abs(a - b), Tolerance);
            Assert.Greater(System.Math.Abs(b - c), Tolerance);
            Assert.Greater(System.Math.Abs(a - c), Tolerance);
        }

        [Test]
        public void Bob_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 140f, 0.4f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 140f, 0.4f, float.NaN), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 140f, 0.4f, float.PositiveInfinity), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 0f, 0.4f, 800f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Bob(float.NaN, 140f, 0.4f, 800f), Tolerance);
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
            float highest = float.NegativeInfinity;
            float lowest = float.PositiveInfinity;

            for (int i = 0; i <= 400; i++)
            {
                float t = i * 0.5f;
                float progress = MapWindMath.LaneProgress(t, 140f, 0.55f);
                float value = MapWindMath.Opacity(rest, progress, t, 140f, 0.55f, 1f);

                Assert.GreaterOrEqual(value, 0f, "Прозрачность никогда не отрицательна.");
                Assert.LessOrEqual(value, peak + Tolerance,
                    "Пик огибающей — потолок прозрачности на всём периоде.");

                // Края полосы гасятся в ноль штатно, поэтому и нижняя граница, и
                // оба сторожа двусторонности спрашиваются только там, где
                // краевое затухание уже равно единице.
                if (MapWindMath.EdgeFade(progress) >= 1f - Tolerance)
                {
                    sampledAtFullFade++;
                    highest = System.Math.Max(highest, value);
                    lowest = System.Math.Min(lowest, value);
                    Assert.GreaterOrEqual(value, trough - Tolerance,
                        "Вне краевого затухания провал огибающей — пол прозрачности.");
                }
            }

            Assert.Greater(sampledAtFullFade, 0,
                "Выборка обязана попасть в середину полосы, иначе нижняя граница не проверена вовсе.");
            Assert.Greater(highest, rest + Tolerance,
                "Пульсация двусторонняя: облако обязано уметь подсвечиваться. "
                + "Односторонняя форма вниз пролезла бы через обе границы выше незамеченной.");
            Assert.Less(lowest, rest - Tolerance,
                "Пульсация двусторонняя: облако обязано уметь и приглушаться. "
                + "Односторонняя форма вверх пролезла бы через обе границы выше незамеченной.");
        }

        /// <summary>
        /// Настоящий инвариант глубины: полосы не меняются местами. Проверяется
        /// по огибающим, а не по одному мгновению — пульсация двусторонняя, и
        /// совпадение фаз ничего не гарантирует.
        ///
        /// <para>
        /// Покои берутся из <see cref="MapWindLayout"/>: иначе таблица и её
        /// сторож молча разъехались бы при любой правке прозрачностей.
        /// </para>
        /// </summary>
        [Test]
        public void FarBandCanNeverOutshineTheMidBand()
        {
            float farPeak = MapWindLayout.FarRestOpacity * (1f + MapWindMath.OpacityAmplitude);
            float midTrough = MapWindLayout.MidRestOpacity * (1f - MapWindMath.OpacityAmplitude);

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
            Assert.AreEqual(0f, MapWindMath.Opacity(float.PositiveInfinity, 0.5f, 7f, 140f, 0.4f, 1f), Tolerance);

            // Испорченная доля пути гасит облако, а не красит его в NaN.
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, float.NaN, 7f, 140f, 0.4f, 1f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, float.PositiveInfinity, 7f, 140f, 0.4f, 1f), Tolerance);

            // То же для множителя ввода в кадр.
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.4f, float.NaN), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.4f, -1f), Tolerance);
            // Небезопасный settle прячет облако, а не показывает его: слой
            // декоративный, и пропавшее облако безобиднее залипшего. Маркеры
            // выбирают наоборот (MapAmbientMath.MarkerEntranceProgress -> 1),
            // потому что невидимый навсегда маркер — это потерянный экран.
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.4f, float.PositiveInfinity), Tolerance);
        }

        /// <summary>
        /// Вырожденный период — единственный вид испорченного ввода, на котором
        /// покой прозрачности НЕ ноль: пульсация глохнет, а покой полосы
        /// остаётся. Ранний возврат нулём сделал бы облако невидимым.
        /// </summary>
        [Test]
        public void Opacity_FallsBackToTheLaneRestOnDegeneratePeriod()
        {
            Assert.AreEqual(0.24f, MapWindMath.Opacity(0.24f, 0.5f, 7f, 0f, 0.4f, 1f), Tolerance);
            Assert.AreEqual(0.24f, MapWindMath.Opacity(0.24f, 0.5f, 7f, -5f, 0.4f, 1f), Tolerance);
            Assert.AreEqual(0.24f, MapWindMath.Opacity(0.24f, 0.5f, 7f, float.NaN, 0.4f, 1f), Tolerance);
            Assert.AreEqual(0.24f, MapWindMath.Opacity(0.24f, 0.5f, float.NaN, 140f, 0.4f, 1f), Tolerance);
        }
    }
}
