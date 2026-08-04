using System.Collections.Generic;
using NUnit.Framework;
using Mikey.Pose;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Regression corpus: real on-device recordings. real_pushups — characterization of current
    /// configuration (user reported missing counts in previous logic); pushups_with_plank_holds — user's
    /// ground truth (5–6 reps with form faults and plank holds); walking_noise — zero reps baseline.
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
        public void RealRecording_CountsFour()
        {
            // 4 — характеризация текущей конфигурации, не граунд-трус: прежние «2» были
            // находкой старой (терявшей повторы) логики, а не правдой. Пользователь в той
            // сессии жаловался, что счёт не шёл вовсе.
            Assert.AreEqual(4, Replay("Pose/Tests/Recordings/real_pushups.csv"));
        }

        [Test]
        public void PlankHoldsRecording_CountsFive()
        {
            // Граунд-трус пользователя: 5–6 отжиманий с ошибками формы + удержания планки.
            Assert.AreEqual(5, Replay("Pose/Tests/Recordings/pushups_with_plank_holds.csv"));
        }

        [Test]
        public void WalkingRecording_CountsNothing()
        {
            Assert.AreEqual(0, Replay("Pose/Tests/Recordings/walking_noise.csv"));
        }
    }
}
