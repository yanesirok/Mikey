using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class YokoGeriAnalyzerTests
    {
        private const float Floor = 0.9f, GedanY = 0.65f, ChudanY = 0.35f, JodanY = 0.18f;

        private static YokoGeriAnalyzer NewAnalyzer(KickZone requested) =>
            new YokoGeriAnalyzer(requested, smoothingAlpha: 1f);

        private static void Feed(YokoGeriAnalyzer a, float ankleY, double t, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Kick(ankleY, chambered: false, vis, t));

        [Test]
        public void KickToRequestedZoneCountsAtAnyTempo()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            Feed(a, Floor, 0.0);
            Feed(a, ChudanY, 0.3);
            Feed(a, Floor, 0.6);       // быстрый мах — темп свободный
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void KickAboveRequestedZoneCounts()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0);
            Feed(a, JodanY, 0.3);
            Feed(a, Floor, 0.6);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(KickZone.Jodan, a.BestZone);
        }

        [Test]
        public void KickBelowRequestedZoneIsNoRepWithHigherCue()
        {
            var a = NewAnalyzer(KickZone.Jodan);
            Feed(a, Floor, 0.0);
            Feed(a, GedanY, 0.3);
            Feed(a, Floor, 0.6);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выше", a.Cue);
            Assert.AreEqual(KickZone.Gedan, a.BestZone);   // лучшая зона копится и на незачёте
        }

        [Test]
        public void HighChamberWithoutExtensionIsNoRepWithExtendCue()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            a.ProcessFrame(LegTestFrames.Kick(Floor, chambered: false, 1f, 0.0));
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));
            a.ProcessFrame(LegTestFrames.Kick(Floor, chambered: false, 1f, 0.6));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выпрями ногу", a.Cue);
            Assert.AreEqual(KickZone.None, a.BestZone);
        }

        [Test]
        public void AirtimeAccumulatesForCountedReps()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            Feed(a, Floor, 0.0);
            Feed(a, ChudanY, 1.0);
            Feed(a, ChudanY, 3.0);
            Feed(a, Floor, 3.5);       // в воздухе 2.5 c
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(2.5, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void FailedKickAddsNoAirtime()
        {
            var a = NewAnalyzer(KickZone.Jodan);
            Feed(a, Floor, 0.0);
            Feed(a, GedanY, 1.0);
            Feed(a, Floor, 2.0);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0.0, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void LowVisibilityReportsNotVisibleWithFrontCue()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0, vis: 0.3f);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (лицом)", a.Cue);
        }

        [Test]
        public void CatalogHasThreeZoneVariantsAndNoSlow()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-gedan"));
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-chudan"));
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-jodan"));
            Assert.IsNull(ExerciseCatalog.Create("yokogeri-slow"));
        }
    }
}
