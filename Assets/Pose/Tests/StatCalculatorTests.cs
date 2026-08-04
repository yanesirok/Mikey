using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class StatCalculatorTests
    {
        [Test]
        public void EmptyResultsGiveZeroStats()
        {
            PlayerStats s = StatCalculator.Compute(new Level0Results());
            Assert.AreEqual(0, s.Strength);
            Assert.AreEqual(0, s.Endurance);
            Assert.AreEqual(0, s.Flexibility);
            Assert.AreEqual(0, s.Balance);
        }

        [Test]
        public void AnchorsGiveExactly100()
        {
            var r = new Level0Results
            {
                PushUpReps = 30,
                SquatReps = 40,
                WallSitSeconds = 120f,
                MaeGeriBestZone = (int)KickZone.Jodan,
                YokoGeriSlowReps = 10,
                YokoGeriHoldSeconds = 20f,
            };
            PlayerStats s = StatCalculator.Compute(r);
            Assert.AreEqual(100, s.Strength);
            Assert.AreEqual(100, s.Endurance);
            Assert.AreEqual(100, s.Flexibility);
            Assert.AreEqual(100, s.Balance);
        }

        [Test]
        public void AboveAnchorClampsTo100()
        {
            var r = new Level0Results { PushUpReps = 90, SquatReps = 200, WallSitSeconds = 999f };
            PlayerStats s = StatCalculator.Compute(r);
            Assert.AreEqual(100, s.Strength);
            Assert.AreEqual(100, s.Endurance);
        }

        [Test]
        public void MidpointsScaleLinearly()
        {
            var r = new Level0Results
            {
                PushUpReps = 15,               // 50 из пуш-апов
                SquatReps = 10,                // 25 из приседаний
                WallSitSeconds = 60f,
                MaeGeriBestZone = (int)KickZone.Chudan,
                YokoGeriSlowReps = 5,          // 35 из повторов
                YokoGeriHoldSeconds = 10f,     // 15 из удержания
            };
            PlayerStats s = StatCalculator.Compute(r);
            Assert.AreEqual(38, s.Strength);   // (0.5 + 0.25) / 2 * 100 = 37.5 → 38
            Assert.AreEqual(50, s.Endurance);
            Assert.AreEqual(66, s.Flexibility);
            Assert.AreEqual(50, s.Balance);
        }

        [Test]
        public void AbsorbKeepsBestOfEachExercise()
        {
            var r = new Level0Results { SquatReps = 12 };

            var squat = new SquatAnalyzer(smoothingAlpha: 1f);
            // 1 повтор — меньше сохранённых 12: результат не ухудшается.
            squat.ProcessFrame(LegTestFrames.Squat(175f, timestamp: 0.0));
            squat.ProcessFrame(LegTestFrames.Squat(95f, timestamp: 1.0));
            squat.ProcessFrame(LegTestFrames.Squat(175f, timestamp: 2.0));
            r.Absorb(squat);
            Assert.AreEqual(12, r.SquatReps);

            var wallsit = new WallSitAnalyzer();
            for (double t = 0; t <= 42.0 + 1e-9; t += 0.5)     // кадры чаще грейса HoldTimer
                wallsit.ProcessFrame(LegTestFrames.WallSit(timestamp: t));
            r.Absorb(wallsit);
            Assert.AreEqual(42f, r.WallSitSeconds, 1e-3f);

            var mg = new MaeGeriAnalyzer(KickZone.Gedan, smoothingAlpha: 1f);
            mg.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 0.0));
            mg.ProcessFrame(LegTestFrames.Kick(0.18f, timestamp: 0.5));    // jodan
            mg.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 1.0));
            r.Absorb(mg);
            Assert.AreEqual((int)KickZone.Jodan, r.MaeGeriBestZone);
        }

        [Test]
        public void SaveLoadRoundTripsThroughPlayerPrefs()
        {
            var r = new Level0Results { PushUpReps = 7, WallSitSeconds = 33.5f };
            r.Save();
            Level0Results loaded = Level0Results.Load();
            Assert.AreEqual(7, loaded.PushUpReps);
            Assert.AreEqual(33.5f, loaded.WallSitSeconds, 1e-3f);
        }
    }
}
