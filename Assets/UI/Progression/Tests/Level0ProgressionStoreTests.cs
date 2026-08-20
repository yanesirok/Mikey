using NUnit.Framework;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Contract for <see cref="Level0ProgressionStore"/>: fresh-state gating,
    /// strict sequential unlock, <see cref="Level0ProgressionStore.CurrentTest"/>/
    /// <see cref="Level0ProgressionStore.IsComplete"/> semantics, idempotent/
    /// guarded Complete, corrupted-storage fallback, and persistence round trip —
    /// all driven through an in-memory <see cref="FakeLevel0ProgressStorage"/> so
    /// no real local storage is touched.
    /// </summary>
    public class Level0ProgressionStoreTests
    {
        [Test]
        public void ExactlyFiveTestsExist()
        {
            Assert.AreEqual(5, System.Enum.GetValues(typeof(Level0Test)).Length,
                "Level 0 must define exactly five tests: Camera Test, Push-Ups, Squats, Wall Sit, Yoko-Geri.");
        }

        [Test]
        public void YokoGeri_IsOneTest_NotSplitIntoGedanChudanJodan()
        {
            // The Gedan/Chudan/Jodan sequence is secondary copy on the ONE
            // YokoGeri test, never separate Level0Test members.
            CollectionAssert.DoesNotContain(System.Enum.GetNames(typeof(Level0Test)), "Gedan");
            CollectionAssert.DoesNotContain(System.Enum.GetNames(typeof(Level0Test)), "Chudan");
            CollectionAssert.DoesNotContain(System.Enum.GetNames(typeof(Level0Test)), "Jodan");
            Assert.AreEqual("GEDAN → CHUDAN → JODAN", Level0TestCopy.SecondaryFor(Level0Test.YokoGeri));
        }

        [Test]
        public void FreshState_OnlyCameraTestIsAvailable_EverythingElseLocked()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());

            Assert.AreEqual(Level0TestState.Available, store.StateOf(Level0Test.CameraTest));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.PushUps));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.Squats));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.WallSit));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.YokoGeri));
        }

        [Test]
        public void FreshState_CurrentTestIsCameraTest_AndNothingIsComplete()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());

            Assert.AreEqual(Level0Test.CameraTest, store.CurrentTest);
            Assert.AreEqual(0, store.CompletedCount);
            Assert.IsFalse(store.IsComplete);
        }

        [Test]
        public void CameraTestCompletion_UnlocksPushUps_AndEverythingAfterStaysLocked()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());
            store.Complete(Level0Test.CameraTest);

            Assert.AreEqual(Level0TestState.Complete, store.StateOf(Level0Test.CameraTest));
            Assert.AreEqual(Level0TestState.Available, store.StateOf(Level0Test.PushUps));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.Squats));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.WallSit));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.YokoGeri));
        }

        [Test]
        public void PushUpsCompletion_UnlocksSquats()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());
            store.Complete(Level0Test.CameraTest);
            store.Complete(Level0Test.PushUps);

            Assert.AreEqual(Level0TestState.Complete, store.StateOf(Level0Test.PushUps));
            Assert.AreEqual(Level0TestState.Available, store.StateOf(Level0Test.Squats));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.WallSit));
        }

        [Test]
        public void SquatsCompletion_UnlocksWallSit()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());
            store.Complete(Level0Test.CameraTest);
            store.Complete(Level0Test.PushUps);
            store.Complete(Level0Test.Squats);

            Assert.AreEqual(Level0TestState.Complete, store.StateOf(Level0Test.Squats));
            Assert.AreEqual(Level0TestState.Available, store.StateOf(Level0Test.WallSit));
            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.YokoGeri));
        }

        [Test]
        public void WallSitCompletion_UnlocksYokoGeri()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());
            store.Complete(Level0Test.CameraTest);
            store.Complete(Level0Test.PushUps);
            store.Complete(Level0Test.Squats);
            store.Complete(Level0Test.WallSit);

            Assert.AreEqual(Level0TestState.Complete, store.StateOf(Level0Test.WallSit));
            Assert.AreEqual(Level0TestState.Available, store.StateOf(Level0Test.YokoGeri));
        }

        [Test]
        public void AllFiveComplete_IsCompleteIsTrue_AndCurrentTestIsTheLastOne()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());
            store.Complete(Level0Test.CameraTest);
            store.Complete(Level0Test.PushUps);
            store.Complete(Level0Test.Squats);
            store.Complete(Level0Test.WallSit);
            store.Complete(Level0Test.YokoGeri);

            Assert.IsTrue(store.IsComplete);
            Assert.AreEqual(5, store.CompletedCount);
            Assert.AreEqual(Level0Test.YokoGeri, store.CurrentTest,
                "Once every test is complete, CurrentTest must be the most recently completed (last) test.");
        }

        [Test]
        public void Complete_OnALockedTest_IsANoOp()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());

            // Squats is locked (Camera Test hasn't completed yet) — skipping ahead must not work.
            store.Complete(Level0Test.Squats);

            Assert.AreEqual(Level0TestState.Locked, store.StateOf(Level0Test.Squats));
            Assert.AreEqual(0, store.CompletedCount);
            Assert.AreEqual(Level0Test.CameraTest, store.CurrentTest);
        }

        [Test]
        public void Complete_IsIdempotent_OnAnAlreadyCompleteTest()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());
            store.Complete(Level0Test.CameraTest);

            int changedCount = 0;
            store.Changed += () => changedCount++;
            store.Complete(Level0Test.CameraTest);

            Assert.AreEqual(1, store.CompletedCount);
            Assert.AreEqual(0, changedCount, "Re-completing an already-complete test must not re-fire Changed.");
        }

        [Test]
        public void Complete_RaisesChanged_OnAGenuineCompletion()
        {
            var store = new Level0ProgressionStore(new FakeLevel0ProgressStorage());
            int changedCount = 0;
            store.Changed += () => changedCount++;

            store.Complete(Level0Test.CameraTest);

            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void PersistenceRoundTrip_SurvivesANewStoreInstance()
        {
            var storage = new FakeLevel0ProgressStorage();
            var first = new Level0ProgressionStore(storage);
            first.Complete(Level0Test.CameraTest);
            first.Complete(Level0Test.PushUps);

            // A fresh store over the SAME storage simulates an app/Editor restart.
            var second = new Level0ProgressionStore(storage);

            Assert.AreEqual(Level0TestState.Complete, second.StateOf(Level0Test.CameraTest));
            Assert.AreEqual(Level0TestState.Complete, second.StateOf(Level0Test.PushUps));
            Assert.AreEqual(Level0TestState.Available, second.StateOf(Level0Test.Squats));
        }

        [TestCase("")]
        [TestCase("not-a-real-test")]
        [TestCase("CameraTest; DROP TABLE users;")]
        public void InvalidOrCorruptedSavedData_FallsBackSafelyToFreshState(string corrupted)
        {
            var storage = new FakeLevel0ProgressStorage();
            storage.Seed(corrupted);

            var store = new Level0ProgressionStore(storage);

            Assert.AreEqual(0, store.CompletedCount);
            Assert.AreEqual(Level0Test.CameraTest, store.CurrentTest);
        }

        [Test]
        public void CorruptedTokenMixedWithValidTokens_KeepsOnlyTheValidOnes()
        {
            var storage = new FakeLevel0ProgressStorage();
            storage.Seed("CameraTest,not-a-real-test,PushUps");

            var store = new Level0ProgressionStore(storage);

            Assert.AreEqual(2, store.CompletedCount);
            Assert.AreEqual(Level0TestState.Complete, store.StateOf(Level0Test.CameraTest));
            Assert.AreEqual(Level0TestState.Complete, store.StateOf(Level0Test.PushUps));
        }

        [Test]
        public void Reset_ClearsAllProgress_AndPersistsTheClear()
        {
            var storage = new FakeLevel0ProgressStorage();
            var store = new Level0ProgressionStore(storage);
            store.Complete(Level0Test.CameraTest);
            store.Complete(Level0Test.PushUps);

            store.Reset();

            Assert.AreEqual(0, store.CompletedCount);
            Assert.AreEqual(Level0Test.CameraTest, store.CurrentTest);
            Assert.IsFalse(storage.TryLoad(out _), "Reset must clear the persisted value, not just save an empty set.");
        }

        [Test]
        public void ConstructorThrows_WhenStorageIsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => new Level0ProgressionStore(null));
        }
    }
}
