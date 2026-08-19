using System;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class StanceAnalyzerTests
    {
        // Кадры раз в 0.5 c: пауза длиннее грейса HoldTimer рвёт удержание.
        private static void Feed(StanceAnalyzer a, StanceTestFrames f, double from, double to)
        {
            for (double t = from; t <= to + 1e-9; t += 0.5)
                a.ProcessFrame(f.Build(t));
        }

        private static StanceTestFrames Faulty()
        {
            var f = StanceTestFrames.Zenkutsu();
            f.Length01 = 1.5f;                              // короткая стойка -> "Шире шаг"
            return f;
        }

        [Test]
        public void ThreeCleanSecondsCountOneRep()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            Feed(a, StanceTestFrames.Zenkutsu(), 0.0, 3.0);

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(string.Empty, a.Cue);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void ShortHoldDoesNotCount()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            Feed(a, StanceTestFrames.Zenkutsu(), 0.0, 2.5);

            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void FudoIsHeldAndCountedToo()
        {
            var a = new StanceAnalyzer(StanceKind.Fudo);
            Feed(a, StanceTestFrames.Fudo(), 0.0, 3.0);

            Assert.AreEqual(1, a.Reps);
        }

        [Test]
        public void FaultMidHoldResetsTheHoldButKeepsTheScore()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            var clean = StanceTestFrames.Zenkutsu();

            Feed(a, clean, 0.0, 3.0);                       // первый чистый повтор
            Feed(a, clean, 3.5, 4.5);                       // 1 c нового удержания
            a.ProcessFrame(Faulty().Build(5.0));            // сорвал

            Assert.AreEqual(1, a.Reps);                     // счёт не обнуляется
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Шире шаг", a.Cue);
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);

            Feed(a, clean, 5.5, 8.5);                       // удержание считается заново
            Assert.AreEqual(2, a.Reps);
            Assert.AreEqual(1, a.NoReps);
        }

        [Test]
        public void OneNoRepPerBrokenHoldNotPerFaultyFrame()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            Feed(a, StanceTestFrames.Zenkutsu(), 0.0, 1.5);
            Feed(a, Faulty(), 2.0, 4.0);                    // пять кривых кадров подряд

            Assert.AreEqual(1, a.NoReps);
        }

        [Test]
        public void FaultBeforeAnyHoldIsNotANoRep()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            Feed(a, Faulty(), 0.0, 2.0);                    // так и не встал в стойку

            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual("Шире шаг", a.Cue);
        }

        [Test]
        public void TrackerBlinkWithinGraceKeepsTheHold()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            var clean = StanceTestFrames.Zenkutsu();
            var blink = StanceTestFrames.Zenkutsu();
            blink.FootVisibility = 0.1f;

            Feed(a, clean, 0.0, 2.0);
            a.ProcessFrame(blink.Build(2.5));               // моргнул трекер
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual(0, a.NoReps);                   // потеря трекинга — не ошибка

            a.ProcessFrame(clean.Build(3.0));               // разрыв 1.0 c <= grace
            Assert.AreEqual(1, a.Reps);
        }

        [Test]
        public void UnreadableFrameAsksForFraming()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            var hidden = StanceTestFrames.Zenkutsu();
            hidden.FootVisibility = 0.1f;

            a.ProcessFrame(hidden.Build(0.0));

            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (боком)", a.Cue);
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void CueClearsAsSoonAsTheStanceIsFixed()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);

            a.ProcessFrame(Faulty().Build(0.0));
            Assert.AreEqual("Шире шаг", a.Cue);

            a.ProcessFrame(StanceTestFrames.Zenkutsu().Build(0.5));
            Assert.AreEqual(string.Empty, a.Cue);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void MirroredStanceScoresTheSame()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            Feed(a, StanceTestFrames.Zenkutsu(mirrored: true), 0.0, 3.0);

            Assert.AreEqual(1, a.Reps);
        }

        [TestCase(StanceKind.Fudo, "stance-fudo", "Fudo dachi")]
        [TestCase(StanceKind.Zenkutsu, "stance-zenkutsu", "Zenkutsu dachi")]
        public void IdentityMatchesTheCatalogContract(StanceKind kind, string id, string displayName)
        {
            var a = new StanceAnalyzer(kind);

            Assert.AreEqual(id, a.Id);
            Assert.AreEqual(displayName, a.DisplayName);
        }

        [Test]
        public void NoStanceKindIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceAnalyzer(StanceKind.None));
        }

        [Test]
        public void ResetClearsTheSet()
        {
            var a = new StanceAnalyzer(StanceKind.Zenkutsu);
            Feed(a, StanceTestFrames.Zenkutsu(), 0.0, 3.0);
            a.Reset();

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (боком)", a.Cue);
        }
    }
}
