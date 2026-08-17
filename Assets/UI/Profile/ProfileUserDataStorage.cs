using UnityEngine;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Persists the whole <see cref="ProfileUserData"/> as one JSON blob under
    /// one PlayerPrefs key — the new primary Profile storage, replacing the old
    /// username-only <see cref="ProfileDisplayNameStorage"/>. That class is
    /// deliberately left in place (not deleted, still written by nothing) purely
    /// as a migration source: the first <see cref="Load"/> to find no
    /// "Mikey.Profile.UserData" key yet recovers whatever name was already saved
    /// under the old key instead of silently resetting it to "Mikey" — see
    /// <see cref="MigrateFromDisplayNameOnly"/>. No backend/account system exists
    /// yet; <c>JsonUtility</c> is a small, dependency-free serializer appropriate
    /// for this local-only data.
    /// </summary>
    public static class ProfileUserDataStorage
    {
        public const string PlayerPrefsKey = "Mikey.Profile.UserData";

        public static ProfileUserData Load()
        {
            if (PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                var loaded = JsonUtility.FromJson<ProfileUserData>(PlayerPrefs.GetString(PlayerPrefsKey));
                if (loaded != null)
                    return loaded;
            }

            return MigrateFromDisplayNameOnly();
        }

        public static void Save(ProfileUserData data) => PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(data));

        /// <summary>
        /// Complete = every field future capability calculations will need is
        /// present and valid: Display Name, Gender, Age, Weight, Height. Deliberately
        /// does not compute or require anything beyond "was this filled in correctly".
        /// </summary>
        public static bool IsComplete(ProfileUserData data) =>
            data != null
            && ProfileDisplayNameStorage.Validate(data.DisplayName) != null
            && ProfileUserDataValidation.IsValidGender(data.Gender)
            && ProfileUserDataValidation.IsValidAge(data.Age)
            && ProfileUserDataValidation.IsValidWeightKg(data.WeightKg)
            && ProfileUserDataValidation.IsValidHeightCm(data.HeightCm);

        /// <summary>An existing display name saved under the old username-only key must not be lost.</summary>
        private static ProfileUserData MigrateFromDisplayNameOnly()
        {
            var migrated = new ProfileUserData();
            if (PlayerPrefs.HasKey(ProfileDisplayNameStorage.PlayerPrefsKey))
                migrated.DisplayName = ProfileDisplayNameStorage.Load();
            return migrated;
        }
    }
}
