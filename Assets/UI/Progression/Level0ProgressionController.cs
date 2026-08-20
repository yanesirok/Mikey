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
    /// </summary>
    public sealed class Level0ProgressionController : MonoBehaviour, ILevel0Progress
    {
        private Level0ProgressionStore _store;

        public event Action Changed
        {
            add { _store.Changed += value; }
            remove { _store.Changed -= value; }
        }

        public Level0TestState StateOf(Level0Test test) => _store.StateOf(test);
        public Level0Test CurrentTest => _store.CurrentTest;
        public int CompletedCount => _store.CompletedCount;
        public bool IsComplete => _store.IsComplete;

        private void Awake()
        {
            _store = new Level0ProgressionStore(new PlayerPrefsLevel0ProgressStorage());
        }

        public void Complete(Level0Test test) => _store.Complete(test);
        public void Reset() => _store.Reset();
    }
}
