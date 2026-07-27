using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Media.Tests
{
    /// <summary>
    /// Scene-wiring contract for the MVP background media batch: the "UI"
    /// GameObject carries a BackgroundMediaController with exactly the four
    /// screen bindings (title/menu/combine/map), each pointing at the
    /// bg-media element added to that screen's full-bleed background layer,
    /// and each binding carries the right kind of media (a video clip for
    /// title/menu/combine, a static image for map) so no screen silently
    /// falls through to an unbound player. Uses by-name/SerializedObject
    /// lookups, mirroring SceneWiringTests, because BackgroundMediaController
    /// lives in Assembly-CSharp, which this test assembly cannot reference
    /// directly.
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
        public void Bindings_CoverAllFourScreens_WithTargetElementsAndMedia()
        {
            GameObject ui = OpenSceneAndFindUi();
            Component controller = ui.GetComponent("BackgroundMediaController");
            Assert.IsNotNull(controller, "Expected a BackgroundMediaController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty bindings = serialized.FindProperty("bindings");
            Assert.IsNotNull(bindings, "BackgroundMediaController must expose a serialized 'bindings' array.");
            Assert.AreEqual(4, bindings.arraySize, "Expected exactly four screen bindings (title, menu, combine, map).");

            var expectedTargets = new Dictionary<string, string>
            {
                { "title", "title-bg-media" },
                { "menu", "home-bg-media" },
                { "combine", "combine-bg-media" },
                { "map", "map-bg-media" },
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

                if (screenId == "map")
                {
                    Assert.IsNull(clip, "The Map binding must not carry a video clip (it is a static background).");
                    Assert.IsNotNull(staticImage, "The Map binding must carry a static image.");
                }
                else
                {
                    Assert.IsNotNull(clip, $"Binding for '{screenId}' must carry a video clip.");
                    Assert.IsNull(staticImage, $"Binding for '{screenId}' must not carry a static image.");
                }
            }

            CollectionAssert.AreEquivalent(expectedTargets.Keys, seen,
                "Bindings must cover exactly title, menu, combine and map — no more, no fewer.");
        }
    }
}
