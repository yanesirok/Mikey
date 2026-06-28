using NUnit.Framework;

namespace Mikey.UI.CameraTest.Tests
{
    /// <summary>
    /// Regression coverage for the screen-entry reset contract. Exercises the pure
    /// decision the controller makes on navigation — <see cref="CameraTestController.IsCamTestEntry"/>
    /// composed with <see cref="CameraTestRepModel"/> — without needing a live panel.
    /// The controller's OnScreenEntered does exactly: if (IsCamTestEntry(id)) model.Reset().
    /// </summary>
    public class CameraTestEntryTests
    {
        // Mirrors CameraTestController.OnScreenEntered using the real predicate.
        private static void Enter(CameraTestRepModel model, string screenId)
        {
            if (CameraTestController.IsCamTestEntry(screenId))
                model.Reset();
        }

        [Test]
        public void Model_CanBeResetToZero()
        {
            var model = new CameraTestRepModel();
            model.SimulateRep();
            model.Reset();
            Assert.AreEqual(0, model.Reps);
            Assert.AreEqual(CameraFormStatus.Ready, model.Status);
        }

        [Test]
        public void EnteringCamTest_ResetsNonZeroCount()
        {
            var model = new CameraTestRepModel();
            model.SimulateRep();
            model.SimulateRep();
            model.SimulateRep();
            Assert.AreEqual(3, model.Reps);

            Enter(model, "camTest");

            Assert.AreEqual(0, model.Reps, "Entering camTest must reset the count to 0.");
            Assert.AreEqual(CameraFormStatus.Ready, model.Status, "Form must return to Ready on entry.");
            Assert.AreEqual("Align in frame", model.StatusText, "Status copy must return to the initial text.");
        }

        [Test]
        public void EnteringAnotherScreen_DoesNotResetCamTestState()
        {
            var model = new CameraTestRepModel();
            model.SimulateRep();
            model.SimulateRep();

            foreach (var other in new[] { "combine", "menu", "combineIntro", "title", "intro" })
                Enter(model, other);

            Assert.AreEqual(2, model.Reps, "Entering a non-camTest screen must NOT reset the count.");
        }

        [Test]
        public void SimulateRep_StillIncrementsExactlyOnce()
        {
            var model = new CameraTestRepModel();
            model.SimulateRep();
            Assert.AreEqual(1, model.Reps);
            model.SimulateRep();
            Assert.AreEqual(2, model.Reps);
        }

        [Test]
        public void ReEnteringCamTest_ResetsAgain()
        {
            var model = new CameraTestRepModel();

            // First visit: do reps, leave to combine (no reset), come back (reset).
            model.SimulateRep();
            model.SimulateRep();
            Enter(model, "combine");
            Assert.AreEqual(2, model.Reps, "Leaving to combine must not reset.");
            Enter(model, "camTest");
            Assert.AreEqual(0, model.Reps, "Re-entering camTest must reset again.");

            // Second visit: more reps, leave, return — resets once more.
            model.SimulateRep();
            Enter(model, "menu");
            Enter(model, "camTest");
            Assert.AreEqual(0, model.Reps);
        }

        [Test]
        public void IsCamTestEntry_IsExactAndCaseSensitive()
        {
            Assert.IsTrue(CameraTestController.IsCamTestEntry("camTest"));
            Assert.IsFalse(CameraTestController.IsCamTestEntry("combine"));
            Assert.IsFalse(CameraTestController.IsCamTestEntry("camtest"), "Match must be exact/case-sensitive.");
            Assert.IsFalse(CameraTestController.IsCamTestEntry(null));
            Assert.IsFalse(CameraTestController.IsCamTestEntry(""));
        }
    }
}
