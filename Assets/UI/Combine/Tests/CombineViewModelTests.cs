using NUnit.Framework;

namespace Mikey.UI.Combine.Tests
{
    /// <summary>
    /// EditMode coverage for the pure Combine state logic — no panel / Play Mode
    /// needed. Verifies state transitions, mock-item population per state, the
    /// dev-cycle order, and that the change event fires.
    /// </summary>
    public class CombineViewModelTests
    {
        [Test]
        public void StartsInLoadingWithNoItems()
        {
            var vm = new CombineViewModel();
            Assert.AreEqual(CombineState.Loading, vm.State);
            Assert.IsEmpty(vm.Items);
        }

        [Test]
        public void Ready_PopulatesMockItems()
        {
            var vm = new CombineViewModel();
            vm.SetState(CombineState.Ready);

            Assert.AreEqual(CombineState.Ready, vm.State);
            Assert.AreEqual(CombineViewModel.CreateMockItems().Count, vm.Items.Count);
            Assert.IsNotEmpty(vm.Items);
        }

        [TestCase(CombineState.Loading)]
        [TestCase(CombineState.Empty)]
        [TestCase(CombineState.Error)]
        public void NonReadyStates_HaveNoItems(CombineState state)
        {
            var vm = new CombineViewModel();
            vm.SetState(CombineState.Ready); // ensure items exist first
            vm.SetState(state);

            Assert.AreEqual(state, vm.State);
            Assert.IsEmpty(vm.Items);
        }

        [Test]
        public void SetState_RaisesChanged()
        {
            var vm = new CombineViewModel();
            int raised = 0;
            vm.Changed += () => raised++;

            vm.SetState(CombineState.Empty);
            vm.SetState(CombineState.Ready);

            Assert.AreEqual(2, raised);
        }

        [Test]
        public void SetState_SameState_StillRaisesChanged()
        {
            var vm = new CombineViewModel();
            int raised = 0;
            vm.Changed += () => raised++;

            vm.SetState(CombineState.Loading); // already Loading at construction

            Assert.AreEqual(1, raised);
        }

        [TestCase(CombineState.Loading, CombineState.Empty)]
        [TestCase(CombineState.Empty, CombineState.Ready)]
        [TestCase(CombineState.Ready, CombineState.Error)]
        [TestCase(CombineState.Error, CombineState.Loading)]
        public void NextState_FollowsDevCycle(CombineState from, CombineState expected)
        {
            Assert.AreEqual(expected, CombineViewModel.NextState(from));
        }

        [Test]
        public void CycleState_WalksFullLoopBackToStart()
        {
            var vm = new CombineViewModel();
            Assert.AreEqual(CombineState.Loading, vm.State);

            vm.CycleState();
            Assert.AreEqual(CombineState.Empty, vm.State);
            vm.CycleState();
            Assert.AreEqual(CombineState.Ready, vm.State);
            vm.CycleState();
            Assert.AreEqual(CombineState.Error, vm.State);
            vm.CycleState();
            Assert.AreEqual(CombineState.Loading, vm.State);
        }

        [Test]
        public void MockItems_AreFullyPopulated()
        {
            var items = CombineViewModel.CreateMockItems();
            Assert.AreEqual(4, items.Count);
            foreach (var item in items)
            {
                Assert.IsFalse(string.IsNullOrEmpty(item.Name), "Name should be set.");
                Assert.IsFalse(string.IsNullOrEmpty(item.Glyph), "Glyph should be set.");
                Assert.IsFalse(string.IsNullOrEmpty(item.Value), "Value should be set.");
                Assert.IsFalse(string.IsNullOrEmpty(item.Unit), "Unit should be set.");
                Assert.IsFalse(string.IsNullOrEmpty(item.Stat), "Stat should be set.");
            }
        }
    }
}
