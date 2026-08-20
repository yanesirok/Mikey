using System;
using System.Collections.Generic;
using System.Text;

namespace Mikey.UI.Progression
{
    /// <summary>
    /// Pure state-transition logic for <see cref="IOkinawaProgress"/>, kept free
    /// of MonoBehaviour so it can be exercised directly in EditMode tests —
    /// mirrors <see cref="TutorialProgressStore"/>/<see cref="Level0ProgressionStore"/>.
    /// The concrete <see cref="IOkinawaProgressStorage"/> and the
    /// <see cref="ILevel0Progress"/> LVL0 derives from are both injected so tests
    /// can use fakes instead of real local storage / a real Level 0 store.
    /// </summary>
    public sealed class OkinawaProgressionStore : IOkinawaProgress
    {
        private const int MaxLevel = 6;

        private readonly IOkinawaProgressStorage _storage;
        private readonly ILevel0Progress _level0;
        private readonly HashSet<int> _completed = new HashSet<int>();

        public event Action Changed;

        public OkinawaProgressionStore(IOkinawaProgressStorage storage, ILevel0Progress level0)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _level0 = level0;

            // LVL0's completion (and therefore LVL1/LVL2's unlock) can change
            // purely because Level 0 Combine progressed, with no change to this
            // store's own persisted data — forward that signal so a single
            // subscription to THIS Changed event is always enough.
            if (_level0 != null)
                _level0.Changed += RaiseChanged;

            Load();
        }

        private void Load()
        {
            _completed.Clear();

            if (!_storage.TryLoad(out string raw) || string.IsNullOrEmpty(raw))
                return;

            foreach (string token in raw.Split(','))
            {
                if (int.TryParse(token, out int level) && level >= 1 && level <= MaxLevel)
                    _completed.Add(level);
            }
        }

        public bool IsComplete(int level)
        {
            if (level == 0)
                return _level0 != null && _level0.IsComplete;
            if (level < 0 || level > MaxLevel)
                return false;
            return _completed.Contains(level);
        }

        public bool IsUnlocked(int level)
        {
            switch (level)
            {
                case 0:
                    return true;
                case 1:
                case 2:
                    return IsComplete(0);
                case 3:
                case 4:
                    return IsComplete(1) && IsComplete(2);
                case 5:
                    return IsComplete(3) && IsComplete(4);
                case 6:
                    // Explicit conjunction of the FULL LVL0-5 prerequisite set,
                    // not just LVL5 — deliberate even though it's transitively
                    // implied by the chain above as long as every Complete() call
                    // is gated by IsUnlocked at call time. This is cheap insurance
                    // against a corrupted/hand-edited save marking LVL5 complete
                    // without 3/4 having been.
                    for (int i = 0; i <= 5; i++)
                    {
                        if (!IsComplete(i))
                            return false;
                    }
                    return true;
                default:
                    return false;
            }
        }

        public void Complete(int level)
        {
            if (level == 0)
                return;
            if (level < 0 || level > MaxLevel)
                return;
            if (!IsUnlocked(level))
                return;
            if (_completed.Contains(level))
                return;

            _completed.Add(level);
            Save();
            RaiseChanged();
        }

        public void Reset()
        {
            bool wasAlreadyEmpty = _completed.Count == 0;
            _completed.Clear();
            _storage.Delete();
            if (!wasAlreadyEmpty)
                RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke();

        private void Save()
        {
            var builder = new StringBuilder();
            bool first = true;
            foreach (int level in _completed)
            {
                if (!first)
                    builder.Append(',');
                builder.Append(level);
                first = false;
            }
            _storage.Save(builder.ToString());
        }
    }
}
