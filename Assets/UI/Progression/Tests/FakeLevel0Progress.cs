using System;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Minimal in-memory <see cref="ILevel0Progress"/> so Okinawa progression
    /// tests can control "is Level 0 complete" directly, without depending on a
    /// real <see cref="Level0ProgressionStore"/>/five-test sequential walk-through.
    /// </summary>
    public sealed class FakeLevel0Progress : ILevel0Progress
    {
        public event Action Changed;

        public bool IsComplete { get; private set; }

        public Level0TestState StateOf(Level0Test test) =>
            IsComplete ? Level0TestState.Complete : Level0TestState.Locked;

        public Level0Test CurrentTest => Level0Test.CameraTest;

        public int CompletedCount => IsComplete ? 5 : 0;

        public void Complete(Level0Test test) { }

        /// <summary>Test hook: directly sets <see cref="IsComplete"/> and raises <see cref="Changed"/>.</summary>
        public void SetComplete(bool complete)
        {
            IsComplete = complete;
            Changed?.Invoke();
        }

        public void Reset()
        {
            IsComplete = false;
            Changed?.Invoke();
        }
    }
}
