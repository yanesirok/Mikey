using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Intro.Tests
{
    /// <summary>
    /// Contract for Lore's cinematic exit: both "lore-skip" and "lore-continue"
    /// (deliberately not "go-menu" — see IntroScreenUxmlTests) darken Lore to
    /// black through the shared transition overlay, only then navigate to
    /// "menu", then fade Main Menu in — never an instant hard cut. Verified by
    /// reading the source, mirroring TitleControllerSourceTests /
    /// TitleControllerSceneTests for MonoBehaviour internals not practical to
    /// drive through a live panel in EditMode.
    /// </summary>
    public class LoreExitControllerTests
    {
        private const string SourcePath = "Assets/UI/Intro/LoreExitController.cs";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void NextScreen_IsMenu()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const string NextScreenId = \"menu\";", source);
        }

        [Test]
        public void BindsToLoreSkipAndLoreContinue_NotGoMenu()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const string SkipButtonName = \"lore-skip\";", source);
            StringAssert.Contains("private const string ContinueButtonName = \"lore-continue\";", source);
            StringAssert.DoesNotContain("root.Q<Button>(\"go-menu\")", source,
                "Lore's exit buttons must not be looked up by the name 'go-menu' — that would collide with ScreenManager's own auto-wiring, which fires its instant Show() first, before this controller ever gets a chance to darken Lore.");
        }

        [Test]
        public void BothButtons_ShareTheSameExitHandler()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_skipButton.clicked += OnExitClicked;", source);
            StringAssert.Contains("_continueButton.clicked += OnExitClicked;", source,
                "Skip and Continue must trigger the identical cinematic exit, not two different transitions.");
        }

        [Test]
        public void OnExitClicked_GuardsAgainstDoubleNavigation()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_exitRoutine != null || _navigator == null)", source,
                "A second click (e.g. Skip then Continue) mid-transition must not start a second exit routine.");
        }

        [Test]
        public void ExitRoutine_FadesToBlack_HoldsOnFullBlack_ThenNavigates_ThenFadesIn()
        {
            string source = File.ReadAllText(SourcePath);
            int routineIndex = source.IndexOf("private IEnumerator ExitRoutine()", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(routineIndex, 0, "Expected an ExitRoutine() method.");

            int fadeToBlackIndex = source.IndexOf("_overlay.FadeToBlack(FadeOutSeconds)", routineIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(fadeToBlackIndex, 0, "Phase A: Lore must fade to black through the shared transition overlay before navigating.");

            int blackHoldIndex = source.IndexOf("WaitForSecondsRealtime(BlackHoldSeconds)", fadeToBlackIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(blackHoldIndex, 0, "The screen must hold on full black before Main Menu is activated.");

            int showIndex = source.IndexOf("_navigator.Show(NextScreenId);", blackHoldIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(showIndex, 0, "Main Menu must be activated only after the fade-to-black and hold, while fully covered.");

            int fadeFromBlackIndex = source.IndexOf("_overlay.FadeFromBlack(FadeInSeconds)", showIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(fadeFromBlackIndex, 0, "Phase B: Main Menu must fade in from black only after the screen swap — a real alpha 1 -> 0 reveal, never an instant bright cut.");
        }

        [Test]
        public void TransitionTiming_MatchesTheApprovedDesignBrief()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float FadeOutSeconds = 0.5f;", source);
            StringAssert.Contains("private const float BlackHoldSeconds = 0.12f;", source);
            StringAssert.Contains("private const float FadeInSeconds = 0.72f;", source);
        }

        [Test]
        public void OnDisable_UnbindsButtonsAndStopsInFlightRoutines_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_skipButton.clicked -= OnExitClicked;", source);
            StringAssert.Contains("_continueButton.clicked -= OnExitClicked;", source);
            StringAssert.Contains("StopCoroutine(_exitRoutine);", source);
        }

        private static GameObject OpenSceneAndFindUi()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), $"Could not open {ScenePath}");

            GameObject ui = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == "UI")
                {
                    ui = go;
                    break;
                }
            }
            Assert.IsNotNull(ui, "Scene must contain a root GameObject named 'UI'.");
            return ui;
        }

        [Test]
        public void UiGameObject_HasLoreExitController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent<LoreExitController>(),
                "UI GameObject must have a LoreExitController for Lore's cinematic exit to run in a real build.");
        }
    }
}
