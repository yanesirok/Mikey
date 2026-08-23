using System;
using UnityEngine;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Хранение <see cref="IMotionSettings"/> в PlayerPrefs. Живёт на том же
    /// GameObject, что и SettingsModalController, который достаёт её через
    /// GetComponent — ровно как уже сделано для громкостей. Персистентность
    /// вынесена в <see cref="IMotionSettingsStorage"/>, тем же приёмом, что и
    /// <see cref="Mikey.UI.Audio.AudioSettingsStore"/> — MonoBehaviour не умеет
    /// параметризованный конструктор, поэтому хранилище задаётся значением
    /// поля по умолчанию, а <see cref="SetStorageForTesting"/> — единственный
    /// крюк, которым тест подменяет его фейком в памяти до первой загрузки.
    /// </summary>
    public sealed class MotionSettingsStore : MonoBehaviour, IMotionSettings
    {
        public const string ReducedMotionKey = "Mikey.Settings.ReducedMotion";

        private IMotionSettingsStorage _storage = new PlayerPrefsMotionSettingsStorage();

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
                _storage.Save(ReducedMotionKey, value);
                Changed?.Invoke();
            }
        }

        /// <summary>Тестовый крюк: подменяет хранилище фейком в памяти до первого обращения к ReducedMotion, чтобы тест не трогал реальный PlayerPrefs.</summary>
        public void SetStorageForTesting(IMotionSettingsStorage storage) => _storage = storage;

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            _reducedMotion = _storage.TryLoad(ReducedMotionKey, out bool value) && value;
        }
    }
}
