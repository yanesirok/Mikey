using System;
using UnityEngine;

namespace Mikey.Pose
{
    /// <summary>
    /// Raw level-0 assessment results, one field per exercise outcome. Absorb() max-merges
    /// a finished set into the stored results — the assessment reflects the player's best
    /// effort, so a weaker retry never downgrades it. Persisted as JSON in PlayerPrefs;
    /// the future game profile reads the same store. The only Pose class that touches
    /// UnityEngine (PlayerPrefs/JsonUtility).
    /// </summary>
    [Serializable]
    public sealed class Level0Results
    {
        private const string PrefsKey = "level0.results";

        public int PushUpReps;
        public int SquatReps;
        public int YokoGeriSlowReps;       // имя для совместимости сейвов: лучший сет повторов любого варианта yoko
        public int YokoGeriBestZone;       // (int)KickZone, лучшая зона yoko geri
        public float WallSitSeconds;
        public float YokoGeriHoldSeconds;

        /// <summary>Max-merges one finished set into these results.</summary>
        public void Absorb(IExerciseAnalyzer analyzer)
        {
            switch (analyzer)
            {
                case PushUpAnalyzer p:
                    PushUpReps = Math.Max(PushUpReps, p.Reps);
                    break;
                case SquatAnalyzer s:
                    SquatReps = Math.Max(SquatReps, s.Reps);
                    break;
                case WallSitAnalyzer w:
                    WallSitSeconds = Math.Max(WallSitSeconds, (float)w.BestHoldSeconds);
                    break;
                case YokoGeriAnalyzer y:
                    YokoGeriSlowReps = Math.Max(YokoGeriSlowReps, y.Reps);
                    YokoGeriHoldSeconds = Math.Max(YokoGeriHoldSeconds, (float)y.TotalLiftedSeconds);
                    YokoGeriBestZone = Math.Max(YokoGeriBestZone, (int)y.BestZone);
                    break;
                // Mae geri сюда не попадает: с уровня 1 он техника обучения, а не
                // мерка оценки — его прогресс живёт в Level1Progress.
            }
        }

        public static Level0Results Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new Level0Results();
            try
            {
                Level0Results r = JsonUtility.FromJson<Level0Results>(json);
                return r ?? new Level0Results();
            }
            catch (ArgumentException)
            {
                // Corrupt PlayerPrefs entry (e.g. non-JSON) — start fresh rather than crash Awake.
                return new Level0Results();
            }
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
