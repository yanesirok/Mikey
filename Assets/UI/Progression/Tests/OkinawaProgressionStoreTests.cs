using NUnit.Framework;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Contract for <see cref="OkinawaProgressionStore"/>: LVL0 derives from
    /// Level 0 Combine (never a duplicate flag), the paired LVL1+2 / LVL3+4
    /// unlock gates, LVL5's gate, the Boss's full-prerequisite-set gate, and
    /// persistence round trip — all driven through in-memory fakes so no real
    /// local storage is touched and no real five-test Level 0 walk-through is
    /// needed.
    /// </summary>
    public class OkinawaProgressionStoreTests
    {
        private static OkinawaProgressionStore NewStore(FakeLevel0Progress level0 = null, FakeOkinawaProgressStorage storage = null)
        {
            return new OkinawaProgressionStore(storage ?? new FakeOkinawaProgressStorage(), level0 ?? new FakeLevel0Progress());
        }

        [Test]
        public void Lvl0_IsAlwaysUnlocked()
        {
            var store = NewStore();
            Assert.IsTrue(store.IsUnlocked(0));
        }

        [Test]
        public void Lvl0Completion_DerivesFromLevel0Progress_NotADuplicateFlag()
        {
            var level0 = new FakeLevel0Progress();
            var store = NewStore(level0);

            Assert.IsFalse(store.IsComplete(0));

            level0.SetComplete(true);

            Assert.IsTrue(store.IsComplete(0), "OkinawaProgressionStore.IsComplete(0) must reflect the injected ILevel0Progress directly.");
        }

        [Test]
        public void Lvl1And2_RemainLocked_UntilLevel0Completes()
        {
            var level0 = new FakeLevel0Progress();
            var store = NewStore(level0);

            Assert.IsFalse(store.IsUnlocked(1));
            Assert.IsFalse(store.IsUnlocked(2));
        }

        [Test]
        public void Lvl1And2_BothUnlock_AssoonAsLevel0Completes()
        {
            var level0 = new FakeLevel0Progress();
            var store = NewStore(level0);

            level0.SetComplete(true);

            Assert.IsTrue(store.IsUnlocked(1));
            Assert.IsTrue(store.IsUnlocked(2));
        }

        [Test]
        public void Lvl1And2_AreIndependentlyCompletable_InEitherOrder()
        {
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);
            var store = NewStore(level0);

            store.Complete(2);

            Assert.IsTrue(store.IsComplete(2));
            Assert.IsFalse(store.IsComplete(1), "Completing LVL2 first must not affect LVL1.");

            store.Complete(1);
            Assert.IsTrue(store.IsComplete(1));
        }

        [Test]
        public void Lvl3And4_RemainLocked_IfOnlyLvl1IsComplete()
        {
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);
            var store = NewStore(level0);
            store.Complete(1);

            Assert.IsFalse(store.IsUnlocked(3));
            Assert.IsFalse(store.IsUnlocked(4));
        }

        [Test]
        public void Lvl3And4_RemainLocked_IfOnlyLvl2IsComplete()
        {
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);
            var store = NewStore(level0);
            store.Complete(2);

            Assert.IsFalse(store.IsUnlocked(3));
            Assert.IsFalse(store.IsUnlocked(4));
        }

        [Test]
        public void Lvl3And4_BothUnlock_WhenBothLvl1AndLvl2AreComplete()
        {
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);
            var store = NewStore(level0);
            store.Complete(1);
            store.Complete(2);

            Assert.IsTrue(store.IsUnlocked(3));
            Assert.IsTrue(store.IsUnlocked(4));
        }

        [Test]
        public void Lvl5_UnlocksOnlyWhenBothLvl3AndLvl4AreComplete()
        {
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);
            var store = NewStore(level0);
            store.Complete(1);
            store.Complete(2);
            store.Complete(3);

            Assert.IsFalse(store.IsUnlocked(5), "LVL5 must stay locked until BOTH LVL3 and LVL4 are complete.");

            store.Complete(4);
            Assert.IsTrue(store.IsUnlocked(5));
        }

        [Test]
        public void Boss_RemainsLocked_IfAnyPriorRequiredLevelIsIncomplete()
        {
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);
            var store = NewStore(level0);
            store.Complete(1);
            store.Complete(2);
            store.Complete(3);
            store.Complete(4);
            // LVL5 deliberately left incomplete.

            Assert.IsFalse(store.IsUnlocked(6),
                "Boss must require the FULL LVL0-5 set, not just LVL5 — LVL0-4 complete but not 5 must still lock it.");
        }

        [Test]
        public void Boss_UnlocksOnlyOnceTheFullLvl0Through5SetIsComplete()
        {
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);
            var store = NewStore(level0);
            store.Complete(1);
            store.Complete(2);
            store.Complete(3);
            store.Complete(4);
            store.Complete(5);

            Assert.IsTrue(store.IsUnlocked(6));
        }

        [Test]
        public void Complete_OnLvl0_IsANoOp_SinceItIsDerivedNotDirectlySettable()
        {
            var level0 = new FakeLevel0Progress();
            var store = NewStore(level0);

            store.Complete(0);

            Assert.IsFalse(store.IsComplete(0), "LVL0 completion must only ever come from ILevel0Progress, never a direct Complete(0) call.");
        }

        [Test]
        public void Complete_OnALockedLevel_IsANoOp()
        {
            var store = NewStore(); // Level 0 not complete, so LVL3 is locked.

            store.Complete(3);

            Assert.IsFalse(store.IsComplete(3));
        }

        [Test]
        public void Changed_FiresWhenUnderlyingLevel0Changes_EvenWithNoDirectOkinawaMutation()
        {
            var level0 = new FakeLevel0Progress();
            var store = NewStore(level0);
            int changedCount = 0;
            store.Changed += () => changedCount++;

            level0.SetComplete(true);

            Assert.AreEqual(1, changedCount,
                "A single subscription to OkinawaProgressionStore.Changed must also see LVL0-driven unlock changes.");
        }

        [Test]
        public void PersistenceRoundTrip_SurvivesANewStoreInstance()
        {
            var storage = new FakeOkinawaProgressStorage();
            var level0 = new FakeLevel0Progress();
            level0.SetComplete(true);

            var first = NewStore(level0, storage);
            first.Complete(1);
            first.Complete(2);

            var second = NewStore(level0, storage);

            Assert.IsTrue(second.IsComplete(1));
            Assert.IsTrue(second.IsComplete(2));
            Assert.IsTrue(second.IsUnlocked(3));
            Assert.IsTrue(second.IsUnlocked(4));
        }

        [Test]
        public void ConstructorThrows_WhenStorageIsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => new OkinawaProgressionStore(null, new FakeLevel0Progress()));
        }

        [Test]
        public void Lvl0Complete_DoesNotThrow_WhenLevel0ProgressIsNull()
        {
            var store = new OkinawaProgressionStore(new FakeOkinawaProgressStorage(), null);

            Assert.IsFalse(store.IsComplete(0));
            Assert.IsFalse(store.IsUnlocked(1));
        }
    }
}
