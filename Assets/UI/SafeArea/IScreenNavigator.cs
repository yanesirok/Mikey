using System;

namespace Mikey.UI.SafeArea
{
    /// <summary>
    /// Minimal screen-entry signal exposed by the navigation driver (ScreenManager).
    /// Lets screen controllers react to genuine navigation entry without depending on
    /// MonoBehaviour <c>OnEnable</c> — which is unreliable here because every screen
    /// shares one always-enabled UI GameObject and ScreenManager only toggles the
    /// visibility of individual UXML screens.
    ///
    /// Lives in this assembly (autoReferenced) so the predefined Assembly-CSharp
    /// (where ScreenManager lives) can implement it while screen-controller assemblies
    /// — which cannot reference Assembly-CSharp — can still consume it by interface.
    /// </summary>
    public interface IScreenNavigator
    {
        /// <summary>
        /// Raised when the shown screen actually changes, carrying the newly shown
        /// screen id. Not raised when the same screen is re-requested while already
        /// active, so a controller resets once per genuine entry, not repeatedly.
        /// </summary>
        event Action<string> ScreenChanged;

        /// <summary>The id of the screen currently shown (null before the first show).</summary>
        string CurrentScreen { get; }
    }
}
