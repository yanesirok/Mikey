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
        public void UiGameObject_HasLevel0Station_AndItsPoseSource()
        {
            GameObject ui = OpenSceneAndFindUi();

            // Both live in assemblies this test asm does not reference: look them up by
            // name (same approach as ScreenManager). The station is useless without a
            // PoseController on the same object — it is what feeds the analyzers.
            Assert.IsNotNull(ui.GetComponent("Level0SessionController"),
                "UI GameObject must have a Level0SessionController (level-0 station wiring).");
            Assert.IsNotNull(ui.GetComponent("PoseController"),
                "UI GameObject must have a PoseController — the level-0 station's pose input.");
        }

        [Test]
        public void UiGameObject_HasPracticeController()
        {
            GameObject ui = OpenSceneAndFindUi();

            // PracticeController lives in Mikey.UI.Practice, which this test asm does
            // not reference: look it up by name (same approach as ScreenManager).
            Assert.IsNotNull(ui.GetComponent("PracticeController"),
                "UI GameObject must have a PracticeController (Practice vertical-slice wiring).");
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

        [Test]
        public void UiGameObject_HasBackendSyncAndAccountPanel()
        {
            string scene = System.IO.File.ReadAllText("Assets/Scenes/SampleScene.unity");
            StringAssert.Contains("Mikey.Backend.SyncService", scene,
                "На GameObject UI нет SyncService — синхронизация не запустится.");
            StringAssert.Contains("Mikey.Backend.AccountPanelController", scene,
                "На GameObject UI нет AccountPanelController — блок аккаунта не оживёт.");
        }
    }
}
