namespace Mikey.UI.SafeArea
{
    /// <summary>
    /// Minimal launch-shell preload signal: lets Logo Intro kick off preparing
    /// whatever the immediate shell flow needs (currently: the Main Menu
    /// background video) the moment it starts playing, instead of waiting
    /// until Lore hands off to Menu and paying for it there as a hitch.
    ///
    /// Lives in this assembly (autoReferenced) so screen-controller assemblies
    /// — which cannot reference Assembly-CSharp, where the concrete preloader
    /// lives — can still consume it via GetComponent, same pattern as
    /// IScreenNavigator.
    /// </summary>
    public interface IShellPreloader
    {
        /// <summary>
        /// Begins preparing whatever the launch shell needs ready ahead of
        /// time. Safe to call more than once — a no-op once already
        /// prepared/preparing.
        /// </summary>
        void BeginPreload();

        /// <summary>
        /// True once everything <see cref="BeginPreload"/> was preparing is
        /// ready to show instantly, with no first-frame hitch. True by
        /// default if there is nothing to prepare, so a caller waiting on
        /// this never blocks forever.
        /// </summary>
        bool IsReady { get; }
    }
}
