using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Media.Tests
{
    /// <summary>
    /// Scene-wiring contract for the background media batch: the "UI" GameObject
    /// carries a BackgroundMediaController with exactly two screen bindings
    /// (menu/combine), each pointing at the bg-media element added to that
    /// screen's full-bleed background layer, each carrying a real video clip, so
    /// no screen silently falls through to an unbound player. The Main Menu
    /// binding specifically must reference the supplied main_menu_loop.mp4 asset.
    /// Logo Intro ("title") and Map ("map") deliberately have no binding here:
    /// Title is a static near-black background + the centered logo (see
    /// Assets/UI/Title/Title.uss); Map's reused ink-wash artwork is now part of
    /// its own pannable ".map-canvas" (a direct USS reference, see
    /// Assets/UI/Map/Map.uss ".map-canvas-art"), not an externally-assigned
    /// full-bleed target — BackgroundMediaController only ever managed a video
    /// lifecycle or a single static image, neither of which fits a pannable/
    /// zoomable layer. Uses by-name/SerializedObject lookups, mirroring
    /// SceneWiringTests, because BackgroundMediaController lives in
    /// Assembly-CSharp, which this test assembly cannot reference directly.
    /// </summary>
    public class BackgroundMediaControllerSceneTests
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
        public void UiGameObject_HasBackgroundMediaController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent("BackgroundMediaController"),
                "UI GameObject must have a BackgroundMediaController (background media wiring).");
        }

        [Test]
        public void Bindings_CoverExactlyTwoScreens_WithTargetElementsAndMedia()
        {
            GameObject ui = OpenSceneAndFindUi();
            Component controller = ui.GetComponent("BackgroundMediaController");
            Assert.IsNotNull(controller, "Expected a BackgroundMediaController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty bindings = serialized.FindProperty("bindings");
            Assert.IsNotNull(bindings, "BackgroundMediaController must expose a serialized 'bindings' array.");
            Assert.AreEqual(2, bindings.arraySize, "Expected exactly two screen bindings (menu, combine). Logo Intro ('title') and Map ('map') have none.");

            var expectedTargets = new Dictionary<string, string>
            {
                { "menu", "home-bg-media" },
                { "combine", "combine-bg-media" },
            };

            var seen = new HashSet<string>();

            for (int i = 0; i < bindings.arraySize; i++)
            {
                SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                string screenId = element.FindPropertyRelative("screenId").stringValue;
                string targetElementName = element.FindPropertyRelative("targetElementName").stringValue;

                Assert.IsTrue(expectedTargets.ContainsKey(screenId), $"Unexpected screenId '{screenId}' in bindings.");
                Assert.AreEqual(expectedTargets[screenId], targetElementName,
                    $"Binding for '{screenId}' must target '{expectedTargets[screenId]}'.");
                seen.Add(screenId);

                Object clip = element.FindPropertyRelative("clip").objectReferenceValue;
                Object staticImage = element.FindPropertyRelative("staticImage").objectReferenceValue;

                Assert.IsNotNull(clip, $"Binding for '{screenId}' must carry a video clip.");
                Assert.IsNull(staticImage, $"Binding for '{screenId}' must not carry a static image.");

                if (screenId == "menu")
                {
                    string clipPath = AssetDatabase.GetAssetPath(clip);
                    Assert.AreEqual("Assets/UI/Media/Videos/main_menu_loop.mp4", clipPath,
                        "The Main Menu binding must reference the supplied main_menu_loop.mp4, not the retired home_loop.mp4.");
                }
            }

            CollectionAssert.AreEquivalent(expectedTargets.Keys, seen,
                "Bindings must cover exactly menu and combine — no more, no fewer.");
        }

        [TestCase("title")]
        [TestCase("map")]
        public void ScreenWithItsOwnMediaModel_HasNoBackgroundMediaControllerBinding(string screenId)
        {
            GameObject ui = OpenSceneAndFindUi();
            Component controller = ui.GetComponent("BackgroundMediaController");
            Assert.IsNotNull(controller, "Expected a BackgroundMediaController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty bindings = serialized.FindProperty("bindings");
            for (int i = 0; i < bindings.arraySize; i++)
            {
                string boundScreenId = bindings.GetArrayElementAtIndex(i).FindPropertyRelative("screenId").stringValue;
                Assert.AreNotEqual(screenId, boundScreenId,
                    $"'{screenId}' must not have a BackgroundMediaController binding — it manages its own media directly.");
            }
        }
    }
}
