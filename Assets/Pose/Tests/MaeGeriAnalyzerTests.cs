using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class MaeGeriAnalyzerTests
    {
        private const float Floor = 0.9f, Gedan = 0.65f, Chudan = 0.35f, Jodan = 0.18f;

        private static MaeGeriAnalyzer NewAnalyzer(KickZone requested)
            => new MaeGeriAnalyzer(requested, smoothingAlpha: 1f);

        private static void Feed(MaeGeriAnalyzer a, float ankleY, double t, bool chambered = false, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Kick(ankleY, chambered, vis, t));

        // Полный удар с чамбером: пол → колено → выпрямление → колено → пол.
        private static void FullKick(MaeGeriAnalyzer a, float peakY, ref double t)
        {
            Feed(a, Floor, t); t += 0.2;
            Feed(a, 0f, t, chambered: true); t += 0.2;
            Feed(a, peakY, t); t += 0.2;
            Feed(a, 0f, t, chambered: true); t += 0.2;
            Feed(a, Floor, t); t += 0.2;
        }

        [Test]
        public void CountsKickAtRequestedLevel()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            double t = 0;
            FullKick(a, Chudan, ref t);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(KickZone.Chudan, a.BestZone);
        }

        [Test]
        public void HigherThanRequestedStillCounts()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            double t = 0;
            FullKick(a, Jodan, ref t);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(KickZone.Jodan, a.BestZone);
        }

        [Test]
        public void LowerThanRequestedIsNoRepWithCue()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            double t = 0;
            FullKick(a, Gedan, ref t);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выше", a.Cue);
            Assert.AreEqual(KickZone.Gedan, a.BestZone);   // гибкость меряем по факту
        }

        [Test]
        public void StraightLegLiftCountsButTalliesChamberFault()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            double t = 0;
            Feed(a, Floor, t); t += 0.3;
            Feed(a, Chudan, t); t += 0.3;                  // мах прямой ногой, без чамбера
            Feed(a, Floor, t);
            Assert.AreEqual(1, a.Reps);                    // мягкий скоринг: зачёт
            Assert.AreEqual(1, a.NoReps);                  // но огрех зафиксирован
            Assert.AreEqual("Сначала колено", a.Cue);
        }

        [Test]
        public void JitterLiftDoesNotCount()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0);
            Feed(a, Gedan, 0.05);
            Feed(a, Floor, 0.1);                            // < minLiftSeconds
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void LowVisibilityReportsNotVisible()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0, vis: 0.3f);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
        }

        [Test]
        public void AllThreeLevelsRegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("maegeri-gedan"));
            Assert.IsNotNull(ExerciseCatalog.Create("maegeri-chudan"));
            Assert.IsNotNull(ExerciseCatalog.Create("maegeri-jodan"));
        }
    }
}
