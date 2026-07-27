using System;
using UnityEngine;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Owns the single canonical <see cref="TutorialProgressStore"/> for the whole
    /// app and exposes it as <see cref="ITutorialProgress"/> so every screen
    /// controller on the shared "UI" GameObject (Home, CameraTest, Combine,
    /// Practice, Map) can reach the same source of truth via
    /// <c>GetComponent&lt;ITutorialProgress&gt;()</c> — mirrors how ScreenManager
    /// exposes <see cref="Mikey.UI.SafeArea.IScreenNavigator"/>. Backed by
    /// PlayerPrefs (survives app/Editor restart); reloads on every enable so a
    /// domain reload / re-entering play mode always reflects the last saved value.
    /// </summary>
    public sealed class TutorialProgressionController : MonoBehaviour, ITutorialProgress
    {
        private TutorialProgressStore _store;

        public event Action Changed
        {
            add { _store.Changed += value; }
            remove { _store.Changed -= value; }
        }

        public TutorialProgressState State => _store.State;

        private void Awake()
        {
            _store = new TutorialProgressStore(new PlayerPrefsTutorialProgressStorage());
        }

        public void SetState(TutorialProgressState state) => _store.SetState(state);

        public void Advance(TutorialProgressState state) => _store.Advance(state);

        public void Reset() => _store.Reset();
    }
}
