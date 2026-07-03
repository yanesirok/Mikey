using System.IO;
using Mikey.Pose.DevSandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mikey.Pose.DevSandbox.EditorTools
{
    /// <summary>
    /// Creates (and opens) the standalone ExerciseSandbox dev scene from a menu item, so the
    /// scene's component references resolve at creation time — no hand-authored GUIDs.
    /// </summary>
    public static class ExerciseSandboxSceneCreator
    {
        private const string ScenePath = "Assets/Scenes/ExerciseSandbox.unity";

        [MenuItem("Mikey/Dev/Create or Open Exercise Sandbox Scene")]
        public static void CreateOrOpen()
        {
            if (File.Exists(ScenePath))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            // Preserve any unsaved work before replacing the open scene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("ExerciseSandbox");
            // RequireComponent adds PoseController automatically.
            go.AddComponent<ExerciseSandbox>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[ExerciseSandbox] Created {ScenePath}. Press Play to test the push-up loop " +
                      "(simulation in the Editor, native MediaPipe on an Android device).");
        }
    }
}
