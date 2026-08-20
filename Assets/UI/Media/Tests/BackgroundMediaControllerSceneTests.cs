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
    /// carries a BackgroundMediaController with exactly three screen bindings
    /// (menu/combine/map), each pointing at the bg-media element added to that
    /// screen's full-bleed background layer, and each binding carries the right
    /// kind of media (a video clip for menu/combine, a static image for map) so no
    /// screen silently falls through to an unbound player. The Main Menu binding
    /// specifically must reference the supplied main_menu_loop.mp4 asset. Logo
    /// Intro ("title") deliberately has no video binding — see
    /// Assets/UI/Title/Title.uss: it is a static near-black background + the
    /// centered logo, nothing else; the retired title_loop.mp4 must not be
    /// visible there. Uses by-name/SerializedObject lookups, mirroring
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
        public void Bindings_CoverExactlyThreeScreens_WithTargetElementsAndMedia()
        {
            GameObject ui = OpenSceneAndFindUi();
            Component controller = ui.GetComponent("BackgroundMediaController");
            Assert.IsNotNull(controller, "Expected a BackgroundMediaController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty bindings = serialized.FindProperty("bindings");
            Assert.IsNotNull(bindings, "BackgroundMediaController must expose a serialized 'bindings' array.");
            Assert.AreEqual(3, bindings.arraySize, "Expected exactly three screen bindings (menu, combine, map). Logo Intro ('title') has none.");

            var expectedTargets = new Dictionary<string, string>
            {
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

                if (screenId == "map" || screenId == "combine")
                {
                    // Combine's real LEVEL 0 checklist uses the supplied static
                    // combine_background.png (matching the locked design
                    // reference), replacing the old placeholder's looping video —
                    // same static-image mechanism the Map screen already uses.
                    Assert.IsNull(clip, $"The '{screenId}' binding must not carry a video clip (it is a static background).");
                    Assert.IsNotNull(staticImage, $"The '{screenId}' binding must carry a static image.");

                    if (screenId == "combine")
                    {
                        string imagePath = AssetDatabase.GetAssetPath(staticImage);
                        Assert.AreEqual("Assets/UI/Media/Images/combine/combine_background.png", imagePath,
                            "The Combine binding must reference the supplied combine_background.png.");
                    }
                }
                else
                {
                    Assert.IsNotNull(clip, $"Binding for '{screenId}' must carry a video clip.");
                    Assert.IsNull(staticImage, $"Binding for '{screenId}' must not carry a static image.");

                    if (screenId == "menu")
                    {
                        string clipPath = AssetDatabase.GetAssetPath(clip);
                        Assert.AreEqual("Assets/UI/Media/Videos/main_menu_loop.mp4", clipPath,
                            "The Main Menu binding must reference the supplied main_menu_loop.mp4, not the retired home_loop.mp4.");
                    }
                }
            }

            CollectionAssert.AreEquivalent(expectedTargets.Keys, seen,
                "Bindings must cover exactly menu, combine and map — no more, no fewer.");
        }

        [Test]
        public void TitleScreen_HasNoBackgroundVideoBinding()
        {
            GameObject ui = OpenSceneAndFindUi();
            Component controller = ui.GetComponent("BackgroundMediaController");
            Assert.IsNotNull(controller, "Expected a BackgroundMediaController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty bindings = serialized.FindProperty("bindings");
            for (int i = 0; i < bindings.arraySize; i++)
            {
                string screenId = bindings.GetArrayElementAtIndex(i).FindPropertyRelative("screenId").stringValue;
                Assert.AreNotEqual("title", screenId,
                    "Logo Intro must not have a background-video binding — the retired title_loop.mp4 must not be visible there.");
            }
        }
    }
}
