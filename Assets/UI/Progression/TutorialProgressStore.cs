using System;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Pure state-transition logic for <see cref="ITutorialProgress"/>, kept free
    /// of MonoBehaviour so it can be exercised directly in EditMode tests (mirrors
    /// how <c>CombineViewModel</c> / <c>PracticeSessionModel</c> are split out from
    /// their MonoBehaviour controllers). The concrete <see cref="ITutorialProgressStorage"/>
    /// is injected so tests can use an in-memory fake instead of real local storage.
    /// </summary>
    public sealed class TutorialProgressStore : ITutorialProgress
    {
        private readonly ITutorialProgressStorage _storage;

        public event Action Changed;

        public TutorialProgressState State { get; private set; } = TutorialProgressState.NewPlayer;

        public TutorialProgressStore(ITutorialProgressStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Load();
        }

        /// <summary>
        /// Reads the persisted state. Falls back to the safe default
        /// (<see cref="TutorialProgressState.NewPlayer"/>) if nothing was ever
        /// saved, or if the saved value is invalid/corrupted (not a recognized
        /// <see cref="TutorialProgressState"/> member) — never throws.
        /// </summary>
        private void Load()
        {
            State = TutorialProgressState.NewPlayer;

            if (!_storage.TryLoad(out string raw) || string.IsNullOrEmpty(raw))
                return;

            if (Enum.TryParse(raw, out TutorialProgressState parsed) && Enum.IsDefined(typeof(TutorialProgressState), parsed))
                State = parsed;
            // Any other raw value (corrupted, from a future/removed enum member,
            // garbage) is silently ignored — State stays at the safe default.
        }

        public void SetState(TutorialProgressState state)
        {
            if (State == state)
                return;

            State = state;
            _storage.Save(state.ToString());
            Changed?.Invoke();
        }

        public void Advance(TutorialProgressState state)
        {
            if (state > State)
                SetState(state);
        }

        public void Reset()
        {
            _storage.Delete();
            bool wasAlreadyDefault = State == TutorialProgressState.NewPlayer;
            State = TutorialProgressState.NewPlayer;
            if (!wasAlreadyDefault)
                Changed?.Invoke();
        }
    }
}
