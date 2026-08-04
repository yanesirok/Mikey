using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Characterization corpus for squats: a real on-device capture (2026-08-05) that glues
    /// several sessions together (the recording buffer wasn't cleared between exercises back
    /// then), so 16 is what the lenient configuration finds — not user ground truth. Guards
    /// against scoring regressions, nothing more.
    /// </summary>
    public class SquatRecordingTests
    {
        [Test]
        public void MixedSessionsRecording_CountsSixteen()
        {
            var analyzer = new SquatAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/squats_mixed_sessions.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(16, analyzer.Reps);
        }
    }
}
