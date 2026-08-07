using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Characterization corpus for yoko geri gedan: a real on-device session
    /// (2026-08-06, ground truth: three slow kicks and one chudan-height swing count,
    /// three knee raises must not). Guards the apex-gate scoring against regressions.
    /// </summary>
    public class YokoGeriRecordingTests
    {
        [Test]
        public void GedanSession_CountsKicksNotKneeRaises()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/yoko_gedan_session.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(3, analyzer.Reps);            // чудан-мах теперь «Ниже», не зачёт
            Assert.AreEqual(4, analyzer.NoReps);
            Assert.AreEqual(KickZone.Chudan, analyzer.BestZone);
        }

        [Test]
        public void MixedSession_CountsKicksRejectsRaisesAndSwings()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/yoko_gedan_mixed.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(5, analyzer.Reps);
            Assert.AreEqual(9, analyzer.NoReps);
            Assert.AreEqual(KickZone.Gedan, analyzer.BestZone);   // махи прямой ногой без замаха не в зачёте
        }

        [Test]
        public void HeightsSession_RejectsKicksAboveRequested()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/yoko_gedan_heights.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(4, analyzer.Reps);
            Assert.AreEqual(5, analyzer.NoReps);
            Assert.AreEqual(KickZone.Jodan, analyzer.BestZone);   // дзёдан-удары растяжку показали
        }

        [Test]
        public void WalkingRecording_CountsNothing()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            foreach (PoseFrame f in CsvPoseFrames.Load("Pose/Tests/Recordings/walking_noise.csv"))
                analyzer.ProcessFrame(f);
            Assert.AreEqual(0, analyzer.Reps);
        }
    }
}
