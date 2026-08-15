using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Audio.Tests
{
    /// <summary>
    /// Contract for AudioController's wiring: UI click SFX is auto-wired by USS
    /// class (no per-screen code, mirrors ScreenManager's "go-" convention),
    /// Main Menu music playback is tied to the "menu" screen id, fight tracks and
    /// Trainer voice-line playback are deliberately not wired, and subscriptions
    /// are unbound on disable (no duplicate-subscription leak). Verified by reading
    /// the source, mirroring HomeControllerSourceTests for MonoBehaviour internals
    /// not practical to drive through a live panel in EditMode.
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
        public void MenuMusic_IsTiedToTheMenuScreenId()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("MenuScreenId = \"menu\"", source);
            StringAssert.Contains("private void OnScreenChanged(string screenId)", source);
            StringAssert.Contains("if (screenId == MenuScreenId)", source);
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
