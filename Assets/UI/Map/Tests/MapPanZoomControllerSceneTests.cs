using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Scene-wiring contract for MapPanZoomController: the "UI" GameObject carries
    /// it, so pan/zoom actually runs in a real build. Mirrors
    /// MapLevelPreviewControllerSceneTests.UiGameObject_HasMapLevelPreviewController.
    /// </summary>
    public class MapPanZoomControllerSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void UiGameObject_HasMapPanZoomController()
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
            Assert.IsNotNull(ui.GetComponent<MapPanZoomController>(),
                "UI GameObject must have a MapPanZoomController for Map's pan/zoom to run in a real build.");
        }
    }
}
