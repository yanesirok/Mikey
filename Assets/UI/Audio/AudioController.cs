using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Audio
{
    /// <summary>
    /// The app's smallest reusable frontend audio system: exposes <see cref="IAudioSettings"/>
    /// (Music/SFX/Trainer Voice volume, persisted via PlayerPrefs through
    /// <see cref="AudioSettingsStore"/>), plays the one shared UI click sound for every
    /// element carrying the "sfx-click" USS class (same auto-wiring convention
    /// ScreenManager uses for "go-&lt;id&gt;" navigators — add the class in UXML, no
    /// code/Inspector wiring needed), and owns the hub/shell soundtrack's lifecycle.
    /// The soundtrack plays continuously across every hub/navigation screen
    /// (<see cref="HubScreenIds"/> — Main Menu, Japan/Okinawa Map, Profile,
    /// Techniques) and any local overlay on top of them (Settings/Vow — these are
    /// not ScreenManager screens, so opening them never even raises
    /// <see cref="IScreenNavigator.ScreenChanged"/>): moving between hub screens
    /// never restarts or re-fades it (<see cref="ShouldStartHubMusic"/>/
    /// <see cref="ShouldStopHubMusic"/> only fire on a genuine hub/non-hub
    /// boundary crossing). It only fades out entering actual training/gameplay
    /// content (combineIntro, camTest, combine, practice) and fades back in
    /// (resuming via <see cref="AudioSource.UnPause"/>, never a second
    /// <see cref="AudioSource.Play"/> or a duplicate AudioSource) on return to
    /// any hub screen. The combat-scene music candidates and Trainer voice-line
    /// playback are deliberately not wired here yet.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AudioController : MonoBehaviour, IAudioSettings
    {
        private const string SfxClickClass = "sfx-click";
        private const int MaxRootResolveFrames = 30;
        private const float FadeSeconds = 0.2f;

        /// <summary>
        /// Hub/shell screens the soundtrack plays continuously across. Every
        /// other screen is not part of the hub: Logo Intro and Lore (before the
        /// hub — the soundtrack has not started yet) and training/gameplay
        /// content (combineIntro, camTest, combine, practice — the soundtrack
        /// fades out for these).
        /// </summary>
        public static readonly HashSet<string> HubScreenIds = new HashSet<string>
        {
            "menu", "map", "mapOkinawa", "profile", "techniques",
        };

        [SerializeField] private AudioClip menuMusicClip;
        [SerializeField] private AudioClip uiClickClip;

        private AudioSettingsStore _settings;
        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private IScreenNavigator _navigator;

        private readonly List<ButtonSfxBinding> _sfxBindings = new List<ButtonSfxBinding>();
        private Coroutine _bindRoutine;
        private Coroutine _fadeRoutine;
        private bool _musicStarted;
        private bool _wasInHub;

        public event Action Changed
        {
            add => _settings.Changed += value;
            remove => _settings.Changed -= value;
        }

        public float MusicVolume
        {
            get => _settings.MusicVolume;
            set
            {
                _settings.MusicVolume = value;
                if (_musicSource != null && _fadeRoutine == null)
                    _musicSource.volume = _settings.MusicVolume;
            }
        }

        public float SfxVolume
        {
            get => _settings.SfxVolume;
            set => _settings.SfxVolume = value;
        }

        public float TrainerVoiceVolume
        {
            get => _settings.TrainerVoiceVolume;
            set => _settings.TrainerVoiceVolume = value;
        }

        private void Awake()
        {
            _settings = new AudioSettingsStore(new PlayerPrefsAudioSettingsStorage());
        }

        private void OnEnable()
        {
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.clip = menuMusicClip;
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.volume = _settings.MusicVolume;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            _bindRoutine = StartCoroutine(BindSfxWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            for (int i = 0; i < _sfxBindings.Count; i++)
                _sfxBindings[i].Unbind();
            _sfxBindings.Clear();

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            if (_musicSource != null)
                Destroy(_musicSource);
            if (_sfxSource != null)
                Destroy(_sfxSource);
            _musicSource = null;
            _sfxSource = null;
            _musicStarted = false;
            _wasInHub = false;
        }

        private IEnumerator BindSfxWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[AudioController] UIDocument root unavailable; UI click SFX not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            document.rootVisualElement.Query<Button>(className: SfxClickClass).ForEach(button =>
            {
                Action callback = PlayUiClick;
                button.clicked += callback;
                _sfxBindings.Add(new ButtonSfxBinding(button, callback));
            });

            _bindRoutine = null;
        }

        /// <summary>Plays the single shared UI click sound at the current SFX volume. Safe to call even if no clip is assigned.</summary>
        public void PlayUiClick()
        {
            if (_sfxSource != null && uiClickClip != null)
                _sfxSource.PlayOneShot(uiClickClip, _settings.SfxVolume);
        }

        private void OnScreenChanged(string screenId)
        {
            bool isHub = IsHubScreen(screenId);

            if (ShouldStartHubMusic(_wasInHub, isHub))
                PlayHubMusic();
            else if (ShouldStopHubMusic(_wasInHub, isHub))
                PauseHubMusic();

            _wasInHub = isHub;
        }

        /// <summary>True for every hub/shell screen the soundtrack plays continuously across.</summary>
        public static bool IsHubScreen(string screenId) => HubScreenIds.Contains(screenId);

        /// <summary>
        /// Pure decision: start the hub soundtrack only on a genuine non-hub ->
        /// hub transition (e.g. Lore -> Menu, or training -> Menu/Map/...),
        /// never on a hub -> hub one (e.g. Menu -> Map) — that would restart or
        /// re-fade a soundtrack that is already playing. Unit-tested.
        /// </summary>
        public static bool ShouldStartHubMusic(bool wasInHub, bool isHub) => isHub && !wasInHub;

        /// <summary>
        /// Pure decision: stop the hub soundtrack only on a genuine hub ->
        /// non-hub transition (e.g. Techniques -> Practice), never on a
        /// non-hub -> non-hub one (e.g. within a multi-screen training flow) or
        /// a hub -> hub one. Unit-tested.
        /// </summary>
        public static bool ShouldStopHubMusic(bool wasInHub, bool isHub) => !isHub && wasInHub;

        private void PlayHubMusic()
        {
            if (_musicSource == null || menuMusicClip == null)
                return;

            if (!_musicStarted)
            {
                _musicStarted = true;
                _musicSource.volume = _settings.MusicVolume;
                _musicSource.Play();
            }
            else
            {
                _musicSource.UnPause();
            }

            RestartFade(FadeIn());
        }

        private void PauseHubMusic()
        {
            if (_musicSource == null || !_musicStarted)
                return;

            RestartFade(FadeOutThenPause());
        }

        private void RestartFade(IEnumerator routine)
        {
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(routine);
        }

        private IEnumerator FadeIn()
        {
            float target = _settings.MusicVolume;
            float elapsed = 0f;
            while (elapsed < FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _musicSource.volume = target * Mathf.Clamp01(elapsed / FadeSeconds);
                yield return null;
            }
            _musicSource.volume = target;
            _fadeRoutine = null;
        }

        private IEnumerator FadeOutThenPause()
        {
            float start = _musicSource.volume;
            float elapsed = 0f;
            while (elapsed < FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _musicSource.volume = start * (1f - Mathf.Clamp01(elapsed / FadeSeconds));
                yield return null;
            }
            _musicSource.Pause();
            _musicSource.volume = _settings.MusicVolume;
            _fadeRoutine = null;
        }

        private readonly struct ButtonSfxBinding
        {
            private readonly Button _button;
            private readonly Action _callback;

            public ButtonSfxBinding(Button button, Action callback)
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
