using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mikey.Pose
{
    /// <summary>
    /// Level-1 teaching progress: clean reps per technique id, max-merged so a weaker
    /// retry never downgrades what was already earned. Persisted as JSON in PlayerPrefs
    /// under its own key — deliberately separate from <c>level0.results</c>, because
    /// training must not touch the assessment. Together with <see cref="Level0Results"/>
    /// the only Pose classes that touch UnityEngine (PlayerPrefs/JsonUtility).
    /// </summary>
    [Serializable]
    public sealed class Level1Progress
    {
        private const string PrefsKey = "level1.progress";

        /// <summary>Clean reps that mark a technique as learned.</summary>
        public const int Goal = 5;

        /// <summary>One technique's score. JsonUtility has no Dictionary, so pairs it is.</summary>
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public int CleanReps;
        }

        public List<Entry> Entries = new List<Entry>();

        /// <summary>Max-merges one finished set into the progress. Junk input is ignored.</summary>
        public void Absorb(string techniqueId, int cleanReps)
        {
            if (string.IsNullOrEmpty(techniqueId))
                return;

            if (cleanReps < 0)
                cleanReps = 0;

            Entry entry = Find(techniqueId);
            if (entry == null)
                Entries.Add(new Entry { Id = techniqueId, CleanReps = cleanReps });
            else
                entry.CleanReps = Math.Max(entry.CleanReps, cleanReps);
        }

        /// <summary>Best clean reps recorded for the technique, 0 when it was never trained.</summary>
        public int RepsFor(string techniqueId)
        {
            Entry entry = Find(techniqueId);
            return entry?.CleanReps ?? 0;
        }

        public static Level1Progress Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new Level1Progress();
            try
            {
                Level1Progress p = JsonUtility.FromJson<Level1Progress>(json);
                if (p == null)
                    return new Level1Progress();
                // JsonUtility может вернуть null-список (например, из "{}") — дальше по нему ходят.
                if (p.Entries == null)
                    p.Entries = new List<Entry>();
                return p;
            }
            catch (ArgumentException)
            {
                // Corrupt PlayerPrefs entry (e.g. non-JSON) — start fresh rather than crash Awake.
                return new Level1Progress();
            }
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        private Entry Find(string techniqueId)
        {
            if (string.IsNullOrEmpty(techniqueId) || Entries == null)
                return null;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Entries[i]?.Id, techniqueId, StringComparison.Ordinal))
                    return Entries[i];
            }
            return null;
        }
    }
}
