using NUnit.Framework;
using Mikey.UI.Practice;

namespace Mikey.UI.Practice.Tests
{
    /// <summary>
    /// Behavioural contract for the pure, frontend-only Practice session model
    /// and the controller's entry/navigation constants: deterministic Ready →
    /// InProgress → Complete progression, a single Changed notification per
    /// mutation (no duplicate-subscription smell), predictable reset, and the
    /// completion target screen.
    /// </summary>
    public class PracticeSessionModelTests
    {
        // 16 — model starts Ready, at its baseline, with the completion action unavailable.
        [Test]
        public void StartsReady_AtBaseline()
        {
            var m = new PracticeSessionModel();
            Assert.AreEqual(PracticeState.Ready, m.State);
            Assert.AreEqual(0, m.Step);
            Assert.AreEqual(PracticeSessionModel.BaseScore, m.Score);
            Assert.AreEqual(PracticeCue.Ready, m.Cue);
            Assert.IsFalse(m.IsComplete);
            Assert.IsFalse(m.CanComplete, "Completion must be unavailable before the session completes.");
            Assert.AreEqual("Begin", m.ActionLabel);
        }

        // 17 — a local action (Advance) advances deterministic progress.
        [Test]
        public void Advance_AdvancesDeterministicProgress()
        {
            var m = new PracticeSessionModel();

            m.Advance();
            Assert.AreEqual(PracticeState.InProgress, m.State);
            Assert.AreEqual(1, m.Step);
            Assert.AreEqual("Next", m.ActionLabel);

            int prevScore = m.Score;
            int prevStep = m.Step;
            m.Advance();
            Assert.AreEqual(2, m.Step);
            Assert.Greater(m.Step, prevStep, "Each Advance must move the step forward.");
            Assert.GreaterOrEqual(m.Score, prevScore, "Mock score must not regress as progress advances.");

            // The pure score lookup is monotonic from base to target.
            Assert.AreEqual(PracticeSessionModel.BaseScore, PracticeSessionModel.ScoreFor(0));
            Assert.AreEqual(PracticeSessionModel.TargetScore, PracticeSessionModel.ScoreFor(PracticeSessionModel.StepCount));
            for (int s = 1; s <= PracticeSessionModel.StepCount; s++)
                Assert.GreaterOrEqual(PracticeSessionModel.ScoreFor(s), PracticeSessionModel.ScoreFor(s - 1));
        }

        // 18 — the Complete state is reached correctly after exactly StepCount advances.
        [Test]
        public void ReachesComplete_AfterStepCountAdvances()
        {
            var m = new PracticeSessionModel();
            for (int i = 0; i < PracticeSessionModel.StepCount; i++)
            {
                Assert.IsFalse(m.IsComplete, "Must not complete early.");
                m.Advance();
            }

            Assert.AreEqual(PracticeState.Complete, m.State);
            Assert.IsTrue(m.IsComplete);
            Assert.IsTrue(m.CanComplete, "Completion action becomes available only at completion.");
            Assert.AreEqual(PracticeSessionModel.TargetScore, m.Score);
            Assert.AreEqual("Done", m.ActionLabel);
        }

        // Advancing past completion is an inert no-op (progress never runs past the end).
        [Test]
        public void Advance_AfterComplete_IsNoOp()
        {
            var m = new PracticeSessionModel();
            for (int i = 0; i < PracticeSessionModel.StepCount; i++)
                m.Advance();

            int changes = 0;
            m.Changed += () => changes++;
            m.Advance();

            Assert.AreEqual(PracticeSessionModel.StepCount, m.Step, "Step must not exceed StepCount.");
            Assert.AreEqual(0, changes, "A no-op advance must not re-notify.");
        }

        // 19 — Reset restores the initial state.
        [Test]
        public void Reset_RestoresInitialState()
        {
            var m = new PracticeSessionModel();
            m.Advance();
            m.Advance();

            m.Reset();
            Assert.AreEqual(PracticeState.Ready, m.State);
            Assert.AreEqual(0, m.Step);
            Assert.AreEqual(PracticeSessionModel.BaseScore, m.Score);
            Assert.IsFalse(m.IsComplete);
        }

        // 22 — exactly one Changed per mutation (guards against duplicate subscriptions/double-fire).
        [Test]
        public void EachMutation_RaisesChangedExactlyOnce()
        {
            var m = new PracticeSessionModel();
            int changes = 0;
            m.Changed += () => changes++;

            m.Advance();
            Assert.AreEqual(1, changes, "Advance must raise Changed exactly once.");

            m.Reset();
            Assert.AreEqual(2, changes, "Reset must raise Changed exactly once.");
        }

        // 20 — entering practice resets a previous run (the controller's entry predicate is true for practice).
        [Test]
        public void EnteringPractice_IsAnEntry()
        {
            Assert.IsTrue(PracticeController.IsPracticeEntry(PracticeController.ScreenId));
            Assert.IsTrue(PracticeController.IsPracticeEntry("practice"));
        }

        // 21 — entering another screen does not reset practice.
        [Test]
        public void EnteringAnotherScreen_IsNotAnEntry()
        {
            Assert.IsFalse(PracticeController.IsPracticeEntry("techniques"));
            Assert.IsFalse(PracticeController.IsPracticeEntry("menu"));
            Assert.IsFalse(PracticeController.IsPracticeEntry(null));
        }

        // 24 — completion navigation targets the intended existing screen (Techniques).
        [Test]
        public void CompletionTarget_IsTechniques()
        {
            Assert.AreEqual("techniques", PracticeController.CompleteTarget);
        }
    }
}
