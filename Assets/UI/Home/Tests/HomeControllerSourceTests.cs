using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Contract for the rebuilt HomeController's wiring: PLANS opens a local
    /// overlay (not ScreenManager navigation; SETTINGS is no longer
    /// HomeController's concern at all — see
    /// Mikey.UI.Settings.SettingsModalController), QUIT's platform-specific
    /// behavior (Editor no-op, real Application.Quit() everywhere else —
    /// never platform-hidden), re-entering Main Menu resets any modal left
    /// open, handlers are unbound on disable (no duplicate-subscription
    /// leak), and none of the old progression-dashboard wiring (dynamic CTA,
    /// Map/Techniques locking, dev bar) remains. Verified by reading the
    /// source, mirroring IntroControllerTests/TitleControllerSourceTests for
    /// MonoBehaviour internals not practical to drive through a live panel in
    /// EditMode.
    /// </summary>
    public class HomeControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Home/HomeController.cs";

        [Test]
        public void Plans_OpensLocalOverlay_ViaDisplayStyle()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ShowModal(_plansModal)", source);
            StringAssert.Contains("HideModal(_plansModal)", source);
            StringAssert.Contains("modal.style.display = DisplayStyle.Flex;", source);
            StringAssert.Contains("modal.style.display = DisplayStyle.None;", source);
        }

        [Test]
        public void Play_HasNoControllerCode_ScreenManagerHandlesItAlone()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("go-map", source,
                "PLAY is a plain ScreenManager 'go-map' navigator — HomeController must not special-case it.");
        }

        [Test]
        public void NoLongerOwnsSettings_TheSharedModalControllerDoesInstead()
        {
            // Settings is unified: HomeController must not reference the old
            // per-screen modal/sliders/audio-settings binding at all anymore.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("_settingsModal", source);
            StringAssert.DoesNotContain("IAudioSettings", source);
            StringAssert.DoesNotContain("WireSlider", source);
        }

        [Test]
        public void Quit_LogsInEditor_AndCallsApplicationQuitEverywhereElse()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnQuitClicked()", source);
            StringAssert.Contains("#if UNITY_EDITOR", source);
            StringAssert.Contains("Debug.Log(", source);
            StringAssert.Contains("Application.Quit();", source);
        }

        [Test]
        public void ReenteringMenu_ResetsAnyOpenModal()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenEntered(string screenId)", source);
            StringAssert.Contains("if (screenId != ScreenId)", source);
        }

        [Test]
        public void OnDisable_UnbindsButtons_AndUnsubscribesScreenChanged_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_buttonBindings[i].Unbind();", source);
            StringAssert.Contains("_buttonBindings.Clear();", source);
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenEntered;", source);
        }

        [Test]
        public void OldProgressionDashboardWiring_IsGone()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("TutorialProgressPresenter", source,
                "The dynamic progression-driven CTA is retired with the old Home dashboard.");
            StringAssert.DoesNotContain("ITutorialProgress", source,
                "HomeController no longer depends on tutorial progression state.");
            StringAssert.DoesNotContain("home-devbar", source,
                "The dev-only progression switcher is retired with the old Home dashboard.");
            StringAssert.DoesNotContain("home-nav-map", source);
            StringAssert.DoesNotContain("home-nav-techniques", source);
        }
    }
}
