using System.Collections.Generic;

namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Maps a screenId to how it is realized. Screens default to <see cref="ScreenKind.Panel"/>;
    /// only scene-backed (heavy) screens are registered explicitly, so adding a panel screen needs
    /// no entry here. Pure data — no Unity dependency, fully unit-testable.
    /// </summary>
    public sealed class ScreenRouteTable
    {
        private readonly Dictionary<string, ScreenRoute> _scenes = new Dictionary<string, ScreenRoute>();

        /// <summary>Mark a screen as backed by an additive scene of the given name.</summary>
        public void RegisterScene(string screenId, string sceneName)
        {
            _scenes[screenId] = new ScreenRoute(screenId, ScreenKind.Scene, sceneName);
        }

        public ScreenKind KindOf(string screenId) =>
            _scenes.ContainsKey(screenId) ? ScreenKind.Scene : ScreenKind.Panel;

        public bool IsScene(string screenId) => _scenes.ContainsKey(screenId);

        /// <summary>The additive scene name for a scene screen, or null for a panel screen.</summary>
        public string SceneNameOf(string screenId) =>
            _scenes.TryGetValue(screenId, out ScreenRoute route) ? route.SceneName : null;
    }
}
