using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Title.Tests
{
    /// <summary>
    /// Contract for TitleController's wiring: it plays the final logo animation
    /// exactly once (no looping) and advances from Logo Intro to Lore when that
    /// video completes, on a tap/click anywhere, or if the VideoPlayer reports an
    /// error; a single guard makes sure only one of those triggers ever navigates
    /// (no double navigation if two occur together), and it drives navigation
    /// itself (unlike every "go-" screen, Title has no button). Playback lifecycle
    /// is tied to genuine screen entry/exit, and leaving Title stops the video
    /// completely. Verified by reading the source, mirroring
    /// HomeControllerSourceTests / IntroControllerTests for MonoBehaviour
    /// internals not practical to drive through a live panel in EditMode.
    /// </summary>
    public class TitleControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Title/TitleController.cs";

        [Test]
        public void NextScreen_IsIntro()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("NextScreenId = \"intro\"", source);
        }

        [Test]
        public void TapAnywhereOnScreen_RegistersAClickCallback()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_titleScreen.RegisterCallback(_tapCallback)", source,
                "A tap/click anywhere on the title screen must skip immediately.");
        }

        [Test]
        public void Advance_GuardsAgainstDoubleNavigation()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void Advance()", source);
            StringAssert.Contains("if (_navigated || _navigator == null)", source);
            StringAssert.Contains("_navigated = true;", source,
                "Video completion, tap and the error fallback must all funnel through the same guarded Advance().");
        }

        [Test]
        public void Controller_DrivesNavigationItself_UnlikeGoNavigators()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_navigator.Show(NextScreenId);", source,
                "Title has no button; unlike passive controllers (e.g. IntroController), it must actively navigate.");
        }

        [Test]
        public void OnDisable_StopsPlaybackAndUnregistersTapCallback_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_titleScreen.UnregisterCallback(_tapCallback);", source);
            StringAssert.Contains("_player.Stop();", source);
            StringAssert.Contains("Destroy(_player.gameObject);", source);
        }

        [Test]
        public void LogoVideo_IsConfiguredAsOneShot_NotLooping()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_player.isLooping = false", source,
                "The logo intro must play exactly once, never loop.");
            StringAssert.Contains("_player.playOnAwake = false", source);
        }

        [Test]
        public void LogoVideo_NaturalCompletion_DrivesNavigation()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_player.loopPointReached += OnLoopPointReached", source,
                "The actual end of the video (loopPointReached on a non-looping player) must control progression, not a fixed timer.");
            StringAssert.Contains("private void OnLoopPointReached(VideoPlayer source)", source);
        }

        [Test]
        public void LogoVideo_ErrorReceived_FallsBackToAdvance()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_player.errorReceived += OnErrorReceived", source,
                "A VideoPlayer failure must not strand the user on a black screen forever.");
        }

        [Test]
        public void EnteringTitle_RestartsPlaybackFromFrameZero()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("player.frame = 0;", source,
                "Entering (or re-entering, e.g. in Editor/testing) Title must restart the logo video from frame 0.");
        }

        [Test]
        public void PlaybackLifecycle_IsDrivenByScreenChanged_NotOnEnable()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_navigator.ScreenChanged += OnScreenChanged;", source,
                "The shared 'UI' GameObject is always enabled, so playback must react to genuine screen entry/exit via ScreenChanged, not OnEnable.");
            StringAssert.Contains("private void EnterTitle()", source);
            StringAssert.Contains("private void LeaveTitle()", source);
        }

        [Test]
        public void LogoVideoAudio_RoutesThroughAudioSource_AtCurrentSfxVolume()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_player.audioOutputMode = VideoAudioOutputMode.AudioSource", source,
                "The logo's embedded audio must be audible (not muted) and routed through a real AudioSource.");
            StringAssert.Contains("_videoAudio.volume = _audioSettings?.SfxVolume ?? 1f;", source,
                "The embedded logo SFX must respect the current SFX volume.");
        }
    }
}
