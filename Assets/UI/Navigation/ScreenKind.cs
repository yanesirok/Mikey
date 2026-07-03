namespace Mikey.UI.Navigation
{
    /// <summary>How a screen is realized: a lightweight UI Toolkit panel toggled in the
    /// shared UIDocument, or a heavy additive scene loaded behind its HUD on demand.</summary>
    public enum ScreenKind
    {
        Panel,
        Scene
    }
}
