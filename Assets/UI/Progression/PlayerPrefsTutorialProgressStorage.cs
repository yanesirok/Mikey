using UnityEngine;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Production <see cref="ITutorialProgressStorage"/> backed by
    /// <see cref="PlayerPrefs"/> — the smallest Unity-local mechanism appropriate
    /// for a single small enum value: survives app/Editor restart, needs no
    /// backend or auth, and stores nothing payment-related or sensitive (just the
    /// name of a tutorial-progress state).
    /// </summary>
    public sealed class PlayerPrefsTutorialProgressStorage : ITutorialProgressStorage
    {
        private const string Key = "Mikey.TutorialProgress.State";

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
