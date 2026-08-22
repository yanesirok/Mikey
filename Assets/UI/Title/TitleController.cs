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
    /// full-bleed near-black backdrop, then opens Sign In ("menu"), either
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
    /// keeps rendering once Sign In opens, and returning to Title (Editor/testing)
    /// restarts predictably.
    ///
    /// Advancing is no longer an instant cut: whatever triggered it (natural
    /// completion, tap-skip, or the error fallback) freezes on the actual
    /// final frame of <see cref="logoIntroClip"/> itself — never a separate
    /// static image — and, via <see cref="_shellPreloader"/>, holds there
    /// until Sign In's background video is ready (plus a short minimum
    /// hold on fast devices so it never flashes by). Only then does it fade
    /// to black, hold briefly on full black, swap to Sign In while fully
    /// covered, and fade Sign In in — through the shared
    /// <see cref="_transitionOverlay"/>, see <see cref="AdvanceRoutine"/>.
    /// Sign In itself may hand straight through to Lore or the Map without ever
    /// being seen (see Mikey.UI.Home.HomeController.ShouldSkipGate); that swap is
    /// deliberately transition-free so it happens inside this same fade.
    /// Natural completion already leaves the (non-looping) VideoPlayer
    /// stopped exactly on its last rendered frame, so nothing needs to
    /// change there; an early tap-skip instead seeks into the final
    /// held-logo portion of the same video (see
    /// <see cref="SeekToFinalFrame"/>) rather than cutting away from
    /// whatever random mid-animation frame the tap landed on.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TitleController : MonoBehaviour
    {
        /// <summary>The screen id this controller drives.</summary>
        public const string ScreenId = "title";

        /// <summary>Where Logo Intro always advances to, whether by video completion or tap.</summary>
        public const string NextScreenId = "menu";

        private const string VideoTargetElementName = "title-video";
        private const int MaxRootResolveFrames = 30;

        /// <summary>Minimum time the final-frame hold stays up even when the shell is already ready, so it never flashes by on fast devices.</summary>
        private const float MinHoldSeconds = 0.25f;

        /// <summary>
        /// Hard cap on the shell-preload hold. The preloader is supposed to report
        /// ready (or failed) on its own, but this is the app's very first screen and
        /// it has no button: a preloader that never settles would strand the player
        /// on a frozen logo frame with no way forward. Launch continues regardless
        /// once this elapses — a missing background video is a cosmetic loss, a
        /// dead launch is not.
        /// </summary>
        private const float MaxShellWaitSeconds = 8f;

        /// <summary>
        /// How far before the video's own end an early tap-skip seeks, since
        /// frame-accurate seeking on a compressed video is not reliable — this
        /// lands inside the video's own held-logo portion rather than at the
        /// exact last frame.
        /// </summary>
        private const float SkipSeekBackSeconds = 0.15f;

        /// <summary>How long the frozen final frame darkens to pure black.</summary>
        private const float FadeToBlackSeconds = 0.5f;

        /// <summary>How long the screen holds on full black (Sign In is swapped in during this hold, while fully covered).</summary>
        private const float BlackHoldSeconds = 0.12f;

        /// <summary>How long Sign In fades in from black once it is the active screen.</summary>
        private const float FadeInSeconds = 0.7f;

        [SerializeField]
        [Tooltip("Final logo animation (logo_intro.mp4), played once. Natural completion advances to Sign In.")]
        private VideoClip logoIntroClip;

        private IScreenNavigator _navigator;
        private IAudioSettings _audioSettings;
        private IShellPreloader _shellPreloader;
        private ITransitionOverlay _transitionOverlay;
        private VisualElement _titleScreen;
        private VisualElement _videoTarget;
        private EventCallback<ClickEvent> _tapCallback;
        private Coroutine _bindRoutine;
        private Coroutine _advanceRoutine;

        private VideoPlayer _player;
        private RenderTexture _renderTexture;
        private AudioSource _videoAudio;

        private bool _navigated;

        private void OnEnable()
        {
            _navigated = false;
            _navigator = GetComponent<IScreenNavigator>();
            _audioSettings = GetComponent<IAudioSettings>();
            _shellPreloader = GetComponent<IShellPreloader>();
            _transitionOverlay = GetComponent<ITransitionOverlay>();
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_advanceRoutine != null)
            {
                StopCoroutine(_advanceRoutine);
                _advanceRoutine = null;
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
            _shellPreloader = null;
            _transitionOverlay = null;
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

            _tapCallback = _ =>
            {
                SeekToFinalFrame();
                Advance();
            };
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

            if (_advanceRoutine != null)
            {
                StopCoroutine(_advanceRoutine);
                _advanceRoutine = null;
            }

            // Kick off preparing the immediate shell flow (currently: the Main
            // Menu background video) the moment Logo Intro starts, so most of
            // it finishes invisibly while the user watches the video.
            _shellPreloader?.BeginPreload();

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

        /// <summary>Stops the logo video completely so it never keeps rendering after Sign In opens.</summary>
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
            Debug.LogWarning($"[TitleController] Logo intro video error: {message}. Advancing to Sign In.", this);
            Advance();
        }

        /// <summary>Begins advancing to Sign In exactly once, however it was triggered (video completion, tap, or error fallback).</summary>
        private void Advance()
        {
            if (_navigated || _navigator == null)
                return;
            _navigated = true;

            _advanceRoutine = StartCoroutine(AdvanceRoutine());
        }

        /// <summary>
        /// Freezes on the video's own final frame, waits for the shell to be
        /// ready (with a short minimum hold so it never flashes by on a fast
        /// device, and a hard <see cref="MaxShellWaitSeconds"/> cap so launch can
        /// never stall here), then fades to black, holds briefly on full black,
        /// swaps to Sign In while fully covered, and fades Sign In in.
        /// </summary>
        private IEnumerator AdvanceRoutine()
        {
            FreezeVideo();

            float holdStart = Time.unscaledTime;
            while (_shellPreloader != null
                   && !_shellPreloader.IsReady
                   && Time.unscaledTime - holdStart < MaxShellWaitSeconds)
                yield return null;

            if (_shellPreloader != null && !_shellPreloader.IsReady)
                Debug.LogWarning($"[TitleController] Shell preload did not report ready within " +
                                 $"{MaxShellWaitSeconds:F0}s; advancing to Sign In anyway.", this);

            float remainingHold = MinHoldSeconds - (Time.unscaledTime - holdStart);
            if (remainingHold > 0f)
                yield return new WaitForSecondsRealtime(remainingHold);

            if (_transitionOverlay != null)
                yield return StartCoroutine(_transitionOverlay.FadeToBlack(FadeToBlackSeconds));

            yield return new WaitForSecondsRealtime(BlackHoldSeconds);

            _navigator.Show(NextScreenId);

            if (_transitionOverlay != null)
                yield return StartCoroutine(_transitionOverlay.FadeFromBlack(FadeInSeconds));

            _advanceRoutine = null;
        }

        /// <summary>
        /// Defensive catch-all so the hold never keeps advancing frames: pauses
        /// if still playing. Natural completion already leaves the (non-looping)
        /// VideoPlayer stopped on its final rendered frame, and an early
        /// tap-skip already paused via <see cref="SeekToFinalFrame"/> — the
        /// rendered frame (and its render texture) is never hidden, cleared or
        /// swapped for a separate image.
        /// </summary>
        private void FreezeVideo()
        {
            if (_player != null && _player.isPlaying)
                _player.Pause();
        }

        /// <summary>
        /// Tap-to-skip only: pauses mid-playback and seeks into the final
        /// held-logo portion of the SAME video, so the hold shows the actual
        /// final logo pose instead of cutting away from whatever random
        /// mid-animation frame the tap landed on. Frame-accurate seeking on a
        /// compressed video is not reliable, so this seeks a small fixed
        /// distance before the end (<see cref="SkipSeekBackSeconds"/>) rather
        /// than to the exact last frame.
        /// </summary>
        private void SeekToFinalFrame()
        {
            if (_player == null || !_player.isPrepared || _player.length <= 0d)
                return;

            if (_player.isPlaying || _player.isPaused)
                _player.Pause();

            _player.time = System.Math.Max(0d, _player.length - SkipSeekBackSeconds);
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
