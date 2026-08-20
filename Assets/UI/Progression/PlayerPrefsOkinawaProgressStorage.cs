using UnityEngine;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Production <see cref="IOkinawaProgressStorage"/> backed by
    /// <see cref="PlayerPrefs"/> — mirrors <see cref="PlayerPrefsTutorialProgressStorage"/>
    /// exactly, its own separate key so Okinawa mission completion never collides
    /// with the legacy linear tutorial state or Level 0's own storage.
    /// </summary>
    public sealed class PlayerPrefsOkinawaProgressStorage : IOkinawaProgressStorage
    {
        private const string Key = "Mikey.OkinawaProgress.CompletedLevels";

        public bool TryLoad(out string value)
        {
            if (!PlayerPrefs.HasKey(Key))
            {
                value = null;
                return false;
            }

            value = PlayerPrefs.GetString(Key);
            return true;
        }

        public void Save(string value)
        {
            PlayerPrefs.SetString(Key, value);
            PlayerPrefs.Save();
        }

        public void Delete()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
