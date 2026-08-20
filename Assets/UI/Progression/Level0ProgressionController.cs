using System;
using UnityEngine;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Owns the single canonical <see cref="Level0ProgressionStore"/> for the
    /// whole app and exposes it as <see cref="ILevel0Progress"/> so every Level 0
    /// screen controller on the shared "UI" GameObject (Combine, CameraTest, the
    /// Level0Tests placeholders) can reach the same source of truth via
    /// <c>GetComponent&lt;ILevel0Progress&gt;()</c> — mirrors
    /// <see cref="TutorialProgressionController"/> exactly. Backed by PlayerPrefs
    /// (survives app/Editor restart); reloads on every enable so a domain reload
    /// always reflects the last saved value.
    ///
    /// <see cref="Store"/> builds the underlying store lazily, on first access,
    /// rather than only in <see cref="Awake"/>: Unity does not guarantee this
    /// component's Awake runs before a sibling component's own
    /// initialization reaches in and reads/subscribes to this one — a real
    /// path here is <c>OkinawaProgressionController</c>, which subscribes to
    /// this component's <see cref="Changed"/> event as part of building its
    /// own store. Every public member goes through <see cref="Store"/>, never
    /// the backing field directly, so the store is guaranteed constructed no
    /// matter which order components initialize in.
    /// </summary>
    public sealed class Level0ProgressionController : MonoBehaviour, ILevel0Progress
    {
        private Level0ProgressionStore _store;

        /// <summary>
        /// The store, constructed on first access if it doesn't exist yet.
        /// Idempotent: a later call (e.g. from <see cref="Awake"/>, if it
        /// hasn't already run) sees <see cref="_store"/> already set and does
        /// no extra work.
        /// </summary>
        private Level0ProgressionStore Store
        {
            get
            {
                if (_store == null)
                    _store = new Level0ProgressionStore(new PlayerPrefsLevel0ProgressStorage());
                return _store;
            }
        }

        public event Action Changed
        {
            add { Store.Changed += value; }
            remove { Store.Changed -= value; }
        }

        public Level0TestState StateOf(Level0Test test) => Store.StateOf(test);
        public Level0Test CurrentTest => Store.CurrentTest;
        public int CompletedCount => Store.CompletedCount;
        public bool IsComplete => Store.IsComplete;

        private void Awake()
        {
            // Forces the normal, early construction point; a no-op if
            // something already accessed Store before Awake ran.
            _ = Store;
        }

        public void Complete(Level0Test test) => Store.Complete(test);
        public void Reset() => Store.Reset();
    }
}
