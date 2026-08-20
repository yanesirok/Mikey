using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Guards specifically against the regression where ProfileDetailsController
    /// compiled cleanly and every source-text/UXML-structure test passed, yet the
    /// component was never actually attached to the production "UI" GameObject in
    /// SampleScene.unity — so Gender selection and Save silently did nothing at
    /// runtime while the native TextFields kept working (they don't depend on the
    /// controller). Mirrors SceneWiringTests / MapControllersSceneTests'
    /// established "open the real scene, check the real component" pattern.
    /// </summary>
    public class ProfileDetailsSceneWiringTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        private static GameObject OpenSceneAndFindUi()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), $"Could not open {ScenePath}");

            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == "UI")
                    return go;
            }
            Assert.Fail("Scene must contain a root GameObject named 'UI'.");
            return null;
        }

        [Test]
        public void UiGameObject_HasAnEnabledProfileDetailsController()
        {
            GameObject ui = OpenSceneAndFindUi();

            var controller = ui.GetComponent<ProfileDetailsController>();
            Assert.IsNotNull(controller,
                "UI GameObject must have a ProfileDetailsController, or Gender selection and Save silently do nothing at runtime.");
            Assert.IsTrue(controller.enabled, "ProfileDetailsController must be enabled, not just present.");
            Assert.IsTrue(ui.activeInHierarchy, "The UI GameObject carrying it must itself be active.");
        }

        [Test]
        public void ProfileDetailsController_IsOnTheSameGameObject_AsTheProductionUIDocumentAndScreenManager()
        {
            GameObject ui = OpenSceneAndFindUi();

            var controller = ui.GetComponent<ProfileDetailsController>();
            Assert.IsNotNull(controller);

            var document = ui.GetComponent<UIDocument>();
            Assert.IsNotNull(document, "UI GameObject must have the production UIDocument.");
            Assert.AreSame(ui, document.gameObject,
                "ProfileDetailsController must live on the SAME GameObject as the production UIDocument, not a second/parallel UI root.");

            // ScreenManager lives in Assembly-CSharp, which this asm can't reference: look it up by name (same approach as SceneWiringTests).
            var screenManager = ui.GetComponent("ScreenManager");
            Assert.IsNotNull(screenManager, "UI GameObject must have a ScreenManager.");
        }

        [Test]
        public void UiGameObject_StillHasNoMissingScriptComponents()
        {
            GameObject ui = OpenSceneAndFindUi();

            foreach (var component in ui.GetComponents<Component>())
                Assert.IsNotNull(component, "UI GameObject has a missing-script component (null) after wiring ProfileDetailsController in.");
        }
    }
}
