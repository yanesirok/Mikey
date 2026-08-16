using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Audio.Tests
{
    /// <summary>
    /// Contract for AudioController's wiring: UI click SFX is auto-wired by USS
    /// class (no per-screen code, mirrors ScreenManager's "go-" convention), the
    /// hub soundtrack plays continuously across every hub/navigation screen and
    /// only fades for actual training/gameplay content (see
    /// AudioControllerHubMusicTests for the pure hub/non-hub transition-policy
    /// unit tests), fight tracks and Trainer voice-line playback are deliberately
    /// not wired, and subscriptions are unbound on disable (no duplicate-
    /// subscription leak). Verified by reading the source, mirroring
    /// HomeControllerSourceTests for MonoBehaviour internals not practical to
    /// drive through a live panel in EditMode.
    /// </summary>
    public class AudioControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Audio/AudioController.cs";

        [Test]
        public void UiClickSfx_IsAutoWiredByUssClass_NoPerScreenCode()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("SfxClickClass = \"sfx-click\"", source);
            StringAssert.Contains("Query<Button>(className: SfxClickClass)", source,
                "UI click SFX must be auto-wired by USS class across the whole app, not per-screen.");
        }

        [Test]
        public void OnScreenChanged_DrivesHubMusicOnlyThroughThePureTransitionPolicy()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenChanged(string screenId)", source);
            StringAssert.Contains("bool isHub = IsHubScreen(screenId);", source);
            StringAssert.Contains("if (ShouldStartHubMusic(_wasInHub, isHub))", source);
            StringAssert.Contains("PlayHubMusic();", source);
            StringAssert.Contains("else if (ShouldStopHubMusic(_wasInHub, isHub))", source);
            StringAssert.Contains("PauseHubMusic();", source);
            StringAssert.Contains("_wasInHub = isHub;", source,
                "The previous hub/non-hub state must be tracked so hub-to-hub transitions (e.g. Menu -> Map) never re-trigger a start/stop.");
        }

        [Test]
        public void HubScreenIds_CoversExactlyTheFiveShellNavigationScreens()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("\"menu\", \"map\", \"mapOkinawa\", \"profile\", \"techniques\",", source,
                "The hub soundtrack must cover Main Menu, Japan Map, Okinawa Map, Profile and Techniques — training/gameplay screens (combineIntro, camTest, combine, practice) and pre-hub screens (title, intro) must not be included.");
        }

        [Test]
        public void PlayHubMusic_ResumesViaUnPause_NeverASecondPlayCall_NoDuplicateSource()
        {
            string source = File.ReadAllText(SourcePath);
            int methodIndex = source.IndexOf("private void PlayHubMusic()", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(methodIndex, 0);
            int nextMethodIndex = source.IndexOf("private void PauseHubMusic()", methodIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(nextMethodIndex, 0);
            string body = source.Substring(methodIndex, nextMethodIndex - methodIndex);

            StringAssert.Contains("_musicSource.UnPause();", body,
                "Returning to the hub must resume the existing AudioSource, never restart it from zero.");
            StringAssert.Contains("_musicSource.Play();", body,
                "The very first hub entry must still start playback once.");

            // Exactly one music AudioSource is ever created (OnEnable), regardless
            // of how many times PlayHubMusic runs across the app's lifetime.
            int addComponentCount = 0;
            int searchFrom = 0;
            while (true)
            {
                int idx = source.IndexOf("gameObject.AddComponent<AudioSource>()", searchFrom, System.StringComparison.Ordinal);
                if (idx < 0) break;
                addComponentCount++;
                searchFrom = idx + 1;
            }
            Assert.AreEqual(2, addComponentCount,
                "Expected exactly two AddComponent<AudioSource> calls total (music + SFX), both in OnEnable — never one per hub entry.");
        }

        [Test]
        public void FightTracks_AreNotWired()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("fight", source.ToLowerInvariant(),
                "AudioController must not reference any fight-music track yet.");
        }

        [Test]
        public void TrainerVoiceLines_AreNotWiredForPlayback()
        {
            string source = File.ReadAllText(SourcePath);
            // Trainer Voice is settings-only: the volume property may exist (via
            // IAudioSettings), but no AudioClip/AudioSource playback for it yet.
            StringAssert.DoesNotContain("trainerVoiceClip", source);
            StringAssert.DoesNotContain("PlayTrainerVoice", source);
        }

        [Test]
        public void OnDisable_UnbindsSfxButtons_AndUnsubscribesScreenChanged_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_sfxBindings[i].Unbind();", source);
            StringAssert.Contains("_sfxBindings.Clear();", source);
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenChanged;", source);
        }
    }
}
