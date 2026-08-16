using System;
using System.Collections;
using Mikey.UI.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Drives the one shared Settings modal (Music/SFX/Trainer Voice, see
    /// Settings.uss ".settings-modal") reachable from Main Menu and both map
    /// screens. Deliberately screen-agnostic: it knows nothing about
    /// ScreenManager or which screen is currently active — it just shows/
    /// hides one overlay and two-way-binds its three sliders directly to the
    /// shared <see cref="IAudioSettings"/> instance, once. Because every
    /// entry point opens this exact same element (not a per-screen copy),
    /// "same value shown everywhere" and "closing never disturbs the screen
    /// underneath" both hold by construction, not by extra synchronization
    /// code. Mirrors the bind/entry/unsubscribe pattern used throughout this
    /// app (see HomeController, JapanMapController).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SettingsModalController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string OpenClass = "settings-modal--open";

        private VisualElement _modal;
        private Button _closeButton;
        private Slider _musicSlider;
        private Slider _sfxSlider;
        private Slider _trainerVoiceSlider;

        private readonly Button[] _openButtons = new Button[3];
        private static readonly string[] OpenButtonNames =
        {
            "menu-settings-open",
            "map-topbar-settings",
            "okinawa-topbar-settings",
        };

        private IAudioSettings _audioSettings;
        private EventCallback<ChangeEvent<float>> _musicChangedCallback;
        private EventCallback<ChangeEvent<float>> _sfxChangedCallback;
        private EventCallback<ChangeEvent<float>> _trainerVoiceChangedCallback;

        private Coroutine _bindRoutine;
        private bool _bound;

        private void OnEnable()
        {
            if (_bound)
                return;
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            if (_bound)
            {
                for (int i = 0; i < _openButtons.Length; i++)
                {
                    if (_openButtons[i] != null)
                        _openButtons[i].clicked -= Open;
                }
                if (_closeButton != null)
                    _closeButton.clicked -= Close;

                if (_musicSlider != null && _musicChangedCallback != null)
                    _musicSlider.UnregisterValueChangedCallback(_musicChangedCallback);
                if (_sfxSlider != null && _sfxChangedCallback != null)
                    _sfxSlider.UnregisterValueChangedCallback(_sfxChangedCallback);
                if (_trainerVoiceSlider != null && _trainerVoiceChangedCallback != null)
                    _trainerVoiceSlider.UnregisterValueChangedCallback(_trainerVoiceChangedCallback);
            }

            _modal = null;
            _closeButton = null;
            _musicSlider = null;
            _sfxSlider = null;
            _trainerVoiceSlider = null;
            _audioSettings = null;
            _musicChangedCallback = null;
            _sfxChangedCallback = null;
            _trainerVoiceChangedCallback = null;
            for (int i = 0; i < _openButtons.Length; i++)
                _openButtons[i] = null;
            _bound = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[SettingsModalController] UIDocument root unavailable; Settings not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _modal = root.Q<VisualElement>("shared-settings-modal");
            _closeButton = root.Q<Button>("shared-settings-close");
            _musicSlider = root.Q<Slider>("shared-settings-music");
            _sfxSlider = root.Q<Slider>("shared-settings-sfx");
            _trainerVoiceSlider = root.Q<Slider>("shared-settings-trainer");

            for (int i = 0; i < OpenButtonNames.Length; i++)
                _openButtons[i] = root.Q<Button>(OpenButtonNames[i]);

            if (_modal == null || _closeButton == null)
            {
                Debug.LogError("[SettingsModalController] Shared Settings modal elements missing; not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            for (int i = 0; i < _openButtons.Length; i++)
            {
                if (_openButtons[i] != null)
                    _openButtons[i].clicked += Open;
            }
            _closeButton.clicked += Close;

            _audioSettings = GetComponent<IAudioSettings>();
            _musicChangedCallback = WireSlider(_musicSlider, _audioSettings, (s, v) => s.MusicVolume = v, s => s.MusicVolume);
            _sfxChangedCallback = WireSlider(_sfxSlider, _audioSettings, (s, v) => s.SfxVolume = v, s => s.SfxVolume);
            _trainerVoiceChangedCallback = WireSlider(_trainerVoiceSlider, _audioSettings, (s, v) => s.TrainerVoiceVolume = v, s => s.TrainerVoiceVolume);

            Close();

            _bound = true;
            _bindRoutine = null;
        }

        /// <summary>Shows the modal, blocking interaction with whatever screen is underneath.</summary>
        public void Open() => _modal?.AddToClassList(OpenClass);

        /// <summary>
        /// Hides the modal only — never touches screen navigation, map pan/
        /// zoom, or map chapter context, so the screen underneath (Main Menu,
        /// Japan, or Okinawa) is always left exactly as it was.
        /// </summary>
        public void Close() => _modal?.RemoveFromClassList(OpenClass);

        private static EventCallback<ChangeEvent<float>> WireSlider(
            Slider slider,
            IAudioSettings settings,
            Action<IAudioSettings, float> apply,
            Func<IAudioSettings, float> read)
        {
            if (slider == null || settings == null)
                return null;

            slider.value = read(settings);
            EventCallback<ChangeEvent<float>> callback = evt => apply(settings, evt.newValue);
            slider.RegisterValueChangedCallback(callback);
            return callback;
        }
    }
}
