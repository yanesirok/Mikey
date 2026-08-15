using UnityEngine;

namespace Mikey.UI.Audio
{
    /// <summary>
    /// Production <see cref="IAudioSettingsStorage"/> backed by <see cref="PlayerPrefs"/>
    /// — the smallest Unity-local mechanism appropriate for three small float values:
    /// survives app/Editor restart, needs no backend or auth. Mirrors
    /// <see cref="Mikey.UI.Progression.PlayerPrefsTutorialProgressStorage"/>.
    /// </summary>
    public sealed class PlayerPrefsAudioSettingsStorage : IAudioSettingsStorage
    {
        public bool TryLoad(string key, out float value)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                value = 0f;
                return false;
            }

            value = PlayerPrefs.GetFloat(key);
            return true;
        }

        public void Save(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }
    }
}
