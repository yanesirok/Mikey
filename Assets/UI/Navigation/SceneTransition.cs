namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Pure transition planner for the single "active heavy scene" slot. Given the currently
    /// loaded heavy scene (null = none) and the target heavy scene (null = none), returns which
    /// scene to unload and which to load. Null entries mean "do nothing" for that side.
    /// </summary>
    public static class SceneTransition
    {
        public static (string unload, string load) Plan(string current, string target)
        {
            if (current == target)
                return (null, null);
            return (current, target);
        }
    }
}
