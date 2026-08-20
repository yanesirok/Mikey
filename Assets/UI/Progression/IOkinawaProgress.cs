using System;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Shared read/write access to the Okinawa LVL0-6 mission unlock/completion
    /// state — the paired-unlock model (LVL1+LVL2 unlock together off Level 0,
    /// LVL3+LVL4 off both 1 and 2, LVL5 off both 3 and 4, LVL6 Boss off the full
    /// LVL0-5 set). Level indices match <c>MissionMarkerLayout.LevelIndex</c> /
    /// the scene's <c>level-node-{i}</c> buttons exactly (0-6) — no separate
    /// mission id type. LVL0's completion is derived from
    /// <see cref="ILevel0Progress.IsComplete"/>, never duplicated here.
    /// </summary>
    public interface IOkinawaProgress
    {
        /// <summary>
        /// Raised whenever a level's completion state changes, INCLUDING when the
        /// underlying <see cref="ILevel0Progress"/> changes (LVL0's derived
        /// completion, and therefore LVL1/LVL2's unlock state, can change without
        /// this store's own persisted data changing at all).
        /// </summary>
        event Action Changed;

        /// <summary>True once <paramref name="level"/>'s prerequisites are met (it can be entered/selected).</summary>
        bool IsUnlocked(int level);

        /// <summary>True once <paramref name="level"/> itself is complete. LVL0 is derived from Level 0 Combine.</summary>
        bool IsComplete(int level);

        /// <summary>
        /// Marks <paramref name="level"/> complete and persists it. A no-op for
        /// LVL0 (derived, not directly settable) and for any level not currently
        /// unlocked.
        /// </summary>
        void Complete(int level);

        /// <summary>Resets every directly-settable level (1-6) to incomplete and clears persisted data.</summary>
        void Reset();
    }
}
