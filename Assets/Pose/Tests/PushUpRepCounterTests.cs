using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Unit coverage for the pure rep state machine: full reps count, shallow dips don't,
    /// hysteresis prevents double-counting, too-fast (noise) reps are rejected, and a mid-rep
    /// start produces no phantom rep.
    /// </summary>
    public class PushUpRepCounterTests
    {
        // Feeds a sequence of elbow angles 0.5s apart; returns how many reps completed.
        private static int Feed(PushUpRepCounter counter, params float[] angles)
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
            var counter = new PushUpRepCounter();
            Assert.AreEqual(0, counter.Reps);
            Assert.AreEqual(RepPhase.Unknown, counter.Phase);
        }

        [Test]
        public void FullRep_CountsOnce()
        {
            var counter = new PushUpRepCounter();
            int completed = Feed(counter, 170f, 80f, 170f);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, counter.Reps);
            Assert.AreEqual(RepPhase.Up, counter.Phase);
        }

        [Test]
        public void ShallowDip_DoesNotCount()
        {
            var counter = new PushUpRepCounter();
            Feed(counter, 170f, 130f, 115f, 170f); // never crosses the depth threshold
            Assert.AreEqual(0, counter.Reps);
        }

        [Test]
        public void ThreeReps_CountThree()
        {
            var counter = new PushUpRepCounter();
            Feed(counter, 170f, 80f, 170f, 85f, 165f, 70f, 170f);
            Assert.AreEqual(3, counter.Reps);
        }

        [Test]
        public void MidBandWobble_DoesNotDoubleCount()
        {
            var counter = new PushUpRepCounter();
            Feed(counter, 170f, 80f, 170f, 150f, 120f, 150f, 130f);
            Assert.AreEqual(1, counter.Reps);
        }

        [Test]
        public void TooFastRep_IsRejectedAsNoise()
        {
            var counter = new PushUpRepCounter(minRepSeconds: 0.3);
            counter.Update(170f, 0.00); // top
            counter.Update(80f, 0.05);  // bottom at 0.05s
            bool done = counter.Update(170f, 0.10); // back to top only 0.05s later
            Assert.IsFalse(done, "A rep faster than the minimum duration is noise, not a rep.");
            Assert.AreEqual(0, counter.Reps);
        }

        [Test]
        public void StartingMidDescent_NoPhantomRep()
        {
            var counter = new PushUpRepCounter();
            Feed(counter, 80f, 85f, 170f); // app starts already at the bottom
            Assert.AreEqual(0, counter.Reps);
            Assert.AreEqual(RepPhase.Up, counter.Phase);

            Feed(counter, 80f, 170f);
            Assert.AreEqual(1, counter.Reps);
        }

        [Test]
        public void Reset_ClearsState()
        {
            var counter = new PushUpRepCounter();
            Feed(counter, 170f, 80f, 170f);
            counter.Reset();
            Assert.AreEqual(0, counter.Reps);
            Assert.AreEqual(RepPhase.Unknown, counter.Phase);
        }
    }
}
