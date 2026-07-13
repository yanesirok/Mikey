using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Mikey.EditorTools
{
    /// <summary>
    /// Forces Unity to always enter Play Mode from the main UI scene (the screen
    /// flow driven by <c>ScreenManager</c> / <c>MikeyApp.uxml</c>), regardless of
    /// which scene the editor currently has open.
    ///
    /// Unity's Play button plays whatever scene is open in the editor, not the
    /// first scene in Build Settings. Setting <see cref="EditorSceneManager.playModeStartScene"/>
    /// overrides that so pressing Play always boots the app from its real entry screen.
    ///
    /// Toggle it off from the menu: <b>Mikey ▸ Play From Main Scene</b>.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeStartScene
    {
        private const string MainScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MenuPath = "Mikey/Play From Main Scene";
        private const string PrefKey = "Mikey.PlayFromMainScene";

        static PlayModeStartScene()
        {
            // Apply after the editor finishes loading so the asset database is ready,
            // and re-apply whenever the open scene changes (dev sandboxes opt out).
            EditorApplication.delayCall += Apply;
            EditorSceneManager.sceneOpened += (_, _) => Apply();
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, validate = true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void Apply()
        {
            if (Enabled && !IsDevScene(SceneManager.GetActiveScene()))
            {
                var mainScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath);
                EditorSceneManager.playModeStartScene = mainScene;
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
            }
        }

        /// <summary>
        /// A scene not listed in Build Settings is a dev sandbox (FightSandbox,
        /// ExerciseSandbox, PoseReview, ...) — pressing Play should run it directly.
        /// </summary>
        private static bool IsDevScene(Scene scene)
        {
            if (string.IsNullOrEmpty(scene.path))
                return false;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.path == scene.path)
                    return false;
            return true;
        }
    }
}
