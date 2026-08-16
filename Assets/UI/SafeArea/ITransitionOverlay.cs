using System.Collections;

namespace Mikey.UI.SafeArea
{
    /// <summary>
    /// The one shared full-screen black fade overlay for the launch shell's
    /// cinematic transitions (Logo → Lore → Main Menu). Painted above every
    /// screen, outside ScreenManager's display:none/flex toggling, so a
    /// controller can darken the current screen, swap to the next one while
    /// fully covered, then reveal it — turning an instant ScreenManager.Show
    /// swap into a smooth fade instead of a hard cut.
    ///
    /// Lives in this assembly (autoReferenced) so screen-controller assemblies
    /// — which cannot reference Assembly-CSharp, where the concrete overlay
    /// lives — can still consume it via GetComponent, same pattern as
    /// IScreenNavigator.
    /// </summary>
    public interface ITransitionOverlay
    {
        /// <summary>
        /// Fades the overlay from its current opacity to fully opaque black
        /// over <paramref name="seconds"/>. Yield this on the caller's own
        /// coroutine to wait for completion before swapping screens.
        /// </summary>
        IEnumerator FadeToBlack(float seconds);

        /// <summary>
        /// Fades the overlay from fully opaque black to fully transparent over
        /// <paramref name="seconds"/>, revealing whatever screen is underneath.
        /// Yield this on the caller's own coroutine to wait for completion.
        /// </summary>
        IEnumerator FadeFromBlack(float seconds);
    }
}
