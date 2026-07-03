using System.IO;
using Mikey.Pose.DevSandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mikey.Pose.DevSandbox.EditorTools
{
    /// <summary>
    /// Creates/opens the Stage-1 pose review scene: a camera, a light, and a
    /// <see cref="PoseReviewer"/> that plays back a recording from Assets/PoseRecordings/.
    /// </summary>
    public static class PoseReviewSceneCreator
    {
        private const string ScenePath = "Assets/Scenes/PoseReview.unity";

        [MenuItem("Mikey/Dev/Create or Open Pose Review Scene")]
        public static void CreateOrOpen()
        {
            if (File.Exists(ScenePath))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("PoseReviewer");
            go.AddComponent<PoseReviewer>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[PoseReview] Created {ScenePath}. Press Play to review the recording " +
                      "in Assets/PoseRecordings/ (drag mouse to rotate).");
        }
    }
}
