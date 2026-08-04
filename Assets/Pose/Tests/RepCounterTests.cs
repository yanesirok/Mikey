using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Unit coverage for the pure rep state machine: full reps count, shallow dips don't,
    /// hysteresis prevents double-counting, too-fast (noise) reps are rejected, and a mid-rep
    /// start produces no phantom rep.
    /// </summary>
    public class RepCounterTests
    {
        // Feeds a sequence of angles 0.5s apart; returns how many reps completed.
        private static int Feed(RepCounter counter, params float[] angles)
        {
            int completed = 0;
            double t = 0;
            foreach (float a in angles)
            {
                if (counter.Update(a, t))
                    completed++;
                t += 0.5;
            }
            return completed;
        }

        [Test]
        public void StartsAtZero()
        {
            var counter = new RepCounter();
            Assert.AreEqual(0, counter.Reps);
            Assert.AreEqual(RepPhase.Unknown, counter.Phase);
        }

        [Test]
        public void FullRep_CountsOnce()
        {
            var counter = new RepCounter();
            int completed = Feed(counter, 170f, 80f, 170f);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, counter.Reps);
            Assert.AreEqual(RepPhase.Up, counter.Phase);
        }

        [Test]
        public void ShallowDip_DoesNotCount()
        {
            var counter = new RepCounter();
            Feed(counter, 170f, 130f, 115f, 170f); // never crosses the depth threshold
            Assert.AreEqual(0, counter.Reps);
        }

        [Test]
        public void ThreeReps_CountThree()
        {
            var counter = new RepCounter();
            Feed(counter, 170f, 80f, 170f, 85f, 165f, 70f, 170f);
            Assert.AreEqual(3, counter.Reps);
        }

        [Test]
        public void MidBandWobble_DoesNotDoubleCount()
        {
            var counter = new RepCounter();
            Feed(counter, 170f, 80f, 170f, 150f, 120f, 150f, 130f);
            Assert.AreEqual(1, counter.Reps);
        }

        [Test]
        public void TooFastRep_IsRejectedAsNoise()
        {
            var counter = new RepCounter(minRepSeconds: 0.3);
            counter.Update(170f, 0.00); // top
            counter.Update(80f, 0.05);  // bottom at 0.05s
            bool done = counter.Update(170f, 0.10); // back to top only 0.05s later
            Assert.IsFalse(done, "A rep faster than the minimum duration is noise, not a rep.");
            Assert.AreEqual(0, counter.Reps);
        }

        [Test]
        public void StartingMidDescent_NoPhantomRep()
        {
            var counter = new RepCounter();
            Feed(counter, 80f, 85f, 170f); // app starts already at the bottom
            Assert.AreEqual(0, counter.Reps);
            Assert.AreEqual(RepPhase.Up, counter.Phase);

            Feed(counter, 80f, 170f);
            Assert.AreEqual(1, counter.Reps);
        }

        [Test]
        public void Reset_ClearsState()
        {
            var counter = new RepCounter();
            Feed(counter, 170f, 80f, 170f);
            counter.Reset();
            Assert.AreEqual(0, counter.Reps);
            Assert.AreEqual(RepPhase.Unknown, counter.Phase);
        }

        [Test]
        public void SingleFrameSpikeDoesNotOpenDown_WithDebounce()
        {
            var counter = new RepCounter(downDebounceFrames: 2);
            counter.Update(170f, 0.0);
            counter.Update(100f, 0.5);                 // одиночный шумовой скачок
            Assert.IsFalse(counter.Update(170f, 1.0)); // Down не открылся — не повтор
            Assert.AreEqual(0, counter.Reps);
            counter.Update(100f, 1.5);
            counter.Update(100f, 2.0);                 // два подряд — Down открыт
            Assert.IsTrue(counter.Update(170f, 2.5));
            Assert.AreEqual(1, counter.Reps);
        }

        [Test]
        public void MidBandFrameBreaksTheDebounceStreak()
        {
            var counter = new RepCounter(downDebounceFrames: 2);
            counter.Update(170f, 0.0);
            counter.Update(100f, 0.5);
            counter.Update(120f, 1.0);                 // середина диапазона — серия сброшена
            counter.Update(100f, 1.5);
            counter.Update(170f, 2.0);                 // Down так и не открылся
            Assert.AreEqual(0, counter.Reps);
        }

        [Test]
        public void ResetDownStreakBreaksTheSeries()
        {
            var counter = new RepCounter(downDebounceFrames: 2);
            counter.Update(170f, 0.0);
            counter.Update(100f, 0.5);
            counter.ResetDownStreak();                 // провал трекинга между кадрами
            counter.Update(100f, 1.0);                 // серия начата заново — это 1-й, не 2-й
            counter.Update(170f, 1.5);
            Assert.AreEqual(0, counter.Reps);
        }
    }
}
