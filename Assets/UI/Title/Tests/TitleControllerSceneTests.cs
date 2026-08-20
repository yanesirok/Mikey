using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Title.Tests
{
    /// <summary>
    /// Scene-wiring contract for TitleController: the "UI" GameObject carries it,
    /// wired to the final logo_intro.mp4 asset, so the Logo Intro's video
    /// playback and tap-skip behavior actually runs in a real build. Mirrors
    /// IntroControllerTests' UiGameObject_HasIntroController.
    /// </summary>
    public class TitleControllerSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string LogoIntroClipPath = "Assets/UI/Media/Videos/logo_intro.mp4";

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
        public void UiGameObject_HasTitleController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent<TitleController>(),
                "UI GameObject must have a TitleController for the Logo Intro video/tap-skip to run in a real build.");
        }

        [Test]
        public void TitleController_ReferencesTheFinalLogoIntroVideo()
        {
            GameObject ui = OpenSceneAndFindUi();
            TitleController controller = ui.GetComponent<TitleController>();
            Assert.IsNotNull(controller, "Expected a TitleController on the UI GameObject.");

            var serialized = new SerializedObject(controller);
            SerializedProperty clip = serialized.FindProperty("logoIntroClip");
            Assert.IsNotNull(clip, "TitleController must expose a serialized 'logoIntroClip'.");
            Assert.IsNotNull(clip.objectReferenceValue, "TitleController must have a logo intro video clip assigned.");

            string clipPath = AssetDatabase.GetAssetPath(clip.objectReferenceValue);
            Assert.AreEqual(LogoIntroClipPath, clipPath,
                "Title must reference the final logo_intro.mp4, not a placeholder or the retired title_loop.mp4.");
        }

        [Test]
        public void LogoIntroVideoAsset_ExistsOnDisk()
        {
            Assert.IsTrue(File.Exists(LogoIntroClipPath), $"Expected the final logo intro video at {LogoIntroClipPath}.");
        }
    }
}
