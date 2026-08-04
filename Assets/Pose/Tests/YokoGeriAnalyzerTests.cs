using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class YokoGeriAnalyzerTests
    {
        private const float Floor = 0.9f, Raised = 0.45f;

        private static YokoGeriAnalyzer NewAnalyzer() => new YokoGeriAnalyzer(smoothingAlpha: 1f);

        private static void Feed(YokoGeriAnalyzer a, float ankleY, double t, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Kick(ankleY, chambered: false, vis, t));

        [Test]
        public void SlowRaiseCounts()
        {
            var a = NewAnalyzer();
            Feed(a, Floor, 0.0);
            Feed(a, Raised, 1.0);
            Feed(a, Raised, 3.5);      // держит
            Feed(a, Floor, 4.0);       // подъём длился 3.0 c ≥ 2.0
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(3.0, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void FastSwingIsNoRep()
        {
            var a = NewAnalyzer();
            Feed(a, Floor, 0.0);
            Feed(a, Raised, 0.3);
            Feed(a, Floor, 1.0);       // подъём 0.7 c < 2.0 — быстрый мах
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Медленнее", a.Cue);
        }

        [Test]
        public void HoldSecondsAccumulateAcrossReps()
        {
            var a = NewAnalyzer();
            double t = 0;
            for (int i = 0; i < 2; i++)
            {
                Feed(a, Floor, t); t += 0.5;
                Feed(a, Raised, t); t += 2.5;
                Feed(a, Floor, t); t += 0.5;
            }
            Assert.AreEqual(2, a.Reps);
            Assert.AreEqual(5.0, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void LowVisibilityReportsNotVisibleWithFrontCue()
        {
            var a = NewAnalyzer();
            Feed(a, Floor, 0.0, vis: 0.3f);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (лицом)", a.Cue);
        }

        [Test]
        public void RegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-slow"));
        }
    }
}
