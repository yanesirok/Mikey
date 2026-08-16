using UnityEngine;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Local-only display name storage for Profile's editable username. There is
    /// no login/account/backend system in this app — this is one explicitly
    /// approved PlayerPrefs key as a placeholder until a real user/profile store
    /// exists, at which point this class is the single thing to replace.
    /// <see cref="Validate"/> is kept pure (no PlayerPrefs) so it's directly
    /// unit-testable independent of the storage mechanism.
    /// </summary>
    public static class ProfileDisplayNameStorage
    {
        public const string PlayerPrefsKey = "Mikey.Profile.DisplayName";
        public const string DefaultDisplayName = "Mikey";
        public const int MaxLength = 24;

        public static string Load() => PlayerPrefs.GetString(PlayerPrefsKey, DefaultDisplayName);

        public static void Save(string displayName) => PlayerPrefs.SetString(PlayerPrefsKey, displayName);

        /// <summary>
        /// Trims whitespace and enforces <see cref="MaxLength"/>. Returns null for an
        /// empty/whitespace-only result — callers must not save when this returns null.
        /// </summary>
        public static string Validate(string rawInput)
        {
            if (rawInput == null)
                return null;

            string trimmed = rawInput.Trim();
            if (trimmed.Length == 0)
                return null;

            return trimmed.Length > MaxLength ? trimmed.Substring(0, MaxLength) : trimmed;
        }
    }
}
