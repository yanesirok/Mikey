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
        public void MarkerBreath_RespectsTheAmbientCompositionRule()
        {
            Assert.GreaterOrEqual(MapAmbientMath.MarkerBreathPeriodSeconds, 3f,
                "Правило композиции: ambient не короче трёх секунд.");
            Assert.LessOrEqual(MapAmbientMath.MarkerBreathAmplitude * MapAmbientMath.FocusBreathMultiplier, 0.04f + Tolerance,
                "Правило композиции: ambient не больше четырёх процентов, включая усиленную цель.");
        }

        [Test]
        public void MarkerShadowScale_AndOpacity_AtRest_ReturnRestValues()
        {
            Assert.AreEqual(1f, MapAmbientMath.MarkerShadowScale(1f), Tolerance);
            Assert.AreEqual(MapAmbientMath.MarkerShadowRestOpacity, MapAmbientMath.MarkerShadowOpacity(1f), Tolerance);
        }

        [Test]
        public void MarkerShadowScale_AndOpacity_ShrinkAndFadeAsBreathGrows()
        {
            float maxBreath = 1f + MapAmbientMath.MarkerBreathAmplitude * MapAmbientMath.FocusBreathMultiplier;
            float restScale = MapAmbientMath.MarkerShadowScale(1f);
            float restOpacity = MapAmbientMath.MarkerShadowOpacity(1f);
            float grownScale = MapAmbientMath.MarkerShadowScale(maxBreath);
            float grownOpacity = MapAmbientMath.MarkerShadowOpacity(maxBreath);

            Assert.Less(grownScale, restScale, "Тень должна поджиматься по мере роста маркера.");
            Assert.Less(grownOpacity, restOpacity, "Тень должна бледнеть по мере роста маркера.");
        }

        /// <summary>
        /// Заказ ре-ревью задачи 10: тень выбранного маркера обязана быть
        /// шире и бледнее тени невыбранного ПРИ ОДНОЙ И ТОЙ ЖЕ фазе дыхания
        /// — сравнение проводится на нескольких фазах (покой, пик дыхания
        /// усиленной цели, старт входа), чтобы добавка выбора не оказалась
        /// заметна только в одной случайной точке.
        /// </summary>
        [Test]
        public void MarkerShadowScale_AndOpacity_SelectedIsWiderAndPalerThanUnselected_AtSamePhase()
        {
            float[] breathPhases =
            {
                1f,
                1f + MapAmbientMath.MarkerBreathAmplitude * MapAmbientMath.FocusBreathMultiplier,
                MapAmbientMath.MarkerEntranceStartScale,
            };

            foreach (float breathScale in breathPhases)
            {
                float unselectedScale = MapAmbientMath.MarkerShadowScale(breathScale, selected: false);
                float selectedScale = MapAmbientMath.MarkerShadowScale(breathScale, selected: true);
                Assert.Greater(selectedScale, unselectedScale,
                    $"Тень выбранного маркера должна быть шире невыбранного при breathScale={breathScale}.");

                float unselectedOpacity = MapAmbientMath.MarkerShadowOpacity(breathScale, selected: false);
                float selectedOpacity = MapAmbientMath.MarkerShadowOpacity(breathScale, selected: true);
                Assert.Less(selectedOpacity, unselectedOpacity,
                    $"Тень выбранного маркера должна быть бледнее невыбранного при breathScale={breathScale}.");
            }
        }

        [Test]
        public void MarkerShadowOpacity_NeverLeavesZeroToOne()
        {
            foreach (bool selected in new[] { false, true })
            {
                for (float breathScale = -1f; breathScale <= 3f; breathScale += 0.1f)
                {
                    float opacity = MapAmbientMath.MarkerShadowOpacity(breathScale, selected);
                    Assert.GreaterOrEqual(opacity, 0f - Tolerance);
                    Assert.LessOrEqual(opacity, 1f + Tolerance);
                }
            }
        }

        [Test]
        public void MarkerShadowScale_AndOpacity_AreSafeOnNaN()
        {
            Assert.AreEqual(1f, MapAmbientMath.MarkerShadowScale(float.NaN), Tolerance);
            Assert.AreEqual(MapAmbientMath.MarkerShadowRestOpacity, MapAmbientMath.MarkerShadowOpacity(float.NaN), Tolerance);
            Assert.AreEqual(1f + MapAmbientMath.MarkerShadowSelectedScaleBonus,
                MapAmbientMath.MarkerShadowScale(float.NaN, selected: true), Tolerance);
            Assert.AreEqual(MapAmbientMath.MarkerShadowRestOpacity - MapAmbientMath.MarkerShadowSelectedOpacityDrop,
                MapAmbientMath.MarkerShadowOpacity(float.NaN, selected: true), Tolerance);
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
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(250f, 1f, 400f), Tolerance);
        }

        [Test]
        public void ParallaxOffset_MovesWithThePanForFactorsAboveOne()
        {
            float offset = MapAmbientMath.ParallaxOffset(100f, 1.1f, 400f);
            Assert.Greater(offset, 0f, "Ближнее облако должно уходить в ту же сторону, что и пан, но дальше.");
            Assert.AreEqual(10f, offset, Tolerance);
        }

        /// <remarks>
        /// Ограничен ВХОД, а не результат, и это не косметика. Потолок на
        /// результате выше насыщения выдаёт всем множителям одно и то же
        /// число — полосы схлопываются в одну, и глубина, ради которой
        /// множители и разведены, выключается. Проверяется именно тем, что
        /// два РАЗНЫХ множителя дают два РАЗНЫХ смещения при пане далеко за
        /// пределом.
        /// </remarks>
        [Test]
        public void ParallaxOffset_LimitsThePanAndKeepsDepthAboveIt()
        {
            const float Limit = 400f;

            Assert.AreEqual(Limit * 0.12f, MapAmbientMath.ParallaxOffset(100000f, 1.12f, Limit), Tolerance);
            Assert.AreEqual(-Limit * 0.12f, MapAmbientMath.ParallaxOffset(-100000f, 1.12f, Limit), Tolerance);

            float near = MapAmbientMath.ParallaxOffset(100000f, 1.12f, Limit);
            float far = MapAmbientMath.ParallaxOffset(100000f, 1.04f, Limit);
            Assert.Greater(near - far, 1f,
                "Выше предела полосы обязаны остаться РАЗНЫМИ — иначе потолок убивает глубину, а не бережёт композицию.");

            // До предела — строго линейно, без всякого поджатия.
            Assert.AreEqual(Limit * 0.5f * 0.12f,
                MapAmbientMath.ParallaxOffset(Limit * 0.5f, 1.12f, Limit), Tolerance);
        }

        [Test]
        public void ParallaxOffset_IsSafeOnDegenerateLimit()
        {
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(500f, 1.12f, 0f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(500f, 1.12f, -10f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(500f, 1.12f, float.NaN), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(float.NaN, 1.12f, 400f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(500f, float.NaN, 400f), Tolerance);
        }

        [Test]
        public void ParallaxFactors_AreDefinedForEveryCloudAndAboveOne()
        {
            Assert.AreEqual(MapAmbientMath.CloudCount, MapAmbientMath.CloudParallaxFactors.Length);
            foreach (float factor in MapAmbientMath.CloudParallaxFactors)
                Assert.Greater(factor, 1f);
        }

        [Test]
        public void ApproachWeight_MovesTowardTargetAndArrives()
        {
            float w = MapAmbientMath.ApproachWeight(0f, 1f, 0.2f, 0.4f);
            Assert.AreEqual(0.5f, w, Tolerance);

            w = MapAmbientMath.ApproachWeight(w, 1f, 0.2f, 0.4f);
            Assert.AreEqual(1f, w, Tolerance);
        }

        [Test]
        public void ApproachWeight_MovesTowardTargetGraduallyWhenDecreasing()
        {
            // Возрастание уже покрыто ApproachWeight_MovesTowardTargetAndArrives —
            // здесь та же проверка постепенного, непрямого шага, но на убывание;
            // без неё симметричная ветвь (from - step) была бы проверена только
            // мгновенным клампом в ноль оверсайз-дельтой, а не частичным шагом.
            float w = MapAmbientMath.ApproachWeight(1f, 0f, 0.2f, 0.4f);
            Assert.AreEqual(0.5f, w, Tolerance);

            w = MapAmbientMath.ApproachWeight(w, 0f, 0.2f, 0.4f);
            Assert.AreEqual(0f, w, Tolerance);
        }

        [Test]
        public void ApproachWeight_NeverLeavesZeroOne()
        {
            Assert.AreEqual(0f, MapAmbientMath.ApproachWeight(0f, 0f, 1f, 0.4f), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.ApproachWeight(1f, 1f, 1f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.ApproachWeight(0.1f, 0f, 10f, 0.4f), Tolerance);
        }

        [Test]
        public void ApproachWeight_IsSafeOnZeroFade()
        {
            Assert.AreEqual(1f, MapAmbientMath.ApproachWeight(0f, 1f, 0.016f, 0f), Tolerance);
        }

        [Test]
        public void KenBurns_StartsAtRestAndStaysInsideItsBudget()
        {
            MapAmbientMath.KenBurns(0f, 1000f, 500f, out float x0, out float y0, out float z0);
            Assert.AreEqual(0f, x0, Tolerance);
            Assert.AreEqual(0f, y0, Tolerance);
            Assert.AreEqual(0f, z0, Tolerance);

            for (int step = 0; step <= 200; step++)
            {
                MapAmbientMath.KenBurns(step * 0.5f, 1000f, 500f, out float x, out float y, out float z);
                Assert.LessOrEqual(System.Math.Abs(x), 1000f * MapAmbientMath.KenBurnsPanAmplitude + Tolerance);
                Assert.LessOrEqual(System.Math.Abs(y), 500f * MapAmbientMath.KenBurnsPanAmplitude + Tolerance);
                Assert.LessOrEqual(System.Math.Abs(z), MapAmbientMath.KenBurnsZoomAmplitude + Tolerance);
            }
        }

        [Test]
        public void PaperBreath_IsAtMostSixPromille()
        {
            Assert.AreEqual(0.006f, MapAmbientMath.PaperBreathAmplitude, Tolerance);
            for (int step = 0; step <= 100; step++)
            {
                float v = MapAmbientMath.Breath(step * 0.5f, MapAmbientMath.PaperBreathPeriodSeconds, MapAmbientMath.PaperBreathAmplitude);
                Assert.GreaterOrEqual(v, 1f - Tolerance);
                Assert.LessOrEqual(v, 1.006f + Tolerance);
            }
        }

        [Test]
        public void DecayVelocity_LosesTheSameFractionEverySecond()
        {
            float afterOne = MapAmbientMath.DecayVelocity(1000f, 1f);
            Assert.AreEqual(1000f * MapAmbientMath.InertiaRemainingPerSecond, afterOne, 0.01f);

            float afterTwo = MapAmbientMath.DecayVelocity(afterOne, 1f);
            Assert.AreEqual(1000f * MapAmbientMath.InertiaRemainingPerSecond * MapAmbientMath.InertiaRemainingPerSecond, afterTwo, 0.01f);
        }

        [Test]
        public void DecayVelocity_IsIndependentOfStepSize()
        {
            float oneStep = MapAmbientMath.DecayVelocity(1000f, 0.5f);
            float twoSteps = MapAmbientMath.DecayVelocity(MapAmbientMath.DecayVelocity(1000f, 0.25f), 0.25f);
            Assert.AreEqual(oneStep, twoSteps, 0.01f);
        }

        [Test]
        public void IsInertiaFinished_TripsBelowTheStopSpeed()
        {
            Assert.IsTrue(MapAmbientMath.IsInertiaFinished(0f, 0f));
            Assert.IsTrue(MapAmbientMath.IsInertiaFinished(10f, 10f));
            Assert.IsFalse(MapAmbientMath.IsInertiaFinished(500f, 0f));
        }

        [Test]
        public void BlendVelocity_FollowsTheLatestSampleButSmoothsSpikes()
        {
            float blended = MapAmbientMath.BlendVelocity(0f, 1000f);
            Assert.Greater(blended, 0f);
            Assert.Less(blended, 1000f, "Одиночный выброс не должен целиком становиться скоростью броска.");
        }

        // ---------- каскад появления маркеров (взамен снятого USS-перехода — ре-ревью задачи 9) ----------

        [Test]
        public void MarkerEntranceProgress_MarkerZero_StartsImmediately()
        {
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceProgress(0, 0f, reducedMotion: false), Tolerance);
            Assert.AreEqual(0.5f, MapAmbientMath.MarkerEntranceProgress(0, MapAmbientMath.MarkerEntranceDurationSeconds * 0.5f, reducedMotion: false), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceProgress(0, MapAmbientMath.MarkerEntranceDurationSeconds, reducedMotion: false), Tolerance);
        }

        [Test]
        public void MarkerEntranceProgress_LaterMarker_WaitsOutItsStepDelayBeforeMoving()
        {
            float delay = 2 * MapAmbientMath.MarkerEntranceStepSeconds;

            // До истечения задержки — на нуле, а не в отрицательной/сломанной зоне.
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceProgress(2, 0f, reducedMotion: false), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceProgress(2, delay * 0.5f, reducedMotion: false), Tolerance);

            // Ровно на границе задержки — вход только начинается.
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceProgress(2, delay, reducedMotion: false), Tolerance);

            // Завершается через полную длительность ПОСЛЕ задержки, не от t=0.
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceProgress(2, delay + MapAmbientMath.MarkerEntranceDurationSeconds, reducedMotion: false), Tolerance);
        }

        [Test]
        public void MarkerEntranceProgress_ClampsPastCompletion_NeverGoesBackDown()
        {
            float wayPastDone = MapAmbientMath.MarkerEntranceDurationSeconds * 100f;
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceProgress(5, wayPastDone, reducedMotion: false), Tolerance);
        }

        [Test]
        public void MarkerEntranceProgress_ReducedMotion_DropsTheStepDelay_AllMarkersMoveTogether()
        {
            // Тот же индекс 5, который под полным движением ещё стоял бы в
            // задержке — под reducedMotion уже наравне со всеми.
            float t = MapAmbientMath.MarkerEntranceReducedDurationSeconds * 0.5f;
            Assert.AreEqual(
                MapAmbientMath.MarkerEntranceProgress(0, t, reducedMotion: true),
                MapAmbientMath.MarkerEntranceProgress(5, t, reducedMotion: true),
                Tolerance);
        }

        [Test]
        public void MarkerEntranceProgress_ReducedMotion_UsesTheShorterDuration()
        {
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceProgress(0, MapAmbientMath.MarkerEntranceReducedDurationSeconds, reducedMotion: true), Tolerance);
            Assert.Less(MapAmbientMath.MarkerEntranceReducedDurationSeconds, MapAmbientMath.MarkerEntranceDurationSeconds,
                "Схлопнутый вход обязан быть короче полного — иначе «меньше движения» не читалось бы короче.");
        }

        [Test]
        public void MarkerEntranceProgress_IsSafeOnDegenerateInput()
        {
            // Испорченный ввод не должен вешать маркер невидимым навсегда —
            // вход считается завершённым.
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceProgress(0, float.NaN, reducedMotion: false), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceProgress(0, float.PositiveInfinity, reducedMotion: false), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceProgress(-1, 0f, reducedMotion: false), Tolerance,
                "Отрицательный индекс не должен давать отрицательную задержку (вход раньше t=0).");
        }

        [Test]
        public void MarkerEntranceScale_GoesFromStartScaleToOne()
        {
            Assert.AreEqual(MapAmbientMath.MarkerEntranceStartScale, MapAmbientMath.MarkerEntranceScale(0f, reducedMotion: false), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceScale(1f, reducedMotion: false), Tolerance);
        }

        [Test]
        public void MarkerEntranceScale_ReducedMotion_NeverLeavesOne()
        {
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceScale(0f, reducedMotion: true), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceScale(1f, reducedMotion: true), Tolerance);
        }

        [Test]
        public void MarkerEntranceOffsetY_GoesFromStartOffsetToZero()
        {
            Assert.AreEqual(MapAmbientMath.MarkerEntranceStartOffsetY, MapAmbientMath.MarkerEntranceOffsetY(0f, reducedMotion: false), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceOffsetY(1f, reducedMotion: false), Tolerance);
        }

        [Test]
        public void MarkerEntranceOffsetY_ReducedMotion_NeverMoves()
        {
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceOffsetY(0f, reducedMotion: true), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceOffsetY(1f, reducedMotion: true), Tolerance);
        }

        [Test]
        public void MarkerEntranceScale_AndOffsetY_AreSafeOnDegenerateInput()
        {
            Assert.AreEqual(1f, MapAmbientMath.MarkerEntranceScale(float.NaN, reducedMotion: false), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.MarkerEntranceOffsetY(float.NaN, reducedMotion: false), Tolerance);
        }

        // ---------- MarkerEntranceTransform: явная доводка до покоя (ре-ре-ревью задачи 9) ----------
        // Регрессия была: opacity писалась только пока progress < 1, и дискретный
        // 33 мс тик почти никогда не попадает в progress == 1 ровно — прозрачность
        // застревала чуть ниже единицы навсегда. Эти тесты гоняют саму доводку
        // напрямую, а не только чистую интерполяцию.

        [Test]
        public void MarkerEntranceTransform_AtExactlyOne_SettlesToRestExactly()
        {
            MapAmbientMath.MarkerEntranceTransform(1f, reducedMotion: false, out float opacity, out float offsetY, out float scaleMultiplier);
            Assert.AreEqual(1f, opacity, Tolerance);
            Assert.AreEqual(0f, offsetY, Tolerance);
            Assert.AreEqual(1f, scaleMultiplier, Tolerance);
        }

        [Test]
        public void MarkerEntranceTransform_OvershootingPastOne_StillSettlesExactly()
        {
            // Смоделированный "перепрыгнувший" тик: 33 мс интервал почти
            // никогда не делит длительность входа нацело, значит progress
            // обычно перескакивает 1, а не попадает в неё ровно.
            MapAmbientMath.MarkerEntranceTransform(1.3f, reducedMotion: false, out float opacity, out float offsetY, out float scaleMultiplier);
            Assert.AreEqual(1f, opacity, Tolerance);
            Assert.AreEqual(0f, offsetY, Tolerance);
            Assert.AreEqual(1f, scaleMultiplier, Tolerance);
        }

        [Test]
        public void MarkerEntranceTransform_BeforeCompletion_TracksProgressAndStartValues()
        {
            MapAmbientMath.MarkerEntranceTransform(0f, reducedMotion: false, out float opacity, out float offsetY, out float scaleMultiplier);
            Assert.AreEqual(0f, opacity, Tolerance);
            Assert.AreEqual(MapAmbientMath.MarkerEntranceStartOffsetY, offsetY, Tolerance);
            Assert.AreEqual(MapAmbientMath.MarkerEntranceStartScale, scaleMultiplier, Tolerance);

            MapAmbientMath.MarkerEntranceTransform(0.999f, reducedMotion: false, out float lateOpacity, out _, out _);
            Assert.Less(lateOpacity, 1f, "Прямо перед завершением прозрачность ещё не должна быть равна единице.");
        }

        [Test]
        public void MarkerEntranceTransform_ReducedMotion_NeverMovesOrScales_OnlyOpacityRamps()
        {
            MapAmbientMath.MarkerEntranceTransform(0.5f, reducedMotion: true, out float opacity, out float offsetY, out float scaleMultiplier);
            Assert.AreEqual(0.5f, opacity, Tolerance);
            Assert.AreEqual(0f, offsetY, Tolerance);
            Assert.AreEqual(1f, scaleMultiplier, Tolerance);
        }

        [Test]
        public void MarkerEntranceTransform_IsSafeOnDegenerateInput()
        {
            MapAmbientMath.MarkerEntranceTransform(float.NaN, reducedMotion: false, out float opacity, out float offsetY, out float scaleMultiplier);
            Assert.AreEqual(1f, opacity, Tolerance);
            Assert.AreEqual(0f, offsetY, Tolerance);
            Assert.AreEqual(1f, scaleMultiplier, Tolerance);
        }

        // ---------- MarkerScale: закрепляет само произведение (ре-ре-ре-ревью задачи 9) ----------

        [Test]
        public void MarkerScale_IsTheProductOfBreathAndEntranceMultiplier()
        {
            Assert.AreEqual(1.02f * 0.92f, MapAmbientMath.MarkerScale(1.02f, 0.92f), Tolerance);
        }

        [Test]
        public void MarkerScale_AtRestForBothInputs_IsExactlyOne()
        {
            // Ровно та точка, где раньше был разрыв: вход завершён
            // (множитель 1) и дыхание в состоянии покоя (1) — произведение
            // обязано быть точной единицей, не "почти".
            Assert.AreEqual(1f, MapAmbientMath.MarkerScale(1f, 1f), Tolerance);
        }

        [Test]
        public void MarkerScale_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(1f, MapAmbientMath.MarkerScale(float.NaN, 0.92f), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.MarkerScale(1.02f, float.NaN), Tolerance);
        }

        // ---------- задача 6: рамка оживает — амплитуды, составная траектория, набухание, крен ----------

        [Test]
        public void FrameDriftIsFinallyAboveThePerceptionFloor()
        {
            // Прежние 0.012/0.006 давали размах ниже порога различения — это и
            // была вся причина, по которой небо читалось мёртвым.
            Assert.GreaterOrEqual(MapAmbientMath.DriftAmplitudeX, 0.03f);

            // По вертикали пол НИЖЕ, чем хотелось бы, и это осознанный
            // проигрыш заметности геометрии: под нижним облаком Японии всего
            // 0.0308 высоты канваса запаса, и его делят дрейф, параллакс и
            // подъём от крена (FrameMotionNeverUncoversTheMapEdge). Выше
            // примерно 0.0134 инвариант края нарушается при любых остальных
            // амплитудах, так что «заметная вертикаль» здесь просто не
            // помещается. Заметность несёт горизонталь, у которой запас есть.
            Assert.GreaterOrEqual(MapAmbientMath.DriftAmplitudeY, 0.009f);
            Assert.Less(MapAmbientMath.DriftAmplitudeY, MapAmbientMath.DriftAmplitudeX,
                "Небо обязано читаться как боковой снос, а не как качели.");
        }

        [Test]
        public void Compound_StaysInsideTheUnitRange()
        {
            for (int i = 0; i <= 2000; i++)
            {
                float value = MapAmbientMath.Compound(i * 0.1f, 26f, 0.37f);
                Assert.GreaterOrEqual(value, -1f - Tolerance,
                    "Без нормировки сумма двух синусоид выносит рамку за её амплитуду.");
                Assert.LessOrEqual(value, 1f + Tolerance);
            }
        }

        [Test]
        public void Compound_DoesNotRepeatWithinTheBasePeriod()
        {
            // Одна синусоида вернулась бы в ту же точку ровно через период.
            // Составная — не должна, иначе небо снова читается зацикленной гифкой.
            float atStart = MapAmbientMath.Compound(0.5f, 26f, 0.37f);
            float aPeriodLater = MapAmbientMath.Compound(26.5f, 26f, 0.37f);
            // NUnit 3 не имеет перегрузки AreNotEqual с допуском — только AreEqual.
            Assert.Greater(System.Math.Abs(atStart - aPeriodLater), Tolerance);
        }

        [Test]
        public void DriftSettle_RisesFromRestToFull()
        {
            Assert.AreEqual(0f, MapAmbientMath.DriftSettle(0f), Tolerance,
                "Вход обязан начинаться из покоя: при ненулевых фазах смещение в нуле времени иначе НЕ ноль.");
            Assert.AreEqual(1f, MapAmbientMath.DriftSettle(MapAmbientMath.DriftSettleSeconds), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.DriftSettle(99f), Tolerance);

            // Одних концов и монотонности мало: разрывная ступень «0 в нуле,
            // 0.95 на втором кадре» проходит их все и даёт ровно тот скачок,
            // ради устранения которого множитель и вводился. Нужна пара —
            // сход с нуля И замедление. Любой ease-out её проходит, линейный
            // ввод и ступень — нет.
            Assert.Less(MapAmbientMath.DriftSettle(MapAmbientMath.DriftSettleSeconds * 0.05f), 0.25f,
                "Ввод обязан ОТХОДИТЬ от нуля, а не прыгать: разрывная ступень даёт тот самый скачок на втором кадре.");
            Assert.Greater(MapAmbientMath.DriftSettle(MapAmbientMath.DriftSettleSeconds * 0.5f), 0.7f,
                "И обязан ЗАМЕДЛЯТЬСЯ: у линейного ввода здесь ровно половина.");

            float previous = -1f;
            for (int i = 0; i <= 30; i++)
            {
                float value = MapAmbientMath.DriftSettle(i * MapAmbientMath.DriftSettleSeconds / 30f);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }
        }

        [Test]
        public void CloudDrift_StartsAtExactRestForEveryCloud()
        {
            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                MapAmbientMath.CloudDrift(i, 0f, 1000f, 500f,
                    out float dx, out float dy, out float dOpacity);

                Assert.AreEqual(0f, dx, Tolerance, $"Облако {i} прыгает по горизонтали на первом кадре.");
                Assert.AreEqual(0f, dy, Tolerance, $"Облако {i} прыгает по вертикали на первом кадре.");
                Assert.AreEqual(0f, dOpacity, Tolerance, $"Облако {i} прыгает прозрачностью на первом кадре.");
            }
        }

        [Test]
        public void CloudDrift_StaysInsideItsDeclaredAmplitude()
        {
            const float width = 1000f;
            const float height = 500f;

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                for (int step = 0; step <= 2000; step++)
                {
                    MapAmbientMath.CloudDrift(i, step * 0.1f, width, height,
                        out float dx, out float dy, out float dOpacity);

                    Assert.LessOrEqual(System.Math.Abs(dx), width * MapAmbientMath.DriftAmplitudeX + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dy), height * MapAmbientMath.DriftAmplitudeY + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dOpacity), MapAmbientMath.DriftOpacityAmplitude + Tolerance);
                }
            }
        }

        /// <remarks>
        /// Угол между условиями, который не встречает ни один тест брифа:
        /// «t = 0» закреплён CloudDrift_StartsAtExactRestForEveryCloud, потолок
        /// — CloudDrift_StaysInsideItsDeclaredAmplitude, а вот СЕРЕДИНА ввода и
        /// жизнь ПОСЛЕ него — нет. Без этого теста замена <c>settle</c> на
        /// единицу переживает всё, что есть у облака 0 (его фаза нулевая, и в
        /// нуле времени синусоида и так в нуле), а замена на постоянный ноль
        /// переживает вообще все проверки «не больше амплитуды».
        /// </remarks>
        [Test]
        public void CloudDrift_IsRampedWhileEnteringAndFullyReleasedAfterwards()
        {
            const float width = 1000f;
            const float height = 500f;
            const float midEntry = MapAmbientMath.DriftSettleSeconds * 0.5f;

            MapAmbientMath.CloudDrift(1, midEntry, width, height, out float dx, out _, out _);
            float unramped = width * MapAmbientMath.DriftAmplitudeX
                * MapAmbientMath.Compound(midEntry, MapAmbientMath.CloudDriftPeriodsSeconds[1],
                    MapAmbientMath.CloudDriftPhases[1]);

            Assert.Greater(System.Math.Abs(unramped), 10f * Tolerance,
                "Проба выбрана так, чтобы неослабленное смещение было заметным — иначе сравнение ниже проходит вхолостую.");
            Assert.Less(System.Math.Abs(dx), System.Math.Abs(unramped),
                "Посреди ввода рамка обязана идти ОСЛАБЛЕННО — иначе множителя ввода нет вовсе.");
            Assert.AreEqual(unramped * MapAmbientMath.DriftSettle(midEntry), dx, Tolerance,
                "Ослабление обязано быть ровно множителем ввода, а не любым другим числом меньше единицы.");

            // ...и после ввода ослабление обязано СНЯТЬСЯ: постоянный
            // множитель меньше единицы прошёл бы и предыдущую проверку, и все
            // потолки амплитуды, оставив небо навсегда вполсилы.
            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                float peak = 0f;
                for (int step = 0; step <= 2000; step++)
                {
                    MapAmbientMath.CloudDrift(i, step * 0.1f, width, height, out float x, out _, out _);
                    float magnitude = System.Math.Abs(x);
                    if (magnitude > peak)
                        peak = magnitude;
                }

                Assert.Greater(peak, 0.9f * width * MapAmbientMath.DriftAmplitudeX,
                    $"Облако {i} никогда не выходит на заявленную амплитуду — ввод так и не отпустил его.");
            }
        }

        [Test]
        public void FrameSwell_StartsAtRestAndStaysInsideItsAmplitude()
        {
            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                Assert.AreEqual(1f, MapAmbientMath.FrameSwell(i, 0f), Tolerance,
                    "Экран уже показан: любой масштаб кроме единицы на первом кадре — видимый скачок.");

                for (int step = 0; step <= 1000; step++)
                {
                    float value = MapAmbientMath.FrameSwell(i, step * 0.2f);
                    Assert.GreaterOrEqual(value, 1f - Tolerance);
                    Assert.LessOrEqual(value, 1f + MapAmbientMath.FrameSwellAmplitude + Tolerance);
                }
            }
        }

        [Test]
        public void FrameRoll_StartsAtRestAndStaysInsideItsAmplitude()
        {
            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                Assert.AreEqual(0f, MapAmbientMath.FrameRollDegrees(i, 0f), Tolerance);

                for (int step = 0; step <= 1000; step++)
                {
                    float value = MapAmbientMath.FrameRollDegrees(i, step * 0.2f);
                    Assert.LessOrEqual(System.Math.Abs(value),
                        MapAmbientMath.FrameRollAmplitudeDegrees + Tolerance);
                }
            }
        }

        /// <remarks>
        /// Второй угол между условиями: тесты выше гоняют цикл по всем четырём
        /// облакам, но проверяют у каждого только границы — они целиком
        /// проходят и на реализации, которая игнорирует индекс и гоняет всем
        /// четырём период нулевого облака. Тогда рамка набухает и кренится
        /// СТРОЕМ, а весь смысл разных периодов в том, что она этого не делает.
        /// </remarks>
        [Test]
        public void FrameSwellAndRoll_GiveEachCloudItsOwnPeriod()
        {
            const float sample = 7f;
            Assert.Greater(MapAmbientMath.CloudCount, 1, "Сравнивать не с чем — цикл ниже прошёл бы вхолостую.");

            float swellOfFirst = MapAmbientMath.FrameSwell(0, sample);
            float rollOfFirst = MapAmbientMath.FrameRollDegrees(0, sample);

            for (int i = 1; i < MapAmbientMath.CloudCount; i++)
            {
                Assert.Greater(System.Math.Abs(MapAmbientMath.FrameSwell(i, sample) - swellOfFirst), 0.002f,
                    $"Облако {i} набухает синхронно с нулевым — период не зависит от индекса.");
                Assert.Greater(System.Math.Abs(MapAmbientMath.FrameRollDegrees(i, sample) - rollOfFirst), 0.002f,
                    $"Облако {i} кренится синхронно с нулевым — период не зависит от индекса.");
            }
        }

        [Test]
        public void FrameHelpersAreSafeOnOutOfRangeIndex()
        {
            Assert.AreEqual(1f, MapAmbientMath.FrameSwell(-1, 5f), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.FrameSwell(MapAmbientMath.CloudCount, 5f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.FrameRollDegrees(-1, 5f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.FrameRollDegrees(MapAmbientMath.CloudCount, 5f), Tolerance);
        }

        /// <remarks>
        /// Все проверки «не выходит за амплитуду» — зеркала собственной
        /// константы, поэтому сверху рамку не защищает ничто: с
        /// DriftAmplitudeX = 0.5 облако уезжает на пол-экрана и открывает
        /// голый обрез карты, а весь набор остаётся зелёным. Потолки здесь
        /// обоснованы КОМПОЗИЦИЕЙ («рамка только оживает, но не меняется»), а
        /// не текущими значениями.
        /// </remarks>
        [Test]
        public void FrameAmplitudesStayInsideTheCompositionBudget()
        {
            Assert.Less(MapAmbientMath.DriftAmplitudeX, 0.06f,
                "Дальше этого облако рамки перестаёт маскировать обрез карты, ради чего оно и стоит на своём месте.");
            Assert.Less(MapAmbientMath.FrameSwellAmplitude, 0.08f,
                "Набухание — дыхание объёма, а не наезд камеры.");
            // Потолка крена здесь БОЛЬШЕ НЕТ намеренно. Стоявший тут
            // Assert.Less(FrameRollAmplitudeDegrees, 3f) был выведен из
            // ничего: он не связан ни с одним числом геометрии, и мутация до
            // 2.9° проходила зелёной, съедая полтора запаса нижнего облака
            // целиком. Крен теперь охраняет
            // FrameMotionNeverUncoversTheMapEdge — по фактическим прямоугольникам
            // MapCloudLayout, а не по круглому числу.

            // «Вдвое меньше горизонтали» из doc-комментария и таблицы спеки:
            // одного лишь Y < X мало — 0.034 против 0.035 его удовлетворяет и
            // превращает небо в диагональ под 45 градусов.
            Assert.Less(MapAmbientMath.DriftAmplitudeY, 0.6f * MapAmbientMath.DriftAmplitudeX,
                "Вертикаль обязана быть заметно меньше горизонтали, иначе облако ходит по прямой под 45 градусов.");
        }

        /// <remarks>
        /// Отношения периодов как ЗНАЧЕНИЯ не закреплены ничем: замена любого
        /// из них на 1f не красит ни один тест. Закрывается не пересказом
        /// чисел, а свойством расфазировки — ни одна пара движений ОДНОГО
        /// облака не идёт общим периодом, иначе два движения слипаются в одно.
        /// </remarks>
        [Test]
        public void FrameMotionsOfOneCloudNeverShareAPeriod()
        {
            int comparisons = 0;

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                float basePeriod = MapAmbientMath.CloudDriftPeriodsSeconds[i];
                float[] periods =
                {
                    basePeriod,
                    basePeriod * 1.618f,
                    basePeriod * 0.77f,
                    basePeriod * MapAmbientMath.FrameSwellPeriodRatio,
                    basePeriod * MapAmbientMath.FrameRollPeriodRatio,
                };

                for (int a = 0; a < periods.Length; a++)
                {
                    for (int b = a + 1; b < periods.Length; b++)
                    {
                        comparisons++;
                        Assert.Greater(System.Math.Abs(periods[a] - periods[b]) / basePeriod, 0.05f,
                            $"Облако {i}: движения {a} и {b} идут одним периодом и слипаются в одно.");
                    }
                }
            }

            Assert.AreEqual(MapAmbientMath.CloudCount * 10, comparisons,
                "Ни одна пара не осталась непроверенной — иначе циклы прошли бы вхолостую.");
        }

        /// <summary>
        /// Потолок движения рамки, выведенный из ФАКТИЧЕСКОЙ геометрии
        /// <see cref="MapCloudLayout"/>, а не из круглого числа.
        ///
        /// <para>
        /// Вся спека стоит на том, что четыре облака рамки маскируют обрез
        /// карты по периметру: «если они поплывут, откроется голый край».
        /// Значит, у каждого облака есть кромки, свисающие ЗА канвас, и запас
        /// под каждой из них конечен. Самый узкий — под нижним облаком
        /// Японии: 0.0308 высоты канваса. Крен вокруг центра поднимает один
        /// его угол на <c>полуширина · sin θ</c>, а полуширина этого облака —
        /// целая высота канваса, то есть рычаг в тридцать три раза длиннее
        /// запаса. Прежние 1.5° съедали 86% запаса ОДНИМ креном, и вместе с
        /// дрейфом и параллаксом ход вдвое превышал запас: игрок, дотянувший
        /// карту вверх до упора, видел клин голого низа.
        /// </para>
        ///
        /// <para>
        /// Считается худший случай: все три вклада в фазе, пан на упоре.
        /// Набухание в сумму НЕ входит намеренно — <c>Breath</c> лежит в
        /// <c>[1, 1 + A]</c> и только растит бокс от центра, то есть уводит
        /// кромку наружу, в запас.
        /// </para>
        ///
        /// <para>
        /// Проверяется только ВЕРТИКАЛЬ. Горизонтальные кромки этой рамки
        /// инвариантом не описываются вовсе: у левого облака Окинавы
        /// <c>x = 0.00000</c>, то есть на канвасе с пропорцией исходной
        /// картинки его левая кромка стоит ровно на кромке канваса, а на
        /// более широком — уже внутри неё. Горизонтальный запас там равен
        /// нулю по построению композиции, и никакая амплитуда его не
        /// соблюдёт. См. «известный потолок» в §8 спеки.
        /// </para>
        /// </summary>
        [Test]
        public void FrameMotionNeverUncoversTheMapEdge()
        {
            // Высота фиксирована, пропорции перебираются: вертикальные запасы
            // у cover-fit постоянны на всём диапазоне пропорций НЕ шире
            // исходной картинки (там кроп идёт по горизонтали, и по вертикали
            // 1:1), а на более широких только растут. Худший случай поэтому
            // покрыт первыми тремя, четвёртая — контроль того, что он
            // действительно не хуже.
            const float H = 1000f;
            float sourceAspect = MapCloudLayout.SourceImageWidth / MapCloudLayout.SourceImageHeight;
            float[] aspects = { 16f / 9f, 2.17f, sourceAspect, 2.5f };

            double radians = MapAmbientMath.FrameRollAmplitudeDegrees * System.Math.PI / 180.0;
            float sin = (float)System.Math.Abs(System.Math.Sin(radians));
            float versine = (float)(1.0 - System.Math.Cos(radians));

            var presets = new[] { MapCloudLayout.JapanRest, MapCloudLayout.OkinawaRest };
            int checkedEdges = 0;

            foreach (float aspect in aspects)
            {
                float W = H * aspect;

                foreach (MapCloudPreset preset in presets)
                {
                    // Порядок в точности как в MapAmbientController.ResolveScreenElements:
                    // индекс облака ЕСТЬ индекс его периода дрейфа и его множителя
                    // параллакса, и путать их нельзя — рычаги у облаков разные.
                    CloudLayout[] byIndex = { preset.Right1, preset.Left1, preset.Left2, preset.Bottom1 };
                    Assert.AreEqual(MapAmbientMath.CloudCount, byIndex.Length);

                    for (int i = 0; i < byIndex.Length; i++)
                    {
                        MapCoordinateMapping.SourceRectToViewport(
                            byIndex[i].NormalizedX, byIndex[i].NormalizedY,
                            byIndex[i].NormalizedWidth, byIndex[i].NormalizedHeight,
                            MapCloudLayout.SourceImageWidth, MapCloudLayout.SourceImageHeight,
                            W, H, out float vx, out float vy, out float vw, out float vh);

                        float boxWidth = vw * W;
                        float boxHeight = vh * H;

                        // Подъём считается от ПОЛУШИРИНЫ: широкое облако
                        // рычагом усиливает малый угол. Второе слагаемое —
                        // просадка от косинуса, на малых углах почти ноль, но
                        // выписана, чтобы формула была геометрией, а не
                        // приближением.
                        float rollRise = 0.5f * boxWidth * sin + 0.5f * boxHeight * versine;

                        // Пан на входе параллакса уже ограничен ClampPan своим
                        // MaxPanForZoom, но предел рамки ниже любого из них,
                        // поэтому худший случай — ровно предел.
                        float parallax = System.Math.Abs(MapAmbientMath.CloudParallaxFactors[i] - 1f)
                            * H * MapAmbientMath.FrameParallaxPanLimitFraction;

                        float travel = H * MapAmbientMath.DriftAmplitudeY + parallax + rollRise;

                        if (vy < 0f)
                        {
                            checkedEdges++;
                            AssertMaskingEdgeStaysOutside(-vy * H, travel, aspect, i, "Верхняя");
                        }

                        if (vy + vh > 1f)
                        {
                            checkedEdges++;
                            AssertMaskingEdgeStaysOutside((vy + vh - 1f) * H, travel, aspect, i, "Нижняя");
                        }
                    }
                }
            }

            // У каждого из четырёх облаков обоих пресетов ровно одна
            // вертикальная маскирующая кромка (right1/left1/left2 — верхняя,
            // bottom1 — нижняя). Без этой сверки перестановка координат,
            // втянувшая облако целиком внутрь канваса, дала бы зелёный тест,
            // ничего не проверивший.
            Assert.AreEqual(aspects.Length * presets.Length * MapAmbientMath.CloudCount, checkedEdges,
                "Число проверенных кромок не совпало с ожидаемым — часть облаков перестала маскировать край.");
        }

        private static void AssertMaskingEdgeStaysOutside(
            float marginPixels, float travelPixels, float aspect, int index, string edge)
        {
            Assert.Greater(marginPixels, travelPixels,
                $"Пропорция {aspect:F3}, облако {index}. {edge} кромка свисает за канвас на "
                + $"{marginPixels:F2} px (канвас высотой 1000), а суммарный вертикальный ход — дрейф "
                + $"{1000f * MapAmbientMath.DriftAmplitudeY:F2} + параллакс + подъём от крена — "
                + $"составляет {travelPixels:F2} px. Кромка заходит ВНУТРЬ канваса: игрок, дотянувший "
                + "карту до упора, видит клин голого края карты.");
        }
    }
}
