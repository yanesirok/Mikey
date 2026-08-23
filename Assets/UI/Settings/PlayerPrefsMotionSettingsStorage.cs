using UnityEngine;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Production <see cref="IMotionSettingsStorage"/> backed by <see cref="PlayerPrefs"/>.
    /// Mirrors <see cref="Mikey.UI.Audio.PlayerPrefsAudioSettingsStorage"/>. PlayerPrefs has
    /// no native bool, so the value is stored as an int (0/1) — the same on-disk
    /// representation the old direct-write code used, so an already-saved value
    /// is not lost by this refactor.
    /// </summary>
    public sealed class PlayerPrefsMotionSettingsStorage : IMotionSettingsStorage
    {
        public bool TryLoad(string key, out bool value)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                value = false;
                return false;
            }

            value = PlayerPrefs.GetInt(key) != 0;
            return true;
        }

        public void Save(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
