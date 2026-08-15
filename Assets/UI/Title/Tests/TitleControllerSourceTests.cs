using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Title.Tests
{
    /// <summary>
    /// Contract for TitleController's wiring: it advances from Logo Intro to Lore
    /// on a timer within the ~2.5-3s spec range and on a tap/click anywhere, a
    /// single guard makes sure only one of those two triggers ever navigates (no
    /// double navigation if both occur together), and it drives navigation itself
    /// (unlike every "go-" screen, Title has no button). Verified by reading the
    /// source, mirroring HomeControllerSourceTests / IntroControllerTests for
    /// MonoBehaviour internals not practical to drive through a live panel in
    /// EditMode.
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
        public void AutoAdvanceDelay_IsWithinTwoPointFiveToThreeSeconds()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("[Range(2.5f, 3f)]", source,
                "The auto-advance delay must be constrained to the 2.5-3s spec range.");
            StringAssert.Contains("autoAdvanceSeconds = 2.75f", source);
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
                "Both the timer and the tap handler must funnel through the same guarded Advance().");
        }

        [Test]
        public void Controller_DrivesNavigationItself_UnlikeGoNavigators()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_navigator.Show(NextScreenId);", source,
                "Title has no button; unlike passive controllers (e.g. IntroController), it must actively navigate.");
        }

        [Test]
        public void OnDisable_StopsTimerAndUnregistersTapCallback_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("StopCoroutine(_timerRoutine);", source);
            StringAssert.Contains("_titleScreen.UnregisterCallback(_tapCallback);", source);
        }
    }
}
