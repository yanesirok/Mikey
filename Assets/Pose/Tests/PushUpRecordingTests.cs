using System.Collections.Generic;
using NUnit.Framework;
using Mikey.Pose;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Regression corpus: two real on-device recordings (2026-08-04). Any scoring change
    /// that breaks genuine push-ups or resurrects phantom reps fails here, not on a phone.
    /// </summary>
    public class PushUpRecordingTests
    {
        private static int Replay(string path)
        {
            var analyzer = new PushUpAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load(path);
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            return analyzer.Reps;
        }

        [Test]
        public void RealRecording_CountsTwoReps()
        {
            Assert.AreEqual(2, Replay("Pose/Tests/Recordings/real_pushups.csv"));
        }

        [Test]
        public void WalkingRecording_CountsNothing()
        {
            Assert.AreEqual(0, Replay("Pose/Tests/Recordings/walking_noise.csv"));
        }
    }
}
