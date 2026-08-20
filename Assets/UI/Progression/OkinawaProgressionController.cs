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
    /// exactly. Reads the sibling <see cref="ILevel0Progress"/> component at
    /// Awake so LVL0's completion always derives from the single Level 0
    /// Combine source of truth. Backed by PlayerPrefs.
    /// </summary>
    public sealed class OkinawaProgressionController : MonoBehaviour, IOkinawaProgress
    {
        private OkinawaProgressionStore _store;

        public event Action Changed
        {
            add { _store.Changed += value; }
            remove { _store.Changed -= value; }
        }

        public bool IsUnlocked(int level) => _store.IsUnlocked(level);
        public bool IsComplete(int level) => _store.IsComplete(level);

        private void Awake()
        {
            var level0 = GetComponent<ILevel0Progress>();
            _store = new OkinawaProgressionStore(new PlayerPrefsOkinawaProgressStorage(), level0);
        }

        public void Complete(int level) => _store.Complete(level);
        public void Reset() => _store.Reset();
    }
}
