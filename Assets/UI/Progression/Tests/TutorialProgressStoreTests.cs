using NUnit.Framework;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Contract for <see cref="TutorialProgressStore"/>: safe NewPlayer default,
    /// persistence round trip, invalid/corrupted saved-data fallback, forward-only
    /// Advance, unconstrained SetState, and developer Reset — all driven through an
    /// in-memory <see cref="FakeTutorialProgressStorage"/> so no real local storage
    /// is touched by this test run.
    /// </summary>
    public class TutorialProgressStoreTests
    {
        [Test]
        public void DefaultState_IsNewPlayer_WhenNothingWasEverSaved()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            Assert.AreEqual(TutorialProgressState.NewPlayer, store.State);
        }

        [Test]
        public void PersistenceRoundTrip_SurvivesANewStoreInstance()
        {
            var storage = new FakeTutorialProgressStorage();
            var first = new TutorialProgressStore(storage);
            first.SetState(TutorialProgressState.Level1Unlocked);

            // A fresh store over the SAME storage simulates an app/Editor restart.
            var second = new TutorialProgressStore(storage);
            Assert.AreEqual(TutorialProgressState.Level1Unlocked, second.State,
                "A new store instance must load the previously saved state.");
        }

        [TestCase("")]
        [TestCase("not-a-real-state")]
        [TestCase("NewPlayer; DROP TABLE users;")]
        [TestCase("999")]
        public void InvalidOrCorruptedSavedData_FallsBackSafelyToNewPlayer(string corrupted)
        {
            var storage = new FakeTutorialProgressStorage();
            storage.Seed(corrupted);

            var store = new TutorialProgressStore(storage);

            Assert.AreEqual(TutorialProgressState.NewPlayer, store.State,
                $"Corrupted saved value '{corrupted}' must fall back to the safe NewPlayer default.");
        }

        [Test]
        public void SetState_PersistsAndRaisesChanged_OnGenuineChange()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            int changedCount = 0;
            store.Changed += () => changedCount++;

            store.SetState(TutorialProgressState.IntroCompleted);

            Assert.AreEqual(TutorialProgressState.IntroCompleted, store.State);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void SetState_DoesNotRaiseChanged_OnRedundantSameState()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            store.SetState(TutorialProgressState.CombineStarted);

            int changedCount = 0;
            store.Changed += () => changedCount++;
            store.SetState(TutorialProgressState.CombineStarted);

            Assert.AreEqual(0, changedCount, "Re-setting the same state must not re-fire Changed.");
        }

        [Test]
        public void SetState_CanMoveBackward_ForExplicitTransitionsLikeRetryOrDevControls()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            store.SetState(TutorialProgressState.Level1Unlocked);

            store.SetState(TutorialProgressState.IntroCompleted);

            Assert.AreEqual(TutorialProgressState.IntroCompleted, store.State,
                "SetState must allow explicit backward transitions (e.g. Retry Assessment, developer controls).");
        }

        [Test]
        public void Advance_MovesForward()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            store.Advance(TutorialProgressState.CombineStarted);
            Assert.AreEqual(TutorialProgressState.CombineStarted, store.State);
        }

        [Test]
        public void Advance_NeverRegressesAlreadyMadeProgress()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            store.SetState(TutorialProgressState.LessonCompleted);

            store.Advance(TutorialProgressState.LessonStarted);

            Assert.AreEqual(TutorialProgressState.LessonCompleted, store.State,
                "Advance must be a no-op when the target state is not further along than the current one.");
        }

        [Test]
        public void Advance_IsNoOp_OnEqualState_AndDoesNotRaiseChanged()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            store.SetState(TutorialProgressState.CombineStarted);

            int changedCount = 0;
            store.Changed += () => changedCount++;
            store.Advance(TutorialProgressState.CombineStarted);

            Assert.AreEqual(TutorialProgressState.CombineStarted, store.State);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void Reset_ReturnsToNewPlayer_AndClearsStorage()
        {
            var storage = new FakeTutorialProgressStorage();
            var store = new TutorialProgressStore(storage);
            store.SetState(TutorialProgressState.ThirtyDayStreakCompleted);

            store.Reset();

            Assert.AreEqual(TutorialProgressState.NewPlayer, store.State);
            Assert.IsFalse(storage.TryLoad(out _), "Reset must clear the persisted value, not just save NewPlayer.");

            // Restart simulation: a fresh store over the same (now-empty) storage
            // must also come up as NewPlayer, proving the reset actually persisted.
            var afterRestart = new TutorialProgressStore(storage);
            Assert.AreEqual(TutorialProgressState.NewPlayer, afterRestart.State);
        }

        [Test]
        public void Reset_RaisesChanged_WhenNotAlreadyNewPlayer()
        {
            var store = new TutorialProgressStore(new FakeTutorialProgressStorage());
            store.SetState(TutorialProgressState.CombineStarted);

            int changedCount = 0;
            store.Changed += () => changedCount++;
            store.Reset();

            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void NoPaymentOrSensitiveData_OnlyTheStateNameIsPersisted()
        {
            var storage = new FakeTutorialProgressStorage();
            var store = new TutorialProgressStore(storage);
            store.SetState(TutorialProgressState.VowActivated);

            storage.TryLoad(out string raw);
            Assert.AreEqual("VowActivated", raw, "Only the bare enum name is ever persisted.");
        }
    }
}
