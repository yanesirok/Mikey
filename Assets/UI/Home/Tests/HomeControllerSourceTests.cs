using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Contract for HomeController's wiring: the CTA/lock tabs are driven by
    /// progression state rather than ScreenManager's static "go-" convention, the
    /// dev-only progression switcher is gated to the Editor/Development Build
    /// (absent from production builds, mirroring CombineScreenController's
    /// dev-switcher gate), and handlers/subscriptions are unbound on disable (no
    /// duplicate-subscription leak). Verified by reading the source, mirroring
    /// MapLevelPreviewControllerTests' established technique for MonoBehaviour
    /// internals not practical to drive through a live panel in EditMode.
    /// </summary>
    public class HomeControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Home/HomeController.cs";

        [Test]
        public void DevBar_IsGatedToEditorOrDevelopmentBuild()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("#if UNITY_EDITOR || DEVELOPMENT_BUILD", source,
                "HomeController must gate the dev-only progression switcher behind UNITY_EDITOR || DEVELOPMENT_BUILD.");
            StringAssert.Contains("_devBar.style.display = allowed ? DisplayStyle.Flex : DisplayStyle.None;", source,
                "The dev bar must be hidden (not just left in markup) when not allowed.");
        }

        [Test]
        public void DevControls_CoverExactlyTheSpecifiedReachableStates_NoVowSimulation()
        {
            string source = File.ReadAllText(SourcePath);
            foreach (var expected in new[]
            {
                "_progress.Reset()",
                "TutorialProgressState.NewPlayer",
                "TutorialProgressState.CombineStarted",
                "TutorialProgressState.Level1Unlocked",
                "TutorialProgressState.LessonStarted",
                "TutorialProgressState.LessonCompleted",
            })
            {
                StringAssert.Contains(expected, source, $"Expected dev control wiring for '{expected}'.");
            }

            StringAssert.DoesNotContain("ThirtyDayStreakCompleted", source,
                "No 30-day Vow simulation dev control yet.");
            StringAssert.DoesNotContain("VowActivated", source,
                "No Vow simulation dev control yet.");
            StringAssert.DoesNotContain("VowAvailable", source,
                "No Vow simulation dev control yet.");
        }

        [Test]
        public void CtaClick_RecomputesDestinationFromCurrentState_AtClickTime()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnCtaClicked()", source);
            StringAssert.Contains("TutorialProgressPresenter.HomeCtaFor(_progress.State)", source,
                "The destination must be recomputed from the current state at click time, never a stale captured value.");
        }

        [Test]
        public void LockedTabs_DoNotNavigateOnClick()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnNavMapClicked()", source);
            StringAssert.Contains("if (!TutorialProgressPresenter.IsMapUnlocked(_progress.State))", source);
            StringAssert.Contains("private void OnNavTechniquesClicked()", source);
            StringAssert.Contains("if (!TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))", source);
        }

        [Test]
        public void OnDisable_UnbindsDevButtons_AndUnsubscribesProgressChanged_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_devButtonBindings[i].Unbind();", source);
            StringAssert.Contains("_devButtonBindings.Clear();", source);
            StringAssert.Contains("_progress.Changed -= Render;", source);
        }
    }
}
