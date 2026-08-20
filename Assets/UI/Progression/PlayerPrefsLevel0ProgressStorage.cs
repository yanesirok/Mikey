using UnityEngine;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Production <see cref="ILevel0ProgressStorage"/> backed by
    /// <see cref="PlayerPrefs"/> — mirrors <see cref="PlayerPrefsTutorialProgressStorage"/>
    /// exactly, its own separate key so Level 0 test completion and the legacy
    /// linear tutorial state never collide or migrate into each other.
    /// </summary>
    public sealed class PlayerPrefsLevel0ProgressStorage : ILevel0ProgressStorage
    {
        private const string Key = "Mikey.Level0Progress.CompletedTests";

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
