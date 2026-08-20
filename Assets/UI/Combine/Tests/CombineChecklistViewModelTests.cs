using System;
using Mikey.UI.Progression;
using NUnit.Framework;

namespace Mikey.UI.Combine.Tests
{
    /// <summary>
    /// EditMode coverage for the pure Combine checklist presentation logic — no
    /// panel / Play Mode needed. Verifies default selection, locked-row taps
    /// being inert, and the Changed event, all driven through a minimal fake
    /// <see cref="ILevel0Progress"/> so no real five-test walk-through is needed.
    /// </summary>
    public class CombineChecklistViewModelTests
    {
        /// <summary>Minimal in-memory <see cref="ILevel0Progress"/> for view-model tests.</summary>
        private sealed class FakeLevel0Progress : ILevel0Progress
        {
            private Level0Test _current = Level0Test.CameraTest;
            private readonly System.Collections.Generic.HashSet<Level0Test> _locked = new System.Collections.Generic.HashSet<Level0Test>
            {
                Level0Test.PushUps, Level0Test.Squats, Level0Test.WallSit, Level0Test.YokoGeri,
            };

            public event Action Changed;

            public Level0TestState StateOf(Level0Test test) => _locked.Contains(test) ? Level0TestState.Locked : Level0TestState.Available;

            public Level0Test CurrentTest => _current;

            public int CompletedCount { get; private set; }

            public bool IsComplete { get; private set; }

            public void Complete(Level0Test test) { }

            public void Reset() { }

            public void SetCurrentTest(Level0Test test)
            {
                _current = test;
                Changed?.Invoke();
            }

            public void Unlock(Level0Test test) => _locked.Remove(test);
        }

        [Test]
        public void ConstructorSelectsCurrentTest()
        {
            var progress = new FakeLevel0Progress();
            var vm = new CombineChecklistViewModel(progress);

            Assert.AreEqual(Level0Test.CameraTest, vm.SelectedTest);
        }

        [Test]
        public void Select_OnALockedTest_IsANoOp()
        {
            var progress = new FakeLevel0Progress();
            var vm = new CombineChecklistViewModel(progress);

            vm.Select(Level0Test.Squats); // locked in the fake

            Assert.AreEqual(Level0Test.CameraTest, vm.SelectedTest,
                "Selecting a locked test must not change the current selection.");
        }

        [Test]
        public void Select_OnAnUnlockedTest_UpdatesSelection_AndRaisesChanged()
        {
            var progress = new FakeLevel0Progress();
            progress.Unlock(Level0Test.PushUps);
            var vm = new CombineChecklistViewModel(progress);
            int changedCount = 0;
            vm.Changed += () => changedCount++;

            vm.Select(Level0Test.PushUps);

            Assert.AreEqual(Level0Test.PushUps, vm.SelectedTest);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void Select_SameTestAgain_DoesNotRaiseChanged()
        {
            var progress = new FakeLevel0Progress();
            var vm = new CombineChecklistViewModel(progress);
            int changedCount = 0;
            vm.Changed += () => changedCount++;

            vm.Select(Level0Test.CameraTest); // already selected

            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void SelectDefault_ReSelectsWhateverProgressReportsAsCurrent()
        {
            var progress = new FakeLevel0Progress();
            var vm = new CombineChecklistViewModel(progress);
            progress.Unlock(Level0Test.PushUps);
            vm.Select(Level0Test.PushUps);

            progress.SetCurrentTest(Level0Test.CameraTest);
            vm.SelectDefault();

            Assert.AreEqual(Level0Test.CameraTest, vm.SelectedTest,
                "SelectDefault must re-sync to the progress model's CurrentTest, discarding any stale prior preview.");
        }

        [Test]
        public void ConstructorThrows_WhenProgressIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new CombineChecklistViewModel(null));
        }
    }
}
