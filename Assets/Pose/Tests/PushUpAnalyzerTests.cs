using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Integration coverage for the analyzer: gating (visibility + push-up position), counting,
    /// and the form tally. Smoothing is off by default; the bottom is held two frames to satisfy
    /// the counter's debounce.
    /// </summary>
    public class PushUpAnalyzerTests
    {
        private static PushUpAnalyzer NewAnalyzer() => new PushUpAnalyzer(smoothingAlpha: 1f);

        // One rep at the given hip offset; the bottom is held two frames because the
        // default counter debounces the Down phase (2 consecutive below-threshold frames).
        private static void Rep(PushUpAnalyzer a, float hipOffset, ref double t)
        {
            a.ProcessFrame(PoseTestFrames.Build(170f, hipOffset, 1f, t)); t += 0.5;
            a.ProcessFrame(PoseTestFrames.Build(80f, hipOffset, 1f, t)); t += 0.5;
            a.ProcessFrame(PoseTestFrames.Build(80f, hipOffset, 1f, t)); t += 0.5;
            a.ProcessFrame(PoseTestFrames.Build(170f, hipOffset, 1f, t)); t += 0.5;
        }

        [Test]
        public void CleanRep_CountsAsGood()
        {
            var a = NewAnalyzer();
            double t = 0;
            Rep(a, 0f, ref t);

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(PushUpFault.None, a.CurrentFault);
        }

        [Test]
        public void ThreeCleanReps_CountThree()
        {
            var a = NewAnalyzer();
            double t = 0;
            Rep(a, 0f, ref t);
            Rep(a, 0f, ref t);
            Rep(a, 0f, ref t);
            Assert.AreEqual(3, a.Reps);
        }

        [Test]
        public void BentRep_CountsButTalliedAsNoRep()
        {
            var a = NewAnalyzer();
            // Straight at the top, bent (but still a plank) at the bottom.
            a.ProcessFrame(PoseTestFrames.Build(170f, 0f, 1f, 0.0));
            a.ProcessFrame(PoseTestFrames.Build(80f, 0.06f, 1f, 0.5));
            a.ProcessFrame(PoseTestFrames.Build(80f, 0.06f, 1f, 1.0));
            a.ProcessFrame(PoseTestFrames.Build(170f, 0f, 1f, 1.5));

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(1, a.NoReps);
        }

        [Test]
        public void OutOfPosition_DoesNotCount()
        {
            var a = NewAnalyzer();
            double t = 0;
            // Same elbow motion but the body is sharply bent (not a push-up position).
            Rep(a, 0.16f, ref t);

            Assert.AreEqual(0, a.Reps, "Elbow motion while not in a push-up position must not count.");
            Assert.AreEqual(PushUpFault.NotInPosition, a.CurrentFault);
        }

        [Test]
        public void BodyNotVisible_HoldsCount()
        {
            var a = NewAnalyzer();
            double t = 0;
            Rep(a, 0f, ref t);
            Assert.AreEqual(1, a.Reps);

            a.ProcessFrame(PoseTestFrames.Build(170f, 0f, 0.1f, t)); // low visibility

            Assert.IsFalse(a.BodyVisible);
            Assert.AreEqual("В кадр", a.Cue);
            Assert.AreEqual(1, a.Reps);
        }

        [Test]
        public void LiveCue_ReflectsBentBody()
        {
            var a = NewAnalyzer();
            a.ProcessFrame(PoseTestFrames.Build(120f, 0.06f, 1f, 0.0));
            Assert.AreEqual("Держи тело прямым", a.Cue);
        }

        [Test]
        public void Reset_ClearsState()
        {
            var a = NewAnalyzer();
            double t = 0;
            Rep(a, 0f, ref t);
            a.Reset();

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.IsFalse(a.BodyVisible);
            Assert.AreEqual("В кадр", a.Cue);
        }

        [Test]
        public void StandingArmSwing_DoesNotCount()
        {
            var analyzer = new PushUpAnalyzer(smoothingAlpha: 1f);
            analyzer.ProcessFrame(PoseTestFrames.BuildStanding(170f, timestamp: 0.0));
            analyzer.ProcessFrame(PoseTestFrames.BuildStanding(100f, timestamp: 1.0));
            analyzer.ProcessFrame(PoseTestFrames.BuildStanding(170f, timestamp: 2.0));
            Assert.AreEqual(0, analyzer.Reps);
        }
    }
}
