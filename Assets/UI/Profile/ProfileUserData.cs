using System;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Local frontend-only profile data — no backend/account system exists yet
    /// (see <see cref="ProfileUserDataStorage"/>). Plain public fields, not
    /// properties: <c>UnityEngine.JsonUtility</c> only serializes fields. Kept
    /// free of any UI Toolkit dependency so later systems (e.g. capability
    /// calculations, explicitly NOT built in this pass) can read
    /// age/gender/weightKg/heightCm directly instead of parsing UXML text.
    /// </summary>
    [Serializable]
    public sealed class ProfileUserData
    {
        public const string GenderMale = "Male";
        public const string GenderFemale = "Female";
        public const string GenderOther = "Other";
        public const string GenderPreferNotToSay = "Prefer not to say";

        public string DisplayName = ProfileDisplayNameStorage.DefaultDisplayName;
        public string Gender = string.Empty;
        public int Age;
        public float WeightKg;
        public int HeightCm;

        /// <summary>
        /// Время последней правки, ISO-8601 UTC. Проставляется в
        /// <see cref="ProfileUserDataStorage.Save"/> и служит арбитром при
        /// синхронизации: профиль — это правки, а не рекорды, поэтому побеждает
        /// более свежая версия, а не большее число. Пусто у сохранений,
        /// сделанных до появления синхронизации.
        /// </summary>
        public string UpdatedAtIso = string.Empty;
    }
}
