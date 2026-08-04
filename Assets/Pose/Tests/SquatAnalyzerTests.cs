using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class SquatAnalyzerTests
    {
        private static SquatAnalyzer NewAnalyzer() => new SquatAnalyzer(smoothingAlpha: 1f);

        private static void Feed(SquatAnalyzer a, float kneeAngleDeg, double t, float lean = 0f, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Squat(kneeAngleDeg, lean, vis, t));

        [Test]
        public void CountsCleanRep()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0);     // глубокий сед
            Feed(a, 175f, 2.0);    // встал
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void ShallowSquatDoesNotCount()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 120f, 1.0);    // недосед
            Feed(a, 175f, 2.0);
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void ThresholdJitterDoesNotProducePhantomReps()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 0.05);    // «повтор» за 0.1 c — дрожание сигнала, не движение
            Feed(a, 175f, 0.10);
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void TorsoLeanAtBottomIsTalliedButStillCounts()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0, lean: 60f);   // сед с сильным завалом корпуса
            Feed(a, 175f, 2.0);
            Assert.AreEqual(1, a.Reps);      // мягкий скоринг: повтор идёт
            Assert.AreEqual(1, a.NoReps);    // но огрех зафиксирован
        }

        [Test]
        public void LowVisibilityPausesCountingAndReportsNotVisible()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0, vis: 0.3f);   // трекинг потерян в нижней точке
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Feed(a, 175f, 2.0);
            Assert.AreEqual(0, a.Reps);      // низ не был увиден достоверно
        }

        [Test]
        public void ResetClearsSet()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0);
            Feed(a, 175f, 2.0);
            a.Reset();
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void RegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("squat"));
        }
    }
}
