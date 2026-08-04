using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Characterization corpus for squats: a real on-device capture (2026-08-05) that glues
    /// several sessions together (the recording buffer wasn't cleared between exercises back
    /// then), so 18 is what the lenient configuration finds with the hip-over-knee signal v2 —
    /// not user ground truth. Guards against scoring regressions, nothing more.
    /// </summary>
    public class SquatRecordingTests
    {
        [Test]
        public void MixedSessionsRecording_CountsEighteen()
        {
            // Сессия 2026-08-05 00:05–00:30: смешанная запись 3 подходов. 18 — сигнал
            // «таз над коленями», v2.
            var analyzer = new SquatAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/squats_mixed_sessions.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(18, analyzer.Reps);
        }

        [Test]
        public void SideAndFrontRecording_CountsFifteen()
        {
            // Сессия 2026-08-05 02:07: ~8–10 приседаний сбоку + 3–4 анфас (граунд-трус
            // пользователя «~12–14»). 15 — характеризация сигнала v2: анфас ловится.
            var analyzer = new SquatAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/squats_side_and_front.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(15, analyzer.Reps);
        }

        [Test]
        public void WalkingRecording_CountsOneKneel()
        {
            // Запись «хожу/делаю другое»: единственный спорный случай — опускание на пол,
            // где обе ноги реально согнуты (геометрически это присед). Щедрый уровень 0
            // это терпит; тест документирует ровно 1, чтобы регресс в обе стороны был виден.
            var analyzer = new SquatAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/walking_noise.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(1, analyzer.Reps);
        }
    }
}
