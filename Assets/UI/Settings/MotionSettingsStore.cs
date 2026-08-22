using System;
using UnityEngine;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Хранение <see cref="IMotionSettings"/> в PlayerPrefs. Живёт на том же
    /// GameObject, что и SettingsModalController, который достаёт её через
    /// GetComponent — ровно как уже сделано для громкостей.
    /// </summary>
    public sealed class MotionSettingsStore : MonoBehaviour, IMotionSettings
    {
        public const string ReducedMotionKey = "Mikey.Settings.ReducedMotion";

        private bool _reducedMotion;
        private bool _loaded;

        public event Action Changed;

        public bool ReducedMotion
        {
            get
            {
                EnsureLoaded();
                return _reducedMotion;
            }
            set
            {
                EnsureLoaded();
                if (_reducedMotion == value)
                    return;
                _reducedMotion = value;
                PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        private void Awake() => EnsureLoaded();

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            _reducedMotion = PlayerPrefs.GetInt(ReducedMotionKey, 0) != 0;
        }
    }
}
