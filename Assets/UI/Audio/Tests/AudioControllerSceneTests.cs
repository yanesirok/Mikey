using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Audio.Tests
{
    /// <summary>
    /// Scene-wiring contract for AudioController: the "UI" GameObject carries it
    /// with both the Main Menu soundtrack and the shared UI click sound assigned.
    /// Mirrors HomeControllerSceneTests / BackgroundMediaControllerSceneTests.
    /// </summary>
    public class AudioControllerSceneTests
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
        public void UiGameObject_HasAudioController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent<AudioController>(),
                "UI GameObject must have an AudioController (Music/SFX/Trainer Voice settings + playback wiring).");
        }

        [Test]
        public void AudioController_HasMenuMusicAndUiClickClipsAssigned()
        {
            GameObject ui = OpenSceneAndFindUi();
            var controller = ui.GetComponent<AudioController>();
            Assert.IsNotNull(controller, "Expected an AudioController on the UI GameObject.");

            var serialized = new SerializedObject(controller);

            var musicClip = serialized.FindProperty("menuMusicClip");
            Assert.IsNotNull(musicClip, "AudioController must expose a serialized 'menuMusicClip' field.");
            Assert.IsNotNull(musicClip.objectReferenceValue, "'menuMusicClip' must be assigned (music_menu_main.mp3).");

            var clickClip = serialized.FindProperty("uiClickClip");
            Assert.IsNotNull(clickClip, "AudioController must expose a serialized 'uiClickClip' field.");
            Assert.IsNotNull(clickClip.objectReferenceValue, "'uiClickClip' must be assigned (ui_button_press.mp3).");
        }
    }
}
