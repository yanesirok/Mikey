namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Drives the single "active heavy scene" slot for the router. Implemented by the runtime
    /// <c>SceneLoader</c> MonoBehaviour and by test fakes, so ScreenManager's routing can be
    /// verified without actually loading scenes.
    /// </summary>
    public interface ISceneLoader
    {
        /// <summary>The heavy scene currently loaded, or null if none.</summary>
        string CurrentHeavyScene { get; }

        /// <summary>Ensure exactly this heavy scene is loaded (unloading any other first).</summary>
        void ShowScene(string sceneName);

        /// <summary>Unload any heavy scene so only the persistent App scene remains.</summary>
        void ShowNoScene();
    }
}
