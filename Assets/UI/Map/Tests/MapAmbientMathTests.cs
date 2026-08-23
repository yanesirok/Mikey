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

        [Test]
        public void MarkerShadowOpacity_NeverLeavesZeroToOne()
        {
            for (float breathScale = -1f; breathScale <= 3f; breathScale += 0.1f)
            {
                float opacity = MapAmbientMath.MarkerShadowOpacity(breathScale);
                Assert.GreaterOrEqual(opacity, 0f - Tolerance);
                Assert.LessOrEqual(opacity, 1f + Tolerance);
            }
        }

        [Test]
        public void MarkerShadowScale_AndOpacity_AreSafeOnNaN()
        {
            Assert.AreEqual(1f, MapAmbientMath.MarkerShadowScale(float.NaN), Tolerance);
            Assert.AreEqual(MapAmbientMath.MarkerShadowRestOpacity, MapAmbientMath.MarkerShadowOpacity(float.NaN), Tolerance);
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
    }
}
