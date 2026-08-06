using System;

namespace Mikey.Pose
{
    /// <summary>Player stats derived from the level-0 assessment, each 0–100.</summary>
    public readonly struct PlayerStats
    {
        public readonly int Strength;
        public readonly int Endurance;
        public readonly int Flexibility;
        public readonly int Balance;

        public PlayerStats(int strength, int endurance, int flexibility, int balance)
        {
            Strength = strength;
            Endurance = endurance;
            Flexibility = flexibility;
            Balance = balance;
        }
    }

    /// <summary>
    /// Maps raw level-0 results to the four stats. All formulas are linear ramps to a
    /// named anchor ("30 push-ups = 100") clamped at 100 — deliberately simple until
    /// real-player data justifies anything fancier; tune the anchors, not the shape.
    /// </summary>
    public static class StatCalculator
    {
        private const float PushUpsFor100 = 30f;
        private const float SquatsFor100 = 40f;
        private const float WallSitSecondsFor100 = 120f;
        private const float SlowRepsFor70 = 10f;
        private const float HoldSecondsFor30 = 20f;
        private static readonly int[] FlexibilityByZone = { 0, 33, 66, 100 };

        public static PlayerStats Compute(Level0Results r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            float strength = (Ramp(r.PushUpReps, PushUpsFor100) + Ramp(r.SquatReps, SquatsFor100)) / 2f * 100f;
            float endurance = Ramp(r.WallSitSeconds, WallSitSecondsFor100) * 100f;
            // Гибкость — среднее переднего (mae) и бокового (yoko) удара: это разные
            // растяжки, один вид удара не даёт 100.
            float flexibility = (FlexibilityByZone[ClampZone(r.MaeGeriBestZone)]
                               + FlexibilityByZone[ClampZone(r.YokoGeriBestZone)]) / 2f;
            float balance = Ramp(r.YokoGeriSlowReps, SlowRepsFor70) * 70f
                          + Ramp(r.YokoGeriHoldSeconds, HoldSecondsFor30) * 30f;

            return new PlayerStats(
                (int)Math.Round(strength),
                (int)Math.Round(endurance),
                (int)Math.Round(flexibility),
                (int)Math.Round(balance));
        }

        private static int ClampZone(int zone) =>
            Math.Min(Math.Max(zone, 0), FlexibilityByZone.Length - 1);

        private static float Ramp(float value, float anchor) =>
            value <= 0f ? 0f : Math.Min(value / anchor, 1f);
    }
}
