using System;
using System.Collections.Generic;

namespace Mikey.Backend
{
    /// <summary>
    /// Форма запроса и ответа <c>sync_progress</c>. Имена полей — snake_case
    /// намеренно: JsonUtility сопоставляет поля по имени буквально, а на той
    /// стороне это имена колонок Postgres. Отступление от C#-стиля локализовано
    /// в этом файле и нигде больше не расползается.
    /// </summary>
    [Serializable]
    public sealed class SyncProfile
    {
        public string display_name = string.Empty;
        public string gender = string.Empty;
        public int age;
        public float weight_kg;
        public int height_cm;
        public int tutorial_progress;
        public string profile_updated_at = string.Empty;
    }

    [Serializable]
    public sealed class SyncLevel0
    {
        public int pushup_reps;
        public int squat_reps;
        public int yokogeri_slow_reps;
        public int yokogeri_best_zone;
        public float wallsit_seconds;
        public float yokogeri_hold_seconds;
    }

    [Serializable]
    public sealed class SyncTechnique
    {
        public string technique_id = string.Empty;
        public int clean_reps;
    }

    [Serializable]
    public sealed class SyncState
    {
        public SyncProfile profile = new SyncProfile();
        public SyncLevel0 level0 = new SyncLevel0();
        public List<SyncTechnique> level1 = new List<SyncTechnique>();
    }
}
