using System;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Shared read/write access to the single canonical MVP tutorial progression
    /// state. Lives in this assembly (autoReferenced) so the predefined
    /// Assembly-CSharp / per-screen assemblies (CameraTest, Combine, Practice,
    /// Home, Map) can each consume the one instance living on the shared "UI"
    /// GameObject via <c>GetComponent&lt;ITutorialProgress&gt;()</c> — the same
    /// cross-assembly pattern <see cref="Mikey.UI.SafeArea.IScreenNavigator"/>
    /// uses for screen navigation.
    /// </summary>
    public interface ITutorialProgress
    {
        /// <summary>Raised whenever <see cref="State"/> actually changes (including via <see cref="Reset"/>).</summary>
        event Action Changed;

        /// <summary>The current progression state. Safe default is <see cref="TutorialProgressState.NewPlayer"/>.</summary>
        TutorialProgressState State { get; }

        /// <summary>
        /// Sets <see cref="State"/> to exactly <paramref name="state"/> (forward or
        /// backward) and persists it. Used for explicit, intentional transitions —
        /// developer controls and deliberate resets (e.g. Retry Assessment). Most
        /// screen-entry-driven progress should prefer <see cref="Advance"/> instead,
        /// so passively re-visiting an earlier screen never regresses progress
        /// already made.
        /// </summary>
        void SetState(TutorialProgressState state);

        /// <summary>
        /// Sets <see cref="State"/> to <paramref name="state"/> only if that is
        /// further along than the current state; otherwise a no-op. This is the
        /// safe default for progress driven by passively entering/completing a
        /// screen (e.g. entering camTest, completing Practice), so navigating back
        /// through an earlier step via an alternate route (Profile's dock, a
        /// developer control) can never silently undo progress already made.
        /// </summary>
        void Advance(TutorialProgressState state);

        /// <summary>Resets to the safe default (<see cref="TutorialProgressState.NewPlayer"/>) and clears persisted data.</summary>
        void Reset();
    }
}
