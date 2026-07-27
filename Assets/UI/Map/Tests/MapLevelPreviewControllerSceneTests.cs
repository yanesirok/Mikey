using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mikey.UI.Map;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Scene-wiring contract for the Okinawa map level preview: the "UI"
    /// GameObject carries a MapLevelPreviewController with exactly one checkpoint
    /// binding — Okinawa's node, pointing at a real (non-null) preview clip — so
    /// the panel never silently falls through to an unbound checkpoint. Mirrors
    /// BackgroundMediaControllerSceneTests / SceneWiringTests. Unlike
    /// BackgroundMediaController (Assembly-CSharp), MapLevelPreviewController
    /// lives in Mikey.UI.Map, which this test assembly references directly, so
    /// the component itself is looked up by type rather than by name.
    /// </summary>
    public class MapLevelPreviewControllerSceneTests
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
        public void UiGameObject_HasMapLevelPreviewController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent<MapLevelPreviewController>(),
                "UI GameObject must have a MapLevelPreviewController (Map level-panel wiring).");
        }

        [Test]
        public void Checkpoints_ContainExactlyOkinawa_WithNodeNameAndPreviewClip()
        {
            GameObject ui = OpenSceneAndFindUi();
            var controller = ui.GetComponent<MapLevelPreviewController>();
            Assert.IsNotNull(controller, "Expected a MapLevelPreviewController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty checkpoints = serialized.FindProperty("checkpoints");
            Assert.IsNotNull(checkpoints, "MapLevelPreviewController must expose a serialized 'checkpoints' array.");
            Assert.AreEqual(1, checkpoints.arraySize, "Expected exactly one checkpoint binding (Okinawa) for now.");

            SerializedProperty okinawa = checkpoints.GetArrayElementAtIndex(0);
            SerializedProperty nodeElementName = okinawa.FindPropertyRelative("nodeElementName");
            SerializedProperty previewClip = okinawa.FindPropertyRelative("previewClip");

            Assert.AreEqual("map-node-okinawa", nodeElementName.stringValue,
                "The Okinawa binding must target the 'map-node-okinawa' checkpoint node.");
            Assert.IsNotNull(previewClip.objectReferenceValue,
                "The Okinawa binding must carry a real preview video clip (no silently-unbound checkpoint).");
        }
    }
}
