using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Level-1 design, "Тестирование": clean rep; punched with the rear hand; arm not
    /// straightened; wrist below jodan; stance at the peak is not zenkutsu; arm never
    /// folded back into kamae. Plus the mirrored fighter — a flipped
    /// <see cref="StanceReading.ForwardSign"/> must change no verdict.
    ///
    /// Every arm pose is built from shoulder-relative offsets of
    /// <see cref="StanceTestFrames"/>: "Forward" runs along the facing direction and "Up"
    /// against Y, so an elbow placed exactly halfway between shoulder and wrist reads back
    /// as a straight (180°) arm whichever way the fighter faces.
    /// </summary>
    public class KizamiZukiAnalyzerTests
    {
        private const double Step = 0.25;

        private static void Feed(KizamiZukiAnalyzer a, StanceTestFrames f, double from, double to)
        {
            for (double t = from; t <= to + 1e-9; t += Step)
                a.ProcessFrame(f.Build(t));
        }

        /// <summary>Fudo dachi, arms in the builder's default kamae (elbow ≈ 90°).</summary>
        private static StanceTestFrames Ready(bool mirrored = false) =>
            StanceTestFrames.Fudo(mirrored);

        /// <summary>Zenkutsu dachi with both arms back in that same kamae — the recovery frame.</summary>
        private static StanceTestFrames Recover(bool mirrored = false) =>
            StanceTestFrames.Zenkutsu(mirrored);

        /// <summary>
        /// The textbook strike: stepped out into zenkutsu, lead arm straight, wrist level
        /// with the nose (the builder puts the nose 0.7 shanks above the shoulders).
        /// </summary>
        private static StanceTestFrames Strike(bool mirrored = false)
        {
            var f = StanceTestFrames.Zenkutsu(mirrored);
            PutLeadArm(f, elbowForward: 0.75f, elbowUp: 0.35f, wristForward: 1.5f, wristUp: 0.7f);
            return f;
        }

        private static void PutLeadArm(StanceTestFrames f,
            float elbowForward, float elbowUp, float wristForward, float wristUp)
        {
            f.LeadElbowForwardShanks = elbowForward;
            f.LeadElbowUpShanks = elbowUp;
            f.LeadWristForwardShanks = wristForward;
            f.LeadWristUpShanks = wristUp;
        }

        /// <summary>
        /// One whole attempt: 0.75 s of the ready stance, two strike frames, one recovery
        /// frame (which is where the rep is judged). Returns the next free timestamp.
        /// </summary>
        private static double Rep(KizamiZukiAnalyzer a, StanceTestFrames ready,
            StanceTestFrames strike, StanceTestFrames recover, double t0)
        {
            Feed(a, ready, t0, t0 + 0.75);
            Feed(a, strike, t0 + 1.0, t0 + 1.25);
            a.ProcessFrame(recover.Build(t0 + 1.5));
            return t0 + 1.75;
        }

        private static double Rep(KizamiZukiAnalyzer a, StanceTestFrames strike, double t0 = 0.0) =>
            Rep(a, Ready(), strike, Recover(), t0);

        [Test]
        public void CleanPunchCountsOneRep()
        {
            var a = new KizamiZukiAnalyzer();

            Rep(a, Strike());

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(string.Empty, a.Cue);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void MirroredFighterScoresTheSame()
        {
            var a = new KizamiZukiAnalyzer();

            Rep(a, Ready(mirrored: true), Strike(mirrored: true), Recover(mirrored: true), 0.0);

            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(string.Empty, a.Cue);
        }

        [Test]
        public void TwoPunchesInARowCountTwice()
        {
            var a = new KizamiZukiAnalyzer();

            double next = Rep(a, Strike());
            Rep(a, Strike(), next);

            Assert.AreEqual(2, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void RearHandPunchIsNotKizamiZuki()
        {
            var f = StanceTestFrames.Zenkutsu();
            f.RearElbowForwardShanks = 0.75f;               // вперёд ушла дальняя рука,
            f.RearElbowUpShanks = 0.35f;                    // ведущая осталась в камае
            f.RearWristForwardShanks = 1.5f;
            f.RearWristUpShanks = 0.7f;

            var a = new KizamiZukiAnalyzer();
            Rep(a, f);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Ведущей рукой", a.Cue);
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);
        }

        [Test]
        public void BentArmIsNoRep()
        {
            var f = StanceTestFrames.Zenkutsu();
            // Плечо-локоть и локоть-запястье по 0.55 голени под 45° друг к другу:
            // рука вынесена и поднята в голову, но локоть встал на 135°.
            PutLeadArm(f, elbowForward: 0.275f, elbowUp: 0.476f,
                wristForward: 0.806f, wristUp: 0.618f);

            var a = new KizamiZukiAnalyzer();
            Rep(a, f);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выпрями руку", a.Cue);
        }

        [Test]
        public void ChudanHeightIsNoRep()
        {
            var f = StanceTestFrames.Zenkutsu();
            PutLeadArm(f, elbowForward: 0.75f, elbowUp: 0.1f,
                wristForward: 1.5f, wristUp: 0.2f);         // прямая рука, но в грудь

            var a = new KizamiZukiAnalyzer();
            Rep(a, f);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выше, в голову", a.Cue);
        }

        [Test]
        public void PeakOutsideZenkutsuIsNoRep()
        {
            var f = StanceTestFrames.Fudo();                // шага в zenkutsu так и не было
            PutLeadArm(f, elbowForward: 0.75f, elbowUp: 0.35f, wristForward: 1.5f, wristUp: 0.7f);

            var a = new KizamiZukiAnalyzer();
            Rep(a, Ready(), f, Ready(), 0.0);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Дойди до стойки", a.Cue);
        }

        [Test]
        public void ArmLeftHangingIsNoRep()
        {
            var back = StanceTestFrames.Zenkutsu();
            // Руку убрали из-под удара, но не собрали: опущена вниз и всё ещё прямая.
            PutLeadArm(back, elbowForward: 0.05f, elbowUp: -0.55f,
                wristForward: 0.1f, wristUp: -1.1f);

            var a = new KizamiZukiAnalyzer();
            Rep(a, Ready(), Strike(), back, 0.0);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Верни руку", a.Cue);
        }

        [Test]
        public void PunchWithoutTheFudoGateIsNotJudged()
        {
            var a = new KizamiZukiAnalyzer();

            Feed(a, Ready(), 0.0, 0.25);                    // 0.25 c — готовность не набрана
            Feed(a, Strike(), 0.5, 0.75);
            a.ProcessFrame(Recover().Build(1.0));

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);                   // удара как бы и не было
        }

        [Test]
        public void FaultyFudoIsNotAReadyStanceEither()
        {
            var narrow = StanceTestFrames.Fudo();
            narrow.Length01 = 0.7f;

            var a = new KizamiZukiAnalyzer();
            Rep(a, narrow, Strike(), Recover(), 0.0);

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void SetupCueFixesTheStanceLive()
        {
            var narrow = StanceTestFrames.Fudo();
            narrow.Length01 = 0.7f;

            var a = new KizamiZukiAnalyzer();
            a.ProcessFrame(narrow.Build(0.0));

            Assert.AreEqual("Шире", a.Cue);                 // фраза приходит из StanceReader
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);

            a.ProcessFrame(Ready().Build(0.25));
            Assert.AreEqual(string.Empty, a.Cue);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void VerdictOutlivesTheStanceCueLongEnoughToBeHeard()
        {
            var a = new KizamiZukiAnalyzer();
            Rep(a, Strike());                               // судится в t = 1.5

            a.ProcessFrame(Recover().Build(2.0));
            Assert.AreEqual(string.Empty, a.Cue);           // чистый повтор молчит

            a.ProcessFrame(Recover().Build(4.0));           // окно вердикта вышло —
            Assert.AreEqual("Уже", a.Cue);                  // снова живая правка стойки
        }

        [Test]
        public void VerdictIsNotDrownedByTheStanceCue()
        {
            var f = StanceTestFrames.Zenkutsu();
            PutLeadArm(f, elbowForward: 0.75f, elbowUp: 0.1f, wristForward: 1.5f, wristUp: 0.2f);

            var a = new KizamiZukiAnalyzer();
            Rep(a, f);                                      // chudan -> "Выше, в голову"

            // Боец стоит в zenkutsu, то есть fudo нарушена — но разбор удара важнее.
            a.ProcessFrame(Recover().Build(2.0));
            Assert.AreEqual("Выше, в голову", a.Cue);
        }

        [Test]
        public void UnreadableFrameAsksForFraming()
        {
            var hidden = StanceTestFrames.Fudo();
            hidden.FootVisibility = 0.1f;

            var a = new KizamiZukiAnalyzer();
            a.ProcessFrame(hidden.Build(0.0));

            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (боком)", a.Cue);
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void IdentityMatchesTheCatalogContract()
        {
            var a = new KizamiZukiAnalyzer();

            Assert.AreEqual("kizamizuki-jodan", a.Id);
            Assert.AreEqual("Kizami zuki jodan", a.DisplayName);
        }

        [Test]
        public void ResetClearsTheSet()
        {
            var a = new KizamiZukiAnalyzer();
            Rep(a, Strike());

            a.Reset();

            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (боком)", a.Cue);
        }
    }
}
