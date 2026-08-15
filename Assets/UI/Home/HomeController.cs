using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.Audio;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Home
{
    /// <summary>
    /// Drives the Main Menu ("menu") screen — the cinematic PLAY / PLANS / SETTINGS
    /// / QUIT navigation shown over the full-bleed menu video. PLAY is a plain
    /// ScreenManager screen-navigator button targeting the Map screen (no gating,
    /// so it needs no controller code of its own); this controller owns the two
    /// local overlays (Plans/Settings, shown on top of
    /// the menu without leaving the screen — the menu video/music underneath is
    /// untouched by BackgroundMediaController/AudioController, which both key off
    /// the screen id alone), the Settings sliders' two-way binding to
    /// <see cref="IAudioSettings"/>, and QUIT's platform-specific behavior. Formerly
    /// the old Home dashboard's controller (dynamic CTA + Map/Techniques dock
    /// locking); that entire old design is retired with this rebuild.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HomeController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller reacts to (Main Menu's own entry resets any open modal).</summary>
        public const string ScreenId = "menu";

        private VisualElement _plansModal;
        private VisualElement _settingsModal;
        private Button _plansOpenButton;
        private Button _plansCloseButton;
        private Button _settingsOpenButton;
        private Button _settingsCloseButton;
        private Button _quitButton;
        private Slider _musicSlider;
        private Slider _sfxSlider;
        private Slider _trainerVoiceSlider;

        private IScreenNavigator _navigator;
        private IAudioSettings _audioSettings;

        private EventCallback<ChangeEvent<float>> _musicChangedCallback;
        private EventCallback<ChangeEvent<float>> _sfxChangedCallback;
        private EventCallback<ChangeEvent<float>> _trainerVoiceChangedCallback;
        private readonly List<ButtonBinding> _buttonBindings = new List<ButtonBinding>();

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
                for (int i = 0; i < _buttonBindings.Count; i++)
                    _buttonBindings[i].Unbind();
                _buttonBindings.Clear();

                if (_musicSlider != null && _musicChangedCallback != null)
                    _musicSlider.UnregisterValueChangedCallback(_musicChangedCallback);
                if (_sfxSlider != null && _sfxChangedCallback != null)
                    _sfxSlider.UnregisterValueChangedCallback(_sfxChangedCallback);
                if (_trainerVoiceSlider != null && _trainerVoiceChangedCallback != null)
                    _trainerVoiceSlider.UnregisterValueChangedCallback(_trainerVoiceChangedCallback);
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenEntered;
                _navigator = null;
            }

            _audioSettings = null;
            _plansModal = null;
            _settingsModal = null;
            _plansOpenButton = null;
            _plansCloseButton = null;
            _settingsOpenButton = null;
            _settingsCloseButton = null;
            _quitButton = null;
            _musicSlider = null;
            _sfxSlider = null;
            _trainerVoiceSlider = null;
            _musicChangedCallback = null;
            _sfxChangedCallback = null;
            _trainerVoiceChangedCallback = null;
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
                    Debug.LogError("[HomeController] UIDocument root unavailable; Main Menu not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _plansModal = root.Q<VisualElement>("menu-plans-modal");
            _settingsModal = root.Q<VisualElement>("menu-settings-modal");
            _plansOpenButton = root.Q<Button>("menu-plans-open");
            _plansCloseButton = root.Q<Button>("menu-plans-close");
            _settingsOpenButton = root.Q<Button>("menu-settings-open");
            _settingsCloseButton = root.Q<Button>("menu-settings-close");
            _quitButton = root.Q<Button>("menu-quit");
            _musicSlider = root.Q<Slider>("menu-settings-music");
            _sfxSlider = root.Q<Slider>("menu-settings-sfx");
            _trainerVoiceSlider = root.Q<Slider>("menu-settings-trainer");

            if (_plansModal == null || _settingsModal == null || _plansOpenButton == null || _plansCloseButton == null
                || _settingsOpenButton == null || _settingsCloseButton == null || _quitButton == null)
            {
                Debug.LogError("[HomeController] Main Menu elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            BindButton(_plansOpenButton, () => ShowModal(_plansModal));
            BindButton(_plansCloseButton, () => HideModal(_plansModal));
            BindButton(_settingsOpenButton, () => ShowModal(_settingsModal));
            BindButton(_settingsCloseButton, () => HideModal(_settingsModal));
            BindButton(_quitButton, OnQuitClicked);

            _audioSettings = GetComponent<IAudioSettings>();
            WireSlider(_musicSlider, _audioSettings,
                (settings, v) => settings.MusicVolume = v,
                settings => settings.MusicVolume,
                callback => _musicChangedCallback = callback);
            WireSlider(_sfxSlider, _audioSettings,
                (settings, v) => settings.SfxVolume = v,
                settings => settings.SfxVolume,
                callback => _sfxChangedCallback = callback);
            WireSlider(_trainerVoiceSlider, _audioSettings,
                (settings, v) => settings.TrainerVoiceVolume = v,
                settings => settings.TrainerVoiceVolume,
                callback => _trainerVoiceChangedCallback = callback);

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            HideModal(_plansModal);
            HideModal(_settingsModal);

            _bound = true;
            _bindRoutine = null;
        }

        private void BindButton(Button button, Action onClick)
        {
            button.clicked += onClick;
            _buttonBindings.Add(new ButtonBinding(button, onClick));
        }

        private static void WireSlider(
            Slider slider,
            IAudioSettings settings,
            Action<IAudioSettings, float> apply,
            Func<IAudioSettings, float> read,
            Action<EventCallback<ChangeEvent<float>>> storeCallback)
        {
            if (slider == null || settings == null)
                return;

            slider.value = read(settings);

            EventCallback<ChangeEvent<float>> callback = evt => apply(settings, evt.newValue);
            slider.RegisterValueChangedCallback(callback);
            storeCallback(callback);
        }

        private static void ShowModal(VisualElement modal)
        {
            if (modal != null)
                modal.style.display = DisplayStyle.Flex;
        }

        private static void HideModal(VisualElement modal)
        {
            if (modal != null)
                modal.style.display = DisplayStyle.None;
        }

        /// <summary>Main Menu is Mobile-first (Android) but QUIT is never platform-hidden — only its behavior differs.</summary>
        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            Debug.Log("[HomeController] Quit requested — no-op in the Editor.");
#else
            Application.Quit();
#endif
        }

        /// <summary>Re-entering Main Menu always resets any modal left open on a previous visit.</summary>
        private void OnScreenEntered(string screenId)
        {
            if (screenId != ScreenId)
                return;

            HideModal(_plansModal);
            HideModal(_settingsModal);
        }

        private readonly struct ButtonBinding
        {
            private readonly Button _button;
            private readonly Action _callback;

            public ButtonBinding(Button button, Action callback)
            {
                _button = button;
                _callback = callback;
            }

            public void Unbind()
            {
                if (_button != null && _callback != null)
                    _button.clicked -= _callback;
            }
        }
    }
}
