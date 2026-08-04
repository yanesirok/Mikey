using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class WallSitAnalyzerTests
    {
        // Кадры в позе — раз в 0.5 c: паузы длиннее грейса HoldTimer рвут удержание.
        private static void Sit(WallSitAnalyzer a, double from, double to)
        {
            for (double t = from; t <= to + 1e-9; t += 0.5)
                a.ProcessFrame(LegTestFrames.WallSit(timestamp: t));
        }

        [Test]
        public void AccumulatesSecondsWhileSeated()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 5.0);
            Assert.AreEqual(5, a.Reps);
            Assert.AreEqual(5.0, a.BestHoldSeconds, 1e-6);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void TooHighSeatGivesLowerCueAndStopsTimer()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 3.0);
            a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 150f, timestamp: 3.5));   // встал слишком высоко
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);
            Assert.AreEqual("Ниже", a.Cue);
            a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 150f, timestamp: 10.0));
            Assert.AreEqual(3, a.Reps);                     // лучший результат остался 3 с
        }

        [Test]
        public void TooDeepSeatGivesHigherCue()
        {
            var a = new WallSitAnalyzer();
            a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 55f, timestamp: 0.0));
            Assert.AreEqual("Выше", a.Cue);
        }

        [Test]
        public void TrackerBlinkWithinGraceKeepsTheHold()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 4.0);
            a.ProcessFrame(LegTestFrames.WallSit(visibility: 0.2f, timestamp: 4.5));     // моргнул
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            a.ProcessFrame(LegTestFrames.WallSit(timestamp: 5.0));                       // разрыв 1.0 c ≤ grace
            Assert.AreEqual(5, a.Reps);                     // удержание не прервалось
        }

        [Test]
        public void ResetClearsBest()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 5.0);
            a.Reset();
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void RegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("wallsit"));
        }

        [Test]
        public void FloorSitDoesNotAccumulate()
        {
            var a = new WallSitAnalyzer();
            for (double t = 0.0; t <= 5.0 + 1e-9; t += 0.5)
                a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 55f, timestamp: t));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual("Выше", a.Cue);
        }

        [Test]
        public void HeavyLeanPausesTimerWithWallCue()
        {
            var a = new WallSitAnalyzer();
            for (double t = 0.0; t <= 5.0 + 1e-9; t += 0.5)
                a.ProcessFrame(LegTestFrames.WallSit(hipAngleDeg: 140f, timestamp: t));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);
            Assert.AreEqual("Спиной к стене", a.Cue);
        }
    }
}
