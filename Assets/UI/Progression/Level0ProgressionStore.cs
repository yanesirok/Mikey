using System;
using System.Collections.Generic;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Pure state-transition logic for <see cref="ILevel0Progress"/>, kept free of
    /// MonoBehaviour so it can be exercised directly in EditMode tests — mirrors
    /// <see cref="TutorialProgressStore"/>. The concrete
    /// <see cref="ILevel0ProgressStorage"/> is injected so tests can use an
    /// in-memory fake instead of real local storage.
    /// </summary>
    public sealed class Level0ProgressionStore : ILevel0Progress
    {
        private static readonly Level0Test[] Order =
        {
            Level0Test.CameraTest,
            Level0Test.PushUps,
            Level0Test.Squats,
            Level0Test.WallSit,
            Level0Test.YokoGeri,
        };

        private readonly ILevel0ProgressStorage _storage;
        private readonly HashSet<Level0Test> _completed = new HashSet<Level0Test>();

        public event Action Changed;

        public Level0ProgressionStore(ILevel0ProgressStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Load();
        }

        /// <summary>
        /// Reads the persisted completed-test set. Falls back to the safe default
        /// (nothing complete, only Camera Test available) if nothing was ever
        /// saved; any unrecognized/corrupted token is silently dropped rather than
        /// throwing — mirrors <see cref="TutorialProgressStore.Load"/>'s
        /// corruption-safety.
        /// </summary>
        private void Load()
        {
            _completed.Clear();

            if (!_storage.TryLoad(out string raw) || string.IsNullOrEmpty(raw))
                return;

            foreach (string token in raw.Split(','))
            {
                if (Enum.TryParse(token, out Level0Test parsed) && Enum.IsDefined(typeof(Level0Test), parsed))
                    _completed.Add(parsed);
            }
        }

        public Level0TestState StateOf(Level0Test test)
        {
            if (_completed.Contains(test))
                return Level0TestState.Complete;

            foreach (Level0Test candidate in Order)
            {
                if (_completed.Contains(candidate))
                    continue;
                return candidate == test ? Level0TestState.Available : Level0TestState.Locked;
            }

            // Unreachable in practice: every test being complete means the loop
            // above always finds a match. Kept as a safe fallback.
            return Level0TestState.Locked;
        }

        public Level0Test CurrentTest
        {
            get
            {
                foreach (Level0Test candidate in Order)
                {
                    if (!_completed.Contains(candidate))
                        return candidate;
                }
                return Order[Order.Length - 1];
            }
        }

        public int CompletedCount => _completed.Count;

        public bool IsComplete => _completed.Count == Order.Length;

        public void Complete(Level0Test test)
        {
            if (StateOf(test) != Level0TestState.Available)
                return;

            _completed.Add(test);
            Save();
            Changed?.Invoke();
        }

        public void Reset()
        {
            bool wasAlreadyEmpty = _completed.Count == 0;
            _completed.Clear();
            _storage.Delete();
            if (!wasAlreadyEmpty)
                Changed?.Invoke();
        }

        private void Save() => _storage.Save(string.Join(",", _completed));
    }
}
