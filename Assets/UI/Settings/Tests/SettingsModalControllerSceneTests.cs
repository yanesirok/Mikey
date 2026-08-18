using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Settings.Tests
{
    /// <summary>
    /// Scene-wiring contract for SettingsModalController: the "UI" GameObject
    /// carries exactly one instance (replacing HomeController's old inline
    /// Settings wiring and Map's old MapSettingsModalBinder usage), so the
    /// one shared modal actually runs in a real build.
    /// </summary>
    public class SettingsModalControllerSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void UiGameObject_HasExactlyOneSettingsModalController()
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

            var controllers = ui.GetComponents<SettingsModalController>();
            Assert.AreEqual(1, controllers.Length,
                "Expected exactly one SettingsModalController — the one shared Settings modal.");
        }
    }
}
