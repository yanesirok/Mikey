using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Scene-wiring contract for the rebuilt Map flow: the "UI" GameObject
    /// carries JapanMapController, OkinawaMapController, and (Map Pass 3B)
    /// MapCloudTransitionController (replacing the retired
    /// MapLevelPreviewController), and the Okinawa preview clip is still
    /// wired so the chapter panel's video isn't silently broken.
    /// </summary>
    public class MapControllersSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        private static GameObject OpenUiGameObject()
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
        public void UiGameObject_HasJapanMapController()
        {
            GameObject ui = OpenUiGameObject();
            Assert.IsNotNull(ui.GetComponent<JapanMapController>(),
                "UI GameObject must have a JapanMapController for the Japan world map to run in a real build.");
        }

        [Test]
        public void UiGameObject_HasOkinawaMapController()
        {
            GameObject ui = OpenUiGameObject();
            Assert.IsNotNull(ui.GetComponent<OkinawaMapController>(),
                "UI GameObject must have an OkinawaMapController for the Okinawa chapter map to run in a real build.");
        }

        [Test]
        public void UiGameObject_HasMapCloudTransitionController()
        {
            GameObject ui = OpenUiGameObject();
            Assert.IsNotNull(ui.GetComponent<MapCloudTransitionController>(),
                "UI GameObject must have a MapCloudTransitionController for the Map Pass 3B cloud transition to run in a real build.");
        }

        [Test]
        public void UiGameObject_DoesNotHaveTheRetiredMapLevelPreviewController()
        {
            GameObject ui = OpenUiGameObject();
            var components = ui.GetComponents<MonoBehaviour>();
            foreach (var component in components)
            {
                Assert.IsFalse(component == null, "UI GameObject must not carry a missing-script component (the retired MapLevelPreviewController reference must have been removed, not left dangling).");
            }
        }

        [Test]
        public void JapanMapController_HasOkinawaPreviewClipWired()
        {
            GameObject ui = OpenUiGameObject();
            var controller = ui.GetComponent<JapanMapController>();
            Assert.IsNotNull(controller);

            var so = new SerializedObject(controller);
            var clipProp = so.FindProperty("okinawaPreviewClip");
            Assert.IsNotNull(clipProp);
            Assert.IsNotNull(clipProp.objectReferenceValue as VideoClip,
                "JapanMapController must have the Okinawa preview VideoClip wired, or the chapter panel's video will always fall back.");
        }
    }
}
