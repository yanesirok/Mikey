using System.Collections;
using Mikey.UI.Audio;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Mikey.UI.Title
{
    /// <summary>
    /// Drives the Logo Intro ("title") screen — the app's very first screen: plays
    /// the final <see cref="logoIntroClip"/> animation exactly once against a
    /// full-bleed near-black backdrop, then opens Lore ("intro"), either
    /// automatically when the video finishes or immediately on a tap/click
    /// anywhere on the screen. A single <see cref="_navigated"/> guard ensures
    /// only one of those two triggers (plus the VideoPlayer-failure fallback)
    /// ever actually navigates, so a tap landing right as the video ends can
    /// never double-navigate. Unlike every other screen's action, this is not a
    /// ScreenManager "go-" navigator — Title has no button, so it drives
    /// <see cref="IScreenNavigator.Show"/> itself.
    /// Playback is tied to <see cref="IScreenNavigator.ScreenChanged"/> rather
    /// than MonoBehaviour OnEnable/OnDisable, which only fire once for the
    /// shared, always-enabled "UI" GameObject: entering "title" (re)plays the
    /// clip from frame 0 and leaving it stops playback completely, so nothing
    /// keeps rendering once Lore opens, and returning to Title (Editor/testing)
    /// restarts predictably.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TitleController : MonoBehaviour
    {
        /// <summary>The screen id this controller drives.</summary>
        public const string ScreenId = "title";

        /// <summary>Where Logo Intro always advances to, whether by video completion or tap.</summary>
        public const string NextScreenId = "intro";

        private const string VideoTargetElementName = "title-video";
        private const int MaxRootResolveFrames = 30;

        [SerializeField]
        [Tooltip("Final logo animation (logo_intro.mp4), played once. Natural completion advances to Lore.")]
        private VideoClip logoIntroClip;

        private IScreenNavigator _navigator;
        private IAudioSettings _audioSettings;
        private VisualElement _titleScreen;
        private VisualElement _videoTarget;
        private EventCallback<ClickEvent> _tapCallback;
        private Coroutine _bindRoutine;

        private VideoPlayer _player;
        private RenderTexture _renderTexture;
        private AudioSource _videoAudio;

        private bool _navigated;

        private void OnEnable()
        {
            _navigated = false;
            _navigator = GetComponent<IScreenNavigator>();
            _audioSettings = GetComponent<IAudioSettings>();
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            if (_titleScreen != null && _tapCallback != null)
                _titleScreen.UnregisterCallback(_tapCallback);
            _titleScreen = null;
            _videoTarget = null;
            _tapCallback = null;

            DestroyPlayer();
            _audioSettings = null;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[TitleController] UIDocument root unavailable; Logo screen not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _titleScreen = document.rootVisualElement.Q<VisualElement>(ScreenId);
            if (_titleScreen == null)
            {
                Debug.LogError("[TitleController] 'title' screen element missing; not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _videoTarget = _titleScreen.Q<VisualElement>(VideoTargetElementName);

            _tapCallback = _ => Advance();
            _titleScreen.RegisterCallback(_tapCallback);

            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                if (_navigator.CurrentScreen == ScreenId)
                    EnterTitle();
            }

            _bindRoutine = null;
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
                EnterTitle();
            else
                LeaveTitle();
        }

        /// <summary>(Re)starts the logo video from frame 0. Safe to call on every genuine entry, including Editor/testing re-entry.</summary>
        private void EnterTitle()
        {
            _navigated = false;

            if (logoIntroClip == null)
            {
                Debug.LogError("[TitleController] logoIntroClip not assigned; skipping Logo Intro.", this);
                Advance();
                return;
            }

            EnsurePlayer();

            if (_player.isPrepared)
                RestartAndPlay(_player);
            else
                _player.Prepare();
        }

        /// <summary>Stops the logo video completely so it never keeps rendering after Lore opens.</summary>
        private void LeaveTitle()
        {
            if (_player != null && (_player.isPlaying || _player.isPaused))
                _player.Stop();
            if (_videoTarget != null)
                _videoTarget.style.backgroundImage = StyleKeyword.Null;
        }

        private void EnsurePlayer()
        {
            if (_player != null)
                return;

            _renderTexture = new RenderTexture((int)logoIntroClip.width, (int)logoIntroClip.height, 0)
            {
                name = "TitleLogoIntroRT"
            };

            var playerGo = new GameObject("TitleLogoIntroVideoPlayer");
            playerGo.transform.SetParent(transform, false);

            _videoAudio = playerGo.AddComponent<AudioSource>();
            _videoAudio.playOnAwake = false;
            _videoAudio.spatialBlend = 0f;

            _player = playerGo.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _renderTexture;
            _player.source = VideoSource.VideoClip;
            _player.clip = logoIntroClip;
            // The logo animation carries its own brush-stroke sound design — unlike
            // the muted background loops elsewhere, this must be audible, so it is
            // routed through a real AudioSource instead of VideoAudioOutputMode.None.
            _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _player.SetTargetAudioSource(0, _videoAudio);
            _player.prepareCompleted += OnPrepareCompleted;
            _player.loopPointReached += OnLoopPointReached;
            _player.errorReceived += OnErrorReceived;
        }

        private void OnPrepareCompleted(VideoPlayer source)
        {
            // The user may have tapped away (or an error already advanced past
            // Title) while this was still preparing; only play if Title is still
            // the active screen.
            if (_navigator == null || _navigator.CurrentScreen != ScreenId)
                return;

            RestartAndPlay(source);
        }

        private void RestartAndPlay(VideoPlayer player)
        {
            player.frame = 0;
            if (_videoTarget != null)
                _videoTarget.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_renderTexture));
            if (_videoAudio != null)
                _videoAudio.volume = _audioSettings?.SfxVolume ?? 1f;
            player.Play();
        }

        /// <summary>Natural end of the (non-looping) logo video — the normal navigation trigger.</summary>
        private void OnLoopPointReached(VideoPlayer source)
        {
            Advance();
        }

        /// <summary>Safety fallback: a VideoPlayer failure must not strand the user on a black screen forever.</summary>
        private void OnErrorReceived(VideoPlayer source, string message)
        {
            Debug.LogWarning($"[TitleController] Logo intro video error: {message}. Advancing to Lore.", this);
            Advance();
        }

        /// <summary>Navigates to Lore exactly once, however it was triggered (video completion, tap, or error fallback).</summary>
        private void Advance()
        {
            if (_navigated || _navigator == null)
                return;
            _navigated = true;

            _navigator.Show(NextScreenId);
        }

        private void DestroyPlayer()
        {
            if (_player != null)
            {
                _player.prepareCompleted -= OnPrepareCompleted;
                _player.loopPointReached -= OnLoopPointReached;
                _player.errorReceived -= OnErrorReceived;
                _player.Stop();
                Destroy(_player.gameObject);
                _player = null;
            }
            _videoAudio = null;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }
    }
}
