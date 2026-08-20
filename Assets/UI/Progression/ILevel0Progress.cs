using System;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Shared read/write access to the single canonical Level 0 Combine
    /// progression state: which of the five <see cref="Level0Test"/>s are
    /// complete, the derived Locked/Available/Complete state per test, and the
    /// test the Combine checklist should show selected when it opens. Lives
    /// alongside <see cref="ITutorialProgress"/> on the shared "UI" GameObject via
    /// <c>GetComponent&lt;ILevel0Progress&gt;()</c> — same cross-assembly pattern.
    /// </summary>
    public interface ILevel0Progress
    {
        /// <summary>Raised whenever a test's completion state actually changes.</summary>
        event Action Changed;

        /// <summary>Derived Locked/Available/Complete state for <paramref name="test"/>.</summary>
        Level0TestState StateOf(Level0Test test);

        /// <summary>
        /// The test the Combine screen should show selected on open: the first
        /// incomplete test in checklist order, or the LAST test
        /// (<see cref="Level0Test.YokoGeri"/>) once all five are complete.
        /// </summary>
        Level0Test CurrentTest { get; }

        /// <summary>How many of the five tests are complete, 0-5.</summary>
        int CompletedCount { get; }

        /// <summary>True once all five tests are complete.</summary>
        bool IsComplete { get; }

        /// <summary>
        /// Marks <paramref name="test"/> complete and persists it. A no-op unless
        /// <paramref name="test"/> is currently <see cref="Level0TestState.Available"/>
        /// — this is what makes strict sequential unlock a property of the model
        /// itself rather than something every caller has to get right.
        /// </summary>
        void Complete(Level0Test test);

        /// <summary>Resets every test to incomplete (only Camera Test available) and clears persisted data.</summary>
        void Reset();
    }
}
