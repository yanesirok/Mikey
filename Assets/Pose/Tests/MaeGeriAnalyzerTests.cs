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
        public void InvisibleShoulderReportsNotVisible()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            a.ProcessFrame(LegTestFrames.Kick(Floor, timestamp: 0.0, shoulderVisibility: 0.1f));
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
        }

        // ---------------------------------------------------------------------------
        // Строгий профиль — техника уровня 1 «mae geri chudan из стойки».
        // Кадры собираются из StanceTestFrames: удар переставляет ТОЛЬКО бьющую ногу,
        // остальная фигура (опорная нога со стопой, торс) остаётся стойкой, поэтому
        // кадр приземления читается стойкой сам собой.
        // ---------------------------------------------------------------------------

        /// <summary>Чамбер поднимаем на 1.2 голени опорной ноги — выше порога <see cref="LegLiftCycle"/>
        /// (1.0), иначе замах пройдёт мимо цикла и «раньше выпрямления» проверять будет нечем.</summary>
        private const float ChamberLift = 1.2f;

        private static MaeGeriAnalyzer Strict()
            => new MaeGeriAnalyzer(KickZone.Chudan, smoothingAlpha: 1f, profile: ScoringProfile.Strict);

        /// <summary>Стойка между окнами фудо и зенкуцу: ни одна не читается чисто.</summary>
        private static StanceTestFrames NoStance()
        {
            StanceTestFrames s = StanceTestFrames.Zenkutsu();
            s.Length01 = 1.7f;
            return s;
        }

        /// <summary>Замах: колено вынесено вперёд-вверх, голень висит вниз (угол ≈55°).</summary>
        private static PoseFrame Chamber(StanceTestFrames stance, double t)
        {
            PoseFrame f = stance.Build(t);
            Geometry(stance, f, out bool kickLeft, out float fwd, out PoseLandmark hip, out _,
                out float supportAnkleY, out float supportShankY);
            float ankleY = supportAnkleY - ChamberLift * supportShankY;
            float ankleX = hip.X + fwd * 0.75f * stance.Shank;
            return With(f, t, kickLeft, ankleX, ankleY, ankleX, ankleY - stance.Shank);
        }

        /// <summary>Выпрямленная нога в заданной зоне: высота берётся от плеча и таза
        /// ЭТОГО кадра — ровно от тех якорей, по которым зону читает анализатор.</summary>
        private static PoseFrame Extend(StanceTestFrames stance, KickZone zone, double t)
        {
            PoseFrame f = stance.Build(t);
            Geometry(stance, f, out bool kickLeft, out float fwd, out PoseLandmark hip,
                out PoseLandmark shoulder, out _, out _);
            float ankleY =
                zone == KickZone.Jodan ? shoulder.Y - 0.2f * stance.Shank
                : zone == KickZone.Chudan ? 0.5f * (shoulder.Y + hip.Y)
                : hip.Y + 0.3f * stance.Shank;
            float ankleX = hip.X + fwd * 2.0f * stance.Shank;
            return With(f, t, kickLeft, ankleX, ankleY, 0.5f * (hip.X + ankleX), 0.5f * (hip.Y + ankleY));
        }

        // Бьёт задняя нога; опорная — передняя, её голень и есть единица подъёма.
        private static void Geometry(StanceTestFrames stance, PoseFrame f, out bool kickLeft, out float fwd,
            out PoseLandmark hip, out PoseLandmark shoulder, out float supportAnkleY, out float supportShankY)
        {
            kickLeft = !stance.FrontIsLeft;
            fwd = stance.ForwardSign >= 0f ? 1f : -1f;
            hip = f.Get(kickLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            shoulder = f.Get(kickLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            supportAnkleY = f.Get(kickLeft ? PoseLandmarkType.RightAnkle : PoseLandmarkType.LeftAnkle).Y;
            supportShankY = supportAnkleY - f.Get(kickLeft ? PoseLandmarkType.RightKnee : PoseLandmarkType.LeftKnee).Y;
        }

        private static PoseFrame With(PoseFrame f, double t, bool kickLeft,
            float ankleX, float ankleY, float kneeX, float kneeY)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = f.Landmark(i);
            float vis = f.Get(PoseLandmarkType.LeftHip).Visibility;
            lm[(int)(kickLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle)] =
                new PoseLandmark(ankleX, ankleY, 0f, vis);
            lm[(int)(kickLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee)] =
                new PoseLandmark(kneeX, kneeY, 0f, vis);
            return new PoseFrame(lm, t);
        }

        /// <summary>Стойка → замах → выпрямление в зону → замах → приземление.
        /// Возвращает время следующего кадра.</summary>
        private static double Technique(MaeGeriAnalyzer a, StanceTestFrames start, KickZone zone,
            StanceTestFrames landing = null, bool chamber = true)
        {
            double t = 0;
            a.ProcessFrame(start.Build(t)); t += 0.2;
            if (chamber) { a.ProcessFrame(Chamber(start, t)); t += 0.2; }
            a.ProcessFrame(Extend(start, zone, t)); t += 0.2;
            if (chamber) { a.ProcessFrame(Chamber(start, t)); t += 0.2; }
            a.ProcessFrame((landing ?? start).Build(t)); t += 0.2;
            return t;
        }

        [Test]
        public void Strict_KickFromStance_Counts()
        {
            var a = Strict();
            Technique(a, StanceTestFrames.Zenkutsu(), KickZone.Chudan);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(string.Empty, a.Cue);
        }

        [Test]
        public void Strict_KickFromFudo_AlsoCounts()
        {
            var a = Strict();
            Technique(a, StanceTestFrames.Fudo(), KickZone.Chudan);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void Strict_MirroredStance_ReadsTheSame()
        {
            var a = Strict();
            Technique(a, StanceTestFrames.Zenkutsu(mirrored: true), KickZone.Chudan);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void Strict_SameKickWithoutStance_IsNotJudgedButCoachesTheStance()
        {
            var a = Strict();
            StanceTestFrames bad = NoStance();
            double t = Technique(a, bad, KickZone.Chudan);
            Assert.AreEqual(0, a.Reps, "не из стойки — не техника");
            Assert.AreEqual(0, a.NoReps, "и не ошибка удара: судить нечего");

            a.ProcessFrame(bad.Build(t));
            Assert.AreEqual("Уже", a.Cue, "живой cue правит стойку, а не удар");
        }

        [Test]
        public void Strict_StaleStance_DoesNotGateTheKick()
        {
            // Стойка была секунду назад и развалилась: гейт помнит её недолго, иначе
            // «когда-то стоял правильно» открывало бы зачёт на весь подход.
            var a = Strict();
            StanceTestFrames good = StanceTestFrames.Zenkutsu(), bad = NoStance();
            a.ProcessFrame(good.Build(0.0));
            for (double t = 0.2; t <= 1.0 + 1e-9; t += 0.2)
                a.ProcessFrame(bad.Build(t));
            a.ProcessFrame(Chamber(bad, 1.2));
            a.ProcessFrame(Extend(bad, KickZone.Chudan, 1.4));
            a.ProcessFrame(Chamber(bad, 1.6));
            a.ProcessFrame(good.Build(1.8));               // вернулся в стойку — но входил не из неё
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void Strict_AboveChudan_IsNoRep()
        {
            var a = Strict();
            Technique(a, StanceTestFrames.Zenkutsu(), KickZone.Jodan);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Ниже", a.Cue);
        }

        [Test]
        public void Strict_BelowChudan_IsNoRep()
        {
            var a = Strict();
            Technique(a, StanceTestFrames.Zenkutsu(), KickZone.Gedan);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выше", a.Cue);
        }

        [Test]
        public void Strict_NoChamber_IsNoRep()
        {
            var a = Strict();
            Technique(a, StanceTestFrames.Zenkutsu(), KickZone.Chudan, chamber: false);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Сначала колено", a.Cue);
        }

        [Test]
        public void Strict_ChamberAfterExtension_IsNoRep()
        {
            // Мах прямой ногой, согнувший колено только на спуске: чамбер в цикле есть,
            // но высота взята до него — техника не собрана.
            var a = Strict();
            StanceTestFrames s = StanceTestFrames.Zenkutsu();
            a.ProcessFrame(s.Build(0.0));
            a.ProcessFrame(Extend(s, KickZone.Chudan, 0.2));
            a.ProcessFrame(Chamber(s, 0.4));
            a.ProcessFrame(s.Build(0.6));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Сначала колено", a.Cue);
        }

        [Test]
        public void Strict_NoReturnToStance_IsNoRep()
        {
            var a = Strict();
            StanceTestFrames bad = NoStance();
            double t = Technique(a, StanceTestFrames.Zenkutsu(), KickZone.Chudan, landing: bad);
            Assert.AreEqual(0, a.Reps, "повтор ждёт возврата в стойку");

            // Окно возврата — 1 с после приземления; выходим на кадре вердикта, дальше
            // живой cue уже правит стойку и «Вернись в стойку» законно сменяется.
            for (; t <= 3.0 && a.NoReps == 0; t += 0.2)
                a.ProcessFrame(bad.Build(t));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Вернись в стойку", a.Cue);
        }

        [Test]
        public void Strict_UnreadableStance_IsNotVisibleNotAFault()
        {
            var a = Strict();
            a.ProcessFrame(LegTestFrames.Kick(Floor, timestamp: 0.0));   // стопы не размечены
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (боком)", a.Cue);
        }

        [Test]
        public void Strict_IdNamesTheTechnique()
        {
            Assert.AreEqual("maegeri-chudan-stance", Strict().Id);
            Assert.AreEqual("maegeri-chudan", NewAnalyzer(KickZone.Chudan).Id);
        }
    }
}
