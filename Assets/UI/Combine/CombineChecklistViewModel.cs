using System;
using Mikey.UI.Progression;

namespace Mikey.UI.Combine
{
    /// <summary>
    /// Pure, frontend-only presentation layer over <see cref="ILevel0Progress"/>
    /// for the Level 0 Combine checklist screen. Owns nothing about completion
    /// itself (that's the injected progress model) — only the currently
    /// PREVIEWED test, which the left panel renders. Kept free of UnityEngine/UI
    /// Toolkit types so it can be exercised in EditMode tests without a live
    /// panel (mirrors how the old <c>CombineViewModel</c> was split out from
    /// <c>CombineScreenController</c>).
    /// </summary>
    public sealed class CombineChecklistViewModel
    {
        private readonly ILevel0Progress _progress;

        /// <summary>Raised after <see cref="SelectedTest"/> changes, or the underlying progress changes.</summary>
        public event Action Changed;

        public CombineChecklistViewModel(ILevel0Progress progress)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            SelectedTest = _progress.CurrentTest;
        }

        /// <summary>The test currently previewed in the left panel.</summary>
        public Level0Test SelectedTest { get; private set; }

        public Level0TestState StateOf(Level0Test test) => _progress.StateOf(test);

        public int CompletedCount => _progress.CompletedCount;

        public bool IsLevel0Complete => _progress.IsComplete;

        /// <summary>
        /// Selects the given test for preview. A no-op for a
        /// <see cref="Level0TestState.Locked"/> test — locked rows are
        /// non-interactive by construction, not a view-layer "if" the caller has
        /// to remember.
        /// </summary>
        public void Select(Level0Test test)
        {
            if (StateOf(test) == Level0TestState.Locked)
                return;
            if (SelectedTest == test)
                return;

            SelectedTest = test;
            Changed?.Invoke();
        }

        /// <summary>
        /// Re-selects the current available test (or, if Level 0 is fully
        /// complete, the most recently completed one) — called on every genuine
        /// entry into the Combine screen so a stale prior selection never lingers.
        /// </summary>
        public void SelectDefault()
        {
            SelectedTest = _progress.CurrentTest;
            Changed?.Invoke();
        }

        /// <summary>Re-raises Changed without altering the selection — used when the underlying progress model changes.</summary>
        public void NotifyProgressChanged() => Changed?.Invoke();
    }
}
