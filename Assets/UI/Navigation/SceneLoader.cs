using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Owns the single "active heavy scene" slot. Loads heavy screen scenes additively behind
    /// the persistent App scene's UI and unloads them on exit so their 3D/video/effect memory is
    /// released. The unload/load decision is the pure <see cref="SceneTransition.Plan"/>; this
    /// class only performs the async Unity work and tracks the current scene.
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour, ISceneLoader
    {
        private string _current;        // last scene we asked to be active (null = none)
        private string _pendingTarget;  // most recent requested target, for in-flight coalescing

        public string CurrentHeavyScene => _current;

        public void ShowScene(string sceneName) => Transition(sceneName);

        public void ShowNoScene() => Transition(null);

        private void Transition(string target)
        {
            _pendingTarget = target;
            var (unload, load) = SceneTransition.Plan(_current, target);

            if (unload != null && SceneManager.GetSceneByName(unload).isLoaded)
                SceneManager.UnloadSceneAsync(unload);

            if (load != null)
            {
                if (!SceneManager.GetSceneByName(load).isLoaded)
                {
                    AsyncOperation op = SceneManager.LoadSceneAsync(load, LoadSceneMode.Additive);
                    op.completed += _ => OnLoaded(load);
                }
                else
                {
                    OnLoaded(load);
                }
            }

            _current = target;
        }

        private void OnLoaded(string sceneName)
        {
            // Only adopt as active scene if it is still the intended target (guards rapid nav).
            if (_pendingTarget != sceneName)
                return;
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
                SceneManager.SetActiveScene(scene);
        }
    }
}
