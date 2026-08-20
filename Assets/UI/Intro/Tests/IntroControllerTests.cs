using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mikey.UI.Progression;

namespace Mikey.UI.Intro.Tests
{
    /// <summary>
    /// Contract for the Intro-completion progression fix: leaving Intro through its
    /// normal flow (Skip or the primary CTA — now "lore-skip"/"lore-continue",
    /// driven by LoreExitController's cinematic transition, see
    /// LoreExitControllerTests / IntroScreenUxmlTests) must mark
    /// <see cref="TutorialProgressState.IntroCompleted"/>, using the forward-only
    /// Advance so a player already further along is never regressed. This
    /// controller only observes whichever navigation already happened — it does
    /// not care who drove it. Pure decision logic is unit-tested directly; the
    /// MonoBehaviour wiring (not practical to drive through a live panel in
    /// EditMode) is verified by reading the source, mirroring
    /// CameraTestProgressionTests / HomeControllerSourceTests.
    /// </summary>
    public class IntroControllerTests
    {
        private const string SourcePath = "Assets/UI/Intro/IntroController.cs";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void IsIntroExit_TrueOnlyWhenLeavingIntroForAnotherScreen()
        {
            Assert.IsTrue(IntroController.IsIntroExit("intro", "menu"),
                "Leaving Intro for Home (Skip or primary CTA) must count as completing/skipping Intro.");
            Assert.IsTrue(IntroController.IsIntroExit("intro", "title"),
                "Leaving Intro for any other screen still counts as an exit.");
        }

        [Test]
        public void IsIntroExit_FalseWhenNotLeavingIntro()
        {
            Assert.IsFalse(IntroController.IsIntroExit("menu", "intro"),
                "Entering Intro is not exiting it.");
            Assert.IsFalse(IntroController.IsIntroExit("title", "menu"),
                "A transition that never involved Intro must not count as an Intro exit.");
            Assert.IsFalse(IntroController.IsIntroExit("intro", "intro"),
                "Re-requesting the same screen is not a genuine exit.");
            Assert.IsFalse(IntroController.IsIntroExit(null, "menu"),
                "No previous screen (first-ever navigation) must not count as an Intro exit.");
        }

        [Test]
        public void OnScreenChanged_AdvancesIntroCompleted_OnExit()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenChanged(string screenId)", source);
            StringAssert.Contains("if (IsIntroExit(_previousScreen, screenId))", source);
            StringAssert.Contains("_progress?.Advance(TutorialProgressState.IntroCompleted);", source);
        }

        [Test]
        public void IntroCompletion_UsesForwardOnlyAdvance_NeverSetState()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("_progress?.SetState(TutorialProgressState.IntroCompleted)", source,
                "Intro completion must use the forward-only Advance, never SetState, so a player already beyond IntroCompleted is never regressed.");
            StringAssert.DoesNotContain("_progress.SetState(TutorialProgressState.IntroCompleted)", source,
                "Intro completion must use the forward-only Advance, never SetState, so a player already beyond IntroCompleted is never regressed.");
        }

        [Test]
        public void Navigation_IsUntouched_ControllerNeverCallsShow()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain(".Show(", source,
                "IntroController must only observe navigation (a progression side-effect), never drive it — LoreExitController owns Lore's cinematic exit navigation instead.");
        }

        [Test]
        public void OnDisable_UnsubscribesFromScreenChanged_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenChanged;", source);
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
        public void UiGameObject_HasIntroController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent<IntroController>(),
                "UI GameObject must have an IntroController for the Intro-completion progression fix to run in a real build.");
        }
    }
}
