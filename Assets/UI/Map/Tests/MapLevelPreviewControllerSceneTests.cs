using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mikey.UI.Map;
using Mikey.UI.Progression;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Scene-wiring contract for the rebuilt Map: the "UI" GameObject carries a
    /// MapLevelPreviewController with exactly two checkpoint bindings — LVL 0
    /// (the Combine assessment, always unlocked, routing to 'combineIntro') and
    /// LVL 1 (Techniques, locked until Level1Unlocked, routing to 'techniques',
    /// reusing the existing Okinawa preview clip) — so neither checkpoint
    /// silently falls through to an unbound/misrouted state. Mirrors
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
                "UI GameObject must have a MapLevelPreviewController (Map hotspot/panel/top-bar wiring).");
        }

        [Test]
        public void Checkpoints_ContainExactlyLvl0AndLvl1_WithCorrectRoutingAndUnlockState()
        {
            GameObject ui = OpenSceneAndFindUi();
            var controller = ui.GetComponent<MapLevelPreviewController>();
            Assert.IsNotNull(controller, "Expected a MapLevelPreviewController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty checkpoints = serialized.FindProperty("checkpoints");
            Assert.IsNotNull(checkpoints, "MapLevelPreviewController must expose a serialized 'checkpoints' array.");
            Assert.AreEqual(2, checkpoints.arraySize, "Expected exactly two checkpoint bindings (LVL 0, LVL 1).");

            SerializedProperty lvl0 = FindByNodeName(checkpoints, "map-node-lvl0");
            Assert.IsNotNull(lvl0, "Expected an 'map-node-lvl0' checkpoint binding.");
            Assert.AreEqual("map-detail-content-lvl0", lvl0.FindPropertyRelative("contentElementName").stringValue);
            Assert.AreEqual("map-detail-start-lvl0", lvl0.FindPropertyRelative("ctaElementName").stringValue);
            Assert.AreEqual("combineIntro", lvl0.FindPropertyRelative("navigationTarget").stringValue,
                "LVL 0 must route to the existing combineIntro briefing (restoring the assessment entry point).");
            Assert.AreEqual((int)TutorialProgressState.NewPlayer, lvl0.FindPropertyRelative("requiredState").enumValueIndex,
                "LVL 0 must always be unlocked (NewPlayer requirement).");

            SerializedProperty lvl1 = FindByNodeName(checkpoints, "map-node-lvl1");
            Assert.IsNotNull(lvl1, "Expected an 'map-node-lvl1' checkpoint binding.");
            Assert.AreEqual("map-detail-content-lvl1", lvl1.FindPropertyRelative("contentElementName").stringValue);
            Assert.AreEqual("map-detail-start-lvl1", lvl1.FindPropertyRelative("ctaElementName").stringValue);
            Assert.AreEqual("techniques", lvl1.FindPropertyRelative("navigationTarget").stringValue,
                "LVL 1 must route to the existing Techniques hub.");
            Assert.AreEqual((int)TutorialProgressState.Level1Unlocked, lvl1.FindPropertyRelative("requiredState").enumValueIndex,
                "LVL 1 must require Level1Unlocked.");
            Assert.IsNotNull(lvl1.FindPropertyRelative("previewClip").objectReferenceValue,
                "LVL 1 must reuse the existing Okinawa preview clip (japan_okinawa_preview_loop.mp4).");
        }

        private static SerializedProperty FindByNodeName(SerializedProperty array, string nodeElementName)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("nodeElementName").stringValue == nodeElementName)
                    return element;
            }
            return null;
        }
    }
}
