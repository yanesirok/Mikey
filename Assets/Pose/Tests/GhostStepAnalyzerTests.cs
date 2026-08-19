using System;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class GhostStepAnalyzerTests
    {
        // Кадры раз в 0.1 c — характерный темп потока с камеры.
        private const double Dt = 0.1;

        /// <summary>Держит фигуру на месте: восемь кадров = 0.8 c, больше готовности 0.5 c.</summary>
        private static double Hold(GhostStepAnalyzer a, StanceTestFrames f, double t, int frames = 8)
        {
            for (int i = 0; i < frames; i++, t += Dt)
                a.ProcessFrame(f.Build(t));
            return t;
        }

        /// <summary>
        /// Ведёт фигуру на <paramref name="alongForwardShanks"/> голеней по её направлению
        /// «вперёд» (отрицательное значение — назад) за три кадра и останавливает: повтор
        /// закрывает именно остановка, поэтому кадры покоя — часть шага, а не хвост.
        /// <paramref name="hopShanks"/> поднимает фигуру в середине шага (подпрыгивание),
        /// <paramref name="landing"/> правит фигуру ровно перед приземлением.
        /// </summary>
        private static double Step(GhostStepAnalyzer a, StanceTestFrames f, double t,
            float alongForwardShanks, float hopShanks = 0f, Action<StanceTestFrames> landing = null)
        {
            const int moving = 3;
            float from = f.OffsetX;
            float to = from + alongForwardShanks * f.Shank * (f.ForwardSign >= 0f ? 1f : -1f);

            for (int i = 1; i <= moving; i++, t += Dt)
            {
                f.OffsetX = from + (to - from) * i / moving;
                f.OffsetY = i < moving ? -hopShanks * f.Shank : 0f;   // взлетел и приземлился
                a.ProcessFrame(f.Build(t));
            }

            landing?.Invoke(f);
            return Hold(a, f, t, 4);
        }

        [Test]
        public void CleanForwardStepCountsARep()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0), 0.9f);

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(string.Empty, a.Cue);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void CleanBackStepCountsARep()
        {
            var a = new GhostStepAnalyzer(forward: false);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0), -0.9f);

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(string.Empty, a.Cue);
        }

        [Test]
        public void HopBreaksTheStep()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0), 0.9f, hopShanks: 0.4f);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Не подпрыгивай", a.Cue);
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);
        }

        [Test]
        public void ShortStepAsksForMore()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0), 0.5f);          // порог 0.8 голени

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Шире шаг", a.Cue);
        }

        [Test]
        public void SwappingTheFrontLegBreaksTheStance()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            // Середина лодыжек от смены ведущей ноги не двигается, так что шаг всё равно
            // закрывается и судится — а нога впереди уже другая.
            Step(a, f, Hold(a, f, 0.0), 0.9f, landing: x => x.FrontIsLeft = false);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Держи стойку", a.Cue);
        }

        [Test]
        public void LandingInTheOtherStanceBreaksTheStance()
        {
            // Порог подпрыгивания снят: смена стойки сама поднимает голову, а проверяется
            // здесь именно распознавание чужой стойки на выходе.
            var a = new GhostStepAnalyzer(forward: true, maxHeadBobShanks: 1f);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0), 0.9f, landing: x =>
            {
                x.Length01 = 1.3f;                      // приземлился в фудо
                x.FrontKneeDeg = 160f;
                x.BackKneeDeg = 160f;
            });

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Держи стойку", a.Cue);
        }

        [Test]
        public void StanceFaultOnLandingIsNamed()
        {
            var a = new GhostStepAnalyzer(forward: true, maxHeadBobShanks: 1f);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0), 0.9f, landing: x => x.TorsoLeanDeg = 25f);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Корпус прямо", a.Cue);     // фраза из словаря стойки
        }

        [Test]
        public void StepTheWrongWayIsNeitherRepNorNoRep()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            double t = Step(a, f, Hold(a, f, 0.0), -0.9f);   // возврат в исходную точку
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);

            Step(a, f, Hold(a, f, t), 0.9f);                 // а вперёд — как обычно
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void MirroredStanceStepsTheSame()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu(mirrored: true);   // ForwardSign = -1

            Step(a, f, Hold(a, f, 0.0), 0.9f);

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void StepStartedBeforeTheReadyHoldDoesNotCount()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0, frames: 2), 0.9f);       // 0.2 c вместо 0.5

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void OneStepIsCountedOnce()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            double t = Step(a, f, Hold(a, f, 0.0), 0.9f);
            Hold(a, f, t, 20);                                  // стоит и не шевелится

            Assert.AreEqual(1, a.Reps);
        }

        [Test]
        public void StepLostToTrackingIsNotJudged()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();
            double t = Hold(a, f, 0.0);

            f.OffsetX = 0.45f * f.Shank;                    // пошёл
            a.ProcessFrame(f.Build(t));
            t += Dt;

            f.FootVisibility = 0.1f;                        // и пропал из кадра на 1.5 c
            t = Hold(a, f, t, 15);

            f.FootVisibility = 1f;                          // вернулся уже стоящим
            f.OffsetX = 0.9f * f.Shank;
            Hold(a, f, t, 4);

            Assert.AreEqual(0, a.Reps);                     // повтор из устаревшей базы не выдуман
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void UnreadableFrameAsksForFraming()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();
            f.FootVisibility = 0.1f;

            a.ProcessFrame(f.Build(0.0));

            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (боком)", a.Cue);
            Assert.AreEqual(0, a.Reps);
        }

        [TestCase(true, "ghoststep-forward", "Ghost step вперёд")]
        [TestCase(false, "ghoststep-back", "Ghost step назад")]
        public void IdentityMatchesTheCatalogContract(bool forward, string id, string displayName)
        {
            var a = new GhostStepAnalyzer(forward);

            Assert.AreEqual(id, a.Id);
            Assert.AreEqual(displayName, a.DisplayName);
        }

        [Test]
        public void NoStanceKindIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new GhostStepAnalyzer(forward: true, stance: StanceKind.None));
        }

        [Test]
        public void ResetClearsTheSet()
        {
            var a = new GhostStepAnalyzer(forward: true);
            var f = StanceTestFrames.Zenkutsu();

            Step(a, f, Hold(a, f, 0.0), 0.9f);
            a.Reset();

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (боком)", a.Cue);
        }
    }
}
