using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// The level-1 teaching contract for the level-0 analyzers: THE SAME frame stream must score
    /// as before in <see cref="ScoringProfile.Lenient"/>, and in <see cref="ScoringProfile.Strict"/>
    /// a rep with a named fault must land ONLY in <c>NoReps</c> — never in <c>Reps</c>. A clean rep
    /// counts in both. Каждый тест гоняет один и тот же поток кадров через оба профиля: разница
    /// должна быть в политике зачёта, а не в подобранных под профиль кадрах.
    /// </summary>
    public class StrictProfileTests
    {
        private static PushUpAnalyzer PushUp(ScoringProfile p) =>
            new PushUpAnalyzer(smoothingAlpha: 1f, profile: p);

        // Один цикл отжимания: верх → низ (два кадра — дебаунс счётчика) → верх.
        private static void PushUpRep(PushUpAnalyzer a, float topElbow, float bottomElbow, float bottomHipOffset)
        {
            a.ProcessFrame(PoseTestFrames.Build(topElbow, 0f, 1f, 0.0));
            a.ProcessFrame(PoseTestFrames.Build(bottomElbow, bottomHipOffset, 1f, 0.5));
            a.ProcessFrame(PoseTestFrames.Build(bottomElbow, bottomHipOffset, 1f, 1.0));
            a.ProcessFrame(PoseTestFrames.Build(topElbow, 0f, 1f, 1.5));
        }

        [Test]
        public void PushUp_CleanRep_CountsInBothProfiles()
        {
            foreach (ScoringProfile p in new[] { ScoringProfile.Lenient, ScoringProfile.Strict })
            {
                var a = PushUp(p);
                PushUpRep(a, topElbow: 170f, bottomElbow: 80f, bottomHipOffset: 0f);
                Assert.AreEqual(1, a.Reps, p.ToString());
                Assert.AreEqual(0, a.NoReps, p.ToString());
            }
        }

        [Test]
        public void PushUp_SaggingHips_CountLenient_NoRepStrict()
        {
            var lenient = PushUp(ScoringProfile.Lenient);
            PushUpRep(lenient, 170f, 80f, bottomHipOffset: 0.06f);
            Assert.AreEqual(1, lenient.Reps, "мягкий профиль засчитывает повтор с огрехом");
            Assert.AreEqual(1, lenient.NoReps);

            var strict = PushUp(ScoringProfile.Strict);
            PushUpRep(strict, 170f, 80f, bottomHipOffset: 0.06f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual(1, strict.NoReps);
            Assert.AreEqual("Таз выше", strict.Cue);
        }

        [Test]
        public void PushUp_PikingHips_NamesTheOtherDirection()
        {
            var strict = PushUp(ScoringProfile.Strict);
            PushUpRep(strict, 170f, 80f, bottomHipOffset: -0.06f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual(1, strict.NoReps);
            Assert.AreEqual("Таз ниже", strict.Cue);
        }

        [Test]
        public void PushUp_ShallowRep_CountLenient_NoRepStrict()
        {
            // 100° — ниже порога счётчика (105°), но выше строгой глубины (90°).
            var lenient = PushUp(ScoringProfile.Lenient);
            PushUpRep(lenient, 170f, 100f, 0f);
            Assert.AreEqual(1, lenient.Reps);

            var strict = PushUp(ScoringProfile.Strict);
            PushUpRep(strict, 170f, 100f, 0f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual(1, strict.NoReps);
            Assert.AreEqual("Ниже грудь", strict.Cue);
        }

        [Test]
        public void PushUp_NoLockout_CountLenient_NoRepStrict()
        {
            // 145° — счётчик считает это «верхом» (140°), строгий профиль требует 150°.
            var lenient = PushUp(ScoringProfile.Lenient);
            PushUpRep(lenient, 145f, 80f, 0f);
            Assert.AreEqual(1, lenient.Reps);

            var strict = PushUp(ScoringProfile.Strict);
            PushUpRep(strict, 145f, 80f, 0f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual(1, strict.NoReps);
            Assert.AreEqual("Выпрями руки", strict.Cue);
        }

        [Test]
        public void PushUp_LiveCue_NamesHipDirectionOnlyInStrict()
        {
            var lenient = PushUp(ScoringProfile.Lenient);
            lenient.ProcessFrame(PoseTestFrames.Build(120f, 0.06f, 1f, 0.0));
            Assert.AreEqual("Держи тело прямым", lenient.Cue);

            var strict = PushUp(ScoringProfile.Strict);
            strict.ProcessFrame(PoseTestFrames.Build(120f, 0.06f, 1f, 0.0));
            Assert.AreEqual("Таз выше", strict.Cue);
        }

        // Один присед: стоя → сед (два кадра низа) → стоя; завал корпуса только внизу.
        private static void SquatRep(SquatAnalyzer a, float bottomLeanDeg)
        {
            a.ProcessFrame(LegTestFrames.Squat(175f, 0f, 1f, 0.0));
            a.ProcessFrame(LegTestFrames.Squat(95f, bottomLeanDeg, 1f, 1.0));
            a.ProcessFrame(LegTestFrames.Squat(95f, bottomLeanDeg, 1f, 1.5));
            a.ProcessFrame(LegTestFrames.Squat(175f, 0f, 1f, 2.0));
        }

        [Test]
        public void Squat_CleanRep_CountsInBothProfiles()
        {
            foreach (ScoringProfile p in new[] { ScoringProfile.Lenient, ScoringProfile.Strict })
            {
                var a = new SquatAnalyzer(smoothingAlpha: 1f, profile: p);
                SquatRep(a, bottomLeanDeg: 0f);
                Assert.AreEqual(1, a.Reps, p.ToString());
                Assert.AreEqual(0, a.NoReps, p.ToString());
            }
        }

        [Test]
        public void Squat_ModerateLean_CleanForLenient_NoRepForStrict()
        {
            // 40° — между строгим порогом (35°) и мягким (50°): мягкий даже огрех не пишет.
            var lenient = new SquatAnalyzer(smoothingAlpha: 1f);
            SquatRep(lenient, bottomLeanDeg: 40f);
            Assert.AreEqual(1, lenient.Reps);
            Assert.AreEqual(0, lenient.NoReps);

            var strict = new SquatAnalyzer(smoothingAlpha: 1f, profile: ScoringProfile.Strict);
            SquatRep(strict, bottomLeanDeg: 40f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual(1, strict.NoReps);
            Assert.AreEqual("Корпус прямо", strict.Cue);
        }

        [Test]
        public void Squat_HeavyLean_CountLenient_NoRepStrict()
        {
            var lenient = new SquatAnalyzer(smoothingAlpha: 1f);
            SquatRep(lenient, bottomLeanDeg: 60f);
            Assert.AreEqual(1, lenient.Reps, "мягкий профиль засчитывает повтор с завалом");
            Assert.AreEqual(1, lenient.NoReps);

            var strict = new SquatAnalyzer(smoothingAlpha: 1f, profile: ScoringProfile.Strict);
            SquatRep(strict, bottomLeanDeg: 60f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual(1, strict.NoReps);
        }

        [Test]
        public void Squat_LiveLeanCue_IsRenamedInStrict()
        {
            var lenient = new SquatAnalyzer(smoothingAlpha: 1f);
            lenient.ProcessFrame(LegTestFrames.Squat(175f, 0f, 1f, 0.0));
            lenient.ProcessFrame(LegTestFrames.Squat(95f, 60f, 1f, 1.0));
            lenient.ProcessFrame(LegTestFrames.Squat(95f, 60f, 1f, 1.5));
            Assert.AreEqual("Спину прямее", lenient.Cue);

            var strict = new SquatAnalyzer(smoothingAlpha: 1f, profile: ScoringProfile.Strict);
            strict.ProcessFrame(LegTestFrames.Squat(175f, 0f, 1f, 0.0));
            strict.ProcessFrame(LegTestFrames.Squat(95f, 60f, 1f, 1.0));
            strict.ProcessFrame(LegTestFrames.Squat(95f, 60f, 1f, 1.5));   // низ открывается на втором кадре (дебаунс)
            Assert.AreEqual("Корпус прямо", strict.Cue);
        }

        // 5 секунд удержания с кадром раз в 0.5 c (реже — рвётся грейс HoldTimer).
        private static WallSitAnalyzer Sit(ScoringProfile profile, float kneeAngleDeg)
        {
            var a = new WallSitAnalyzer(profile: profile);
            for (double t = 0.0; t <= 5.0 + 1e-9; t += 0.5)
                a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg, timestamp: t));
            return a;
        }

        [Test]
        public void WallSit_ParallelSeat_HoldsInBothProfiles()
        {
            Assert.AreEqual(5, Sit(ScoringProfile.Lenient, 90f).Reps);
            Assert.AreEqual(5, Sit(ScoringProfile.Strict, 90f).Reps);
        }

        [Test]
        public void WallSit_TooHighSeat_HoldsLenient_RejectedStrict()
        {
            // 110° — внутри мягкого окна, выше строгого (85–100°).
            Assert.AreEqual(5, Sit(ScoringProfile.Lenient, 110f).Reps);

            WallSitAnalyzer strict = Sit(ScoringProfile.Strict, 110f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual("Ниже", strict.Cue);
        }

        [Test]
        public void WallSit_TooDeepSeat_HoldsLenient_RejectedStrict()
        {
            // 75° — внутри мягкого окна, ниже строгого.
            Assert.AreEqual(5, Sit(ScoringProfile.Lenient, 75f).Reps);

            WallSitAnalyzer strict = Sit(ScoringProfile.Strict, 75f);
            Assert.AreEqual(0, strict.Reps);
            Assert.AreEqual("Выше", strict.Cue);
        }

        // Yoko geri был строгим по зоне с самого начала — профиль ничего не меняет.
        // Тест это фиксирует: если различие когда-нибудь заведут, он упадёт и напомнит,
        // что для юкогери его не проектировали.
        private static YokoGeriAnalyzer Kick(ScoringProfile profile, float peakAnkleY)
        {
            var a = new YokoGeriAnalyzer(KickZone.Chudan, smoothingAlpha: 1f, profile: profile);
            a.ProcessFrame(LegTestFrames.Kick(0.9f, chambered: false, 1f, 0.0));
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));
            a.ProcessFrame(LegTestFrames.Kick(peakAnkleY, chambered: false, 1f, 0.6));
            a.ProcessFrame(LegTestFrames.Kick(0.9f, chambered: false, 1f, 0.9));
            return a;
        }

        [Test]
        public void YokoGeri_ScoresIdenticallyInBothProfiles()
        {
            YokoGeriAnalyzer cleanLenient = Kick(ScoringProfile.Lenient, 0.35f);   // chudan
            YokoGeriAnalyzer cleanStrict = Kick(ScoringProfile.Strict, 0.35f);
            Assert.AreEqual(1, cleanLenient.Reps);
            Assert.AreEqual(cleanLenient.Reps, cleanStrict.Reps);
            Assert.AreEqual(cleanLenient.NoReps, cleanStrict.NoReps);

            YokoGeriAnalyzer lowLenient = Kick(ScoringProfile.Lenient, 0.65f);     // gedan вместо chudan
            YokoGeriAnalyzer lowStrict = Kick(ScoringProfile.Strict, 0.65f);
            Assert.AreEqual(0, lowLenient.Reps);
            Assert.AreEqual(1, lowLenient.NoReps);
            Assert.AreEqual(lowLenient.Reps, lowStrict.Reps);
            Assert.AreEqual(lowLenient.NoReps, lowStrict.NoReps);
            Assert.AreEqual(lowLenient.Cue, lowStrict.Cue);
        }
    }
}
