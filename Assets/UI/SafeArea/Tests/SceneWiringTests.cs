using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Validates that SampleScene wires SafeAreaController onto the UI GameObject
    /// alongside the existing UIDocument and ScreenManager, with no missing scripts.
    /// This is the automated guard for the hand-edited scene YAML.
    /// </summary>
    public class SceneWiringTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void UiGameObject_HasRequiredComponents_AndNoMissingScripts()
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

            // SafeAreaController + UIDocument are reachable as types (asm references the runtime asm).
            Assert.IsNotNull(ui.GetComponent<UIDocument>(), "UI GameObject must have a UIDocument.");
            Assert.IsNotNull(ui.GetComponent<SafeAreaController>(), "UI GameObject must have a SafeAreaController.");
            // ScreenManager lives in Assembly-CSharp, which an asmdef cannot reference: look it up by name.
            Assert.IsNotNull(ui.GetComponent("ScreenManager"), "UI GameObject must have a ScreenManager.");

            foreach (var component in ui.GetComponents<Component>())
                Assert.IsNotNull(component, "UI GameObject has a missing-script component (null).");
        }
    }
}
