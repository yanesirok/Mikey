using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Title.Tests
{
    /// <summary>
    /// Scene-wiring contract for TitleController: the "UI" GameObject carries it,
    /// so the Logo Intro's timer/tap-skip behavior actually runs in a real build.
    /// Mirrors IntroControllerTests' UiGameObject_HasIntroController.
    /// </summary>
    public class TitleControllerSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void UiGameObject_HasTitleController()
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
            Assert.IsNotNull(ui.GetComponent<TitleController>(),
                "UI GameObject must have a TitleController for the Logo Intro timer/tap-skip to run in a real build.");
        }
    }
}
