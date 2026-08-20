using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Contract for the launch shell's one shared transition overlay: it fades
    /// a full-bleed black element between fully transparent and fully opaque,
    /// blocking input only while actively covering the screen — so it never
    /// interferes with normal UI once a transition completes — and it lives on
    /// the "UI" GameObject so TitleController/LoreExitController can reach it
    /// via <see cref="ITransitionOverlay"/>. TransitionOverlayController lives
    /// in Assembly-CSharp (like ScreenManager/BackgroundMediaController), which
    /// this test assembly cannot reference directly, so wiring is verified by
    /// by-name GetComponent lookups, mirroring BackgroundMediaControllerSceneTests;
    /// internal behavior is verified by reading the source, mirroring
    /// TitleControllerSourceTests.
    /// </summary>
    public class TransitionOverlayControllerTests
    {
        private const string SourcePath = "Assets/UI/TransitionOverlayController.cs";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void ImplementsITransitionOverlay()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("class TransitionOverlayController : MonoBehaviour, ITransitionOverlay", source);
        }

        [Test]
        public void FadeToBlack_AnimatesTowardFullyOpaque()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public IEnumerator FadeToBlack(float seconds) => Fade(1f, seconds);", source);
        }

        [Test]
        public void FadeFromBlack_AnimatesTowardFullyTransparent()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public IEnumerator FadeFromBlack(float seconds) => Fade(0f, seconds);", source);
        }

        [Test]
        public void Fade_BlocksInputWhileCovering_ButGoesClickThroughOnceFullyTransparent()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_overlay.pickingMode = PickingMode.Position;", source,
                "The overlay must block input while it is covering the screen (mid-fade or fully opaque).");
            StringAssert.Contains("_overlay.pickingMode = targetOpacity <= 0f ? PickingMode.Ignore : PickingMode.Position;", source,
                "Once a transition completes fully transparent, the overlay must go click-through so it never interferes with normal UI.");
        }

        [Test]
        public void Fade_UsesSmoothstepEasing_NoBounce()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("t = t * t * (3f - 2f * t); // smoothstep", source,
                "The launch shell's design brief calls for restrained, no-bounce easing.");
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
        public void UiGameObject_HasTransitionOverlayController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent("TransitionOverlayController"),
                "UI GameObject must have a TransitionOverlayController for the launch shell's fades to run in a real build.");
        }
    }
}
