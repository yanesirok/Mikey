using System;
using UnityEngine;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Хранение <see cref="IMotionSettings"/> в PlayerPrefs. Живёт на том же
    /// GameObject, что и SettingsModalController, который достаёт её через
    /// GetComponent — ровно как уже сделано для громкостей. Персистентность
    /// вынесена в <see cref="IMotionSettingsStorage"/>, той же формы, что и
    /// <see cref="Mikey.UI.Audio.IAudioSettingsStorage"/> — только это то, что
    /// разрешено правилом (см. PlayerPrefsKeyRegressionTests): прямую запись
    /// PlayerPrefs делает исключительно класс-хранилище из белого списка,
    /// а не store. Полноценное разделение на POCO-store + MonoBehaviour-обёртку
    /// (как у AudioSettingsStore/AudioController) здесь избыточно — вся логика
    /// это одно bool-поле с дефолтом и дедупликацией повторной записи,
    /// целиком проверяемая через настоящий PlayerPrefs; заводить его стоит
    /// только если store когда-нибудь разрастётся.
    /// </summary>
    public sealed class MotionSettingsStore : MonoBehaviour, IMotionSettings
    {
        public const string ReducedMotionKey = "Mikey.Settings.ReducedMotion";

        private readonly IMotionSettingsStorage _storage = new PlayerPrefsMotionSettingsStorage();

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

        private void Awake() => EnsureLoaded();

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            _reducedMotion = _storage.TryLoad(ReducedMotionKey, out bool value) && value;
        }
    }
}
