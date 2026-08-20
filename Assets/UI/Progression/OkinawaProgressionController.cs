using System;
using UnityEngine;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Owns the single canonical <see cref="OkinawaProgressionStore"/> for the
    /// whole app and exposes it as <see cref="IOkinawaProgress"/> so
    /// <c>OkinawaMapController</c> can reach it via
    /// <c>GetComponent&lt;IOkinawaProgress&gt;()</c> — mirrors
    /// <see cref="TutorialProgressionController"/>/<see cref="Level0ProgressionController"/>
    /// exactly. Reads the sibling <see cref="ILevel0Progress"/> component so
    /// LVL0's completion always derives from the single Level 0 Combine source
    /// of truth. Backed by PlayerPrefs.
    ///
    /// <see cref="Store"/> builds the underlying store lazily, on first access,
    /// rather than only in <see cref="Awake"/>: Unity does not guarantee this
    /// component's Awake runs before another component's OnEnable-driven logic
    /// on the same GameObject (<c>OkinawaMapController.BindWhenReady</c> is a
    /// coroutine started from its own OnEnable, and was observed reaching
    /// <see cref="IsUnlocked"/>/<see cref="Changed"/> here before Awake had
    /// run, throwing a NullReferenceException and leaving the Okinawa screen
    /// blank). Every public member goes through <see cref="Store"/>, never the
    /// backing field directly, so the store is guaranteed constructed no
    /// matter which order components initialize in.
    /// </summary>
    public sealed class OkinawaProgressionController : MonoBehaviour, IOkinawaProgress
    {
        private OkinawaProgressionStore _store;

        /// <summary>
        /// The store, constructed on first access if it doesn't exist yet.
        /// Idempotent: a later call (e.g. from <see cref="Awake"/>, if it
        /// hasn't already run) sees <see cref="_store"/> already set and does
        /// no extra work.
        /// </summary>
        private OkinawaProgressionStore Store
        {
            get
            {
                if (_store == null)
                {
                    var level0 = GetComponent<ILevel0Progress>();
                    _store = new OkinawaProgressionStore(new PlayerPrefsOkinawaProgressStorage(), level0);
                }
                return _store;
            }
        }

        public event Action Changed
        {
            add { Store.Changed += value; }
            remove { Store.Changed -= value; }
        }

        public bool IsUnlocked(int level) => Store.IsUnlocked(level);
        public bool IsComplete(int level) => Store.IsComplete(level);

        private void Awake()
        {
            // Forces the normal, early construction point; a no-op if
            // something already accessed Store before Awake ran.
            _ = Store;
        }

        public void Complete(int level) => Store.Complete(level);
        public void Reset() => Store.Reset();
    }
}
