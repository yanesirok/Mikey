using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Characterization corpus for the wall-sit hold: a real on-device session
    /// (2026-08-05, ground truth "several 5–10 s holds against the wall") plus the
    /// walking recording as a negative control. Guards the margin-window scoring
    /// against regressions, nothing more.
    /// </summary>
    public class WallSitRecordingTests
    {
        [Test]
        public void WallSitSession_BestHoldIsSixSeconds()
        {
            var analyzer = new WallSitAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/wallsit_session.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(6, analyzer.Reps);
        }

        [Test]
        public void WalkingRecording_AccumulatesNothing()
        {
            var analyzer = new WallSitAnalyzer();
            foreach (PoseFrame f in CsvPoseFrames.Load("Pose/Tests/Recordings/walking_noise.csv"))
                analyzer.ProcessFrame(f);
            Assert.AreEqual(0, analyzer.Reps);
        }
    }
}
