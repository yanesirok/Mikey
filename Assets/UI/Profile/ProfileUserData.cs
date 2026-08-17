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
    }
}
