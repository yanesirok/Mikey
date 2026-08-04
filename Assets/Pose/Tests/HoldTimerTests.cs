using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class HoldTimerTests
    {
        // Кадры в позе идут не реже грейса (в реале это 15–30 fps): пауза между
        // true-кадрами длиннее грейса означает потерю данных и рвёт удержание.

        private static void Hold(HoldTimer t, double from, double to)
        {
            for (double s = from; s <= to + 1e-9; s += 0.5)
                t.Update(true, s);
        }

        [Test]
        public void AccumulatesWhileInPose()
        {
            var t = new HoldTimer();
            Hold(t, 0.0, 2.5);
            Assert.AreEqual(2.5, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(2.5, t.BestSeconds, 1e-6);
        }

        [Test]
        public void ShortGapWithinGraceDoesNotBreakTheHold()
        {
            var t = new HoldTimer(graceSeconds: 1.0);
            Hold(t, 0.0, 2.0);
            t.Update(false, 2.4);          // моргнул трекер
            Hold(t, 2.8, 4.0);             // разрыв 0.8 с ≤ grace — удержание продолжается
            Assert.AreEqual(4.0, t.CurrentSeconds, 1e-6);
        }

        [Test]
        public void LongGapBreaksTheHoldButKeepsBest()
        {
            var t = new HoldTimer(graceSeconds: 1.0);
            Hold(t, 0.0, 3.0);
            t.Update(false, 5.0);          // разрыв больше grace
            Assert.AreEqual(0.0, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(3.0, t.BestSeconds, 1e-6);
            Hold(t, 6.0, 7.5);             // новое удержание с нуля
            Assert.AreEqual(1.5, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(3.0, t.BestSeconds, 1e-6);
        }

        [Test]
        public void SparseInPoseFramesBeyondGraceStartFresh()
        {
            var t = new HoldTimer(graceSeconds: 1.0);
            Hold(t, 0.0, 2.0);
            // ни одного Update(false,·): просто длинная пауза между true-кадрами
            t.Update(true, 10.0);
            Assert.AreEqual(0.0, t.CurrentSeconds, 1e-6);
            t.Update(true, 11.0);
            Assert.AreEqual(1.0, t.CurrentSeconds, 1e-6);
        }

        [Test]
        public void ResetClearsEverything()
        {
            var t = new HoldTimer();
            t.Update(true, 0.0);
            t.Update(true, 5.0);
            t.Reset();
            Assert.AreEqual(0.0, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(0.0, t.BestSeconds, 1e-6);
        }
    }
}
