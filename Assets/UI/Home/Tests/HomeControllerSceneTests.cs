using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mikey.UI.Progression;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Scene-wiring contract for HomeController: the "UI" GameObject carries it
    /// alongside a TutorialProgressionController (the single canonical progression
    /// source it and every other screen controller consume via
    /// GetComponent&lt;ITutorialProgress&gt;()). Mirrors
    /// MapLevelPreviewControllerSceneTests / SceneWiringTests. This test assembly
    /// references Mikey.UI.Home and Mikey.UI.Progression directly, so both
    /// components are looked up by type.
    /// </summary>
    public class HomeControllerSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

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
        public void UiGameObject_HasHomeController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent<HomeController>(),
                "UI GameObject must have a HomeController (Home progression-CTA/lock wiring).");
        }

        [Test]
        public void UiGameObject_HasTutorialProgressionController()
        {
            GameObject ui = OpenSceneAndFindUi();
            var controller = ui.GetComponent<TutorialProgressionController>();
            Assert.IsNotNull(controller,
                "UI GameObject must have a TutorialProgressionController (the single canonical progression source).");
            Assert.IsInstanceOf<ITutorialProgress>(controller,
                "TutorialProgressionController must implement ITutorialProgress so every screen controller can consume it.");
        }
    }
}
