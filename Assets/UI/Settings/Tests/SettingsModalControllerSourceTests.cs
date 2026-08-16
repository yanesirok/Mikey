using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Settings.Tests
{
    /// <summary>
    /// Contract for SettingsModalController's wiring: all three entry points
    /// (Main Menu, Japan, Okinawa) are found and wired to open the SAME
    /// modal, the three sliders two-way bind to the existing shared
    /// <see cref="Mikey.UI.Audio.IAudioSettings"/> store (no separate
    /// PlayerPrefs/state), and — the key correctness property for "closing
    /// never disturbs the screen underneath" — this controller never
    /// references screen navigation or Map pan/zoom/context at all, so it
    /// physically cannot reset them. Verified by reading the source,
    /// mirroring this project's established technique for MonoBehaviour
    /// internals not practical to drive through a live panel in EditMode.
    /// </summary>
    public class SettingsModalControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Settings/SettingsModalController.cs";

        [Test]
        public void AllThreeEntryPoints_AreWiredToOpenTheSameModal()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("\"menu-settings-open\",", source);
            StringAssert.Contains("\"map-topbar-settings\",", source);
            StringAssert.Contains("\"okinawa-topbar-settings\",", source);
            StringAssert.Contains("_openButtons[i].clicked += Open;", source,
                "Every entry point must be wired to the same Open() method, not separate per-screen logic.");
        }

        [Test]
        public void Modal_IsFoundByASingleSharedName()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_modal = root.Q<VisualElement>(\"shared-settings-modal\");", source);
        }

        [Test]
        public void Sliders_TwoWayBindToTheExistingSharedAudioSettingsStore()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("using Mikey.UI.Audio;", source);
            StringAssert.Contains("_audioSettings = GetComponent<IAudioSettings>();", source,
                "Must reuse the existing shared IAudioSettings instance — never a new/separate store.");
            StringAssert.Contains("root.Q<Slider>(\"shared-settings-music\");", source);
            StringAssert.Contains("root.Q<Slider>(\"shared-settings-sfx\");", source);
            StringAssert.Contains("root.Q<Slider>(\"shared-settings-trainer\");", source);
            StringAssert.Contains("s.MusicVolume = v", source);
            StringAssert.Contains("s.SfxVolume = v", source);
            StringAssert.Contains("s.TrainerVoiceVolume = v", source);
        }

        [Test]
        public void NoSeparatePlayerPrefsOrStateIsCreated()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("PlayerPrefs", source,
                "Must go through the existing IAudioSettings store, never touch PlayerPrefs directly.");
        }

        [Test]
        public void NeverReferencesScreenNavigation_SoCloseCanNeverChangeScreens()
        {
            // The whole point of Close() being a pure "remove one CSS class"
            // operation: with no IScreenNavigator/Show() anywhere in this
            // file, closing the modal cannot navigate away from whatever
            // screen was already showing underneath it.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("IScreenNavigator", source);
            StringAssert.DoesNotContain(".Show(", source);
        }

        [Test]
        public void NeverReferencesMapPanZoomOrMapNavigationState_SoCloseCanNeverResetTheMapCamera()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("MapPanZoomController", source);
            StringAssert.DoesNotContain("MapNavigationState", source);
            StringAssert.DoesNotContain("MapContext", source);
        }

        [Test]
        public void Close_IsExactlyRemovingTheOpenClass_NothingElse()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains(
                "public void Close() => _modal?.RemoveFromClassList(OpenClass);",
                source);
        }

        [Test]
        public void Open_IsExactlyAddingTheOpenClass_NothingElse()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains(
                "public void Open() => _modal?.AddToClassList(OpenClass);",
                source);
        }

        [Test]
        public void OnDisable_UnwiresAllThreeOpenButtonsAndSliders_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_openButtons[i].clicked -= Open;", source);
            StringAssert.Contains("_closeButton.clicked -= Close;", source);
            StringAssert.Contains("_musicSlider.UnregisterValueChangedCallback(_musicChangedCallback);", source);
            StringAssert.Contains("_sfxSlider.UnregisterValueChangedCallback(_sfxChangedCallback);", source);
            StringAssert.Contains("_trainerVoiceSlider.UnregisterValueChangedCallback(_trainerVoiceChangedCallback);", source);
        }

        [Test]
        public void StartsClosed_OnBind()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("Close();", source);
        }
    }
}
