namespace Mikey.UI.Navigation
{
    /// <summary>One screen's routing record. <see cref="SceneName"/> is null for panels.</summary>
    public sealed class ScreenRoute
    {
        public string ScreenId { get; }
        public ScreenKind Kind { get; }
        public string SceneName { get; }

        public ScreenRoute(string screenId, ScreenKind kind, string sceneName)
        {
            ScreenId = screenId;
            Kind = kind;
            SceneName = sceneName;
        }
    }
}
