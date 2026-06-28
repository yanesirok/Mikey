using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Validates that SampleScene wires SafeAreaController onto the UI GameObject
    /// alongside the existing UIDocument and ScreenManager, with no missing scripts,
    /// and that the Combine vertical slice is wired in: CombineScreenController is
    /// attached to the same UI GameObject, while ScreenManager uses the production
    /// start screen ("title"). This is the automated guard for the scene wiring.
    /// </summary>
    public class SceneWiringTests
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
        public void UiGameObject_HasRequiredComponents_AndNoMissingScripts()
        {
            GameObject ui = OpenSceneAndFindUi();

            // SafeAreaController + UIDocument are reachable as types (asm references the runtime asm).
            Assert.IsNotNull(ui.GetComponent<UIDocument>(), "UI GameObject must have a UIDocument.");
            Assert.IsNotNull(ui.GetComponent<SafeAreaController>(), "UI GameObject must have a SafeAreaController.");
            // ScreenManager lives in Assembly-CSharp, which an asmdef cannot reference: look it up by name.
            Assert.IsNotNull(ui.GetComponent("ScreenManager"), "UI GameObject must have a ScreenManager.");

            foreach (var component in ui.GetComponents<Component>())
                Assert.IsNotNull(component, "UI GameObject has a missing-script component (null).");
        }

        [Test]
        public void UiGameObject_HasCombineScreenController()
        {
            GameObject ui = OpenSceneAndFindUi();

            // CombineScreenController lives in Mikey.UI.Combine, which this test asm
            // does not reference: look it up by name (same approach as ScreenManager).
            Assert.IsNotNull(ui.GetComponent("CombineScreenController"),
                "UI GameObject must have a CombineScreenController (Combine slice wiring).");
        }

        [Test]
        public void ScreenManager_StartScreen_IsTitle()
        {
            GameObject ui = OpenSceneAndFindUi();

            var screenManager = ui.GetComponent("ScreenManager");
            Assert.IsNotNull(screenManager, "UI GameObject must have a ScreenManager.");

            var serialized = new SerializedObject(screenManager);
            SerializedProperty startScreen = serialized.FindProperty("startScreen");
            Assert.IsNotNull(startScreen, "ScreenManager must expose a serialized 'startScreen'.");
            Assert.AreEqual("title", startScreen.stringValue,
                "Production start screen must be 'title' (the consolidated entry screen; Splash was removed).");
        }
    }
}
