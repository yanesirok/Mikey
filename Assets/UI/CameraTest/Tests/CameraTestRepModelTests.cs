using NUnit.Framework;

namespace Mikey.UI.CameraTest.Tests
{
    /// <summary>
    /// Behavioural contract for the pure, frontend-only <see cref="CameraTestRepModel"/>:
    /// starts at zero, increments on a simulated rep, resets predictably, and derives
    /// deterministic form status / copy from the count. No Unity panel, camera, or
    /// backend involved (mirrors CombineViewModelTests).
    /// </summary>
    public class CameraTestRepModelTests
    {
        [Test]
        public void StartsAtZeroReps_WithReadyStatus()
        {
            var model = new CameraTestRepModel();
            Assert.AreEqual(0, model.Reps, "A fresh model must start at 0 reps.");
            Assert.AreEqual(CameraFormStatus.Ready, model.Status, "0 reps must read as Ready.");
            Assert.AreEqual("Align in frame", model.StatusText);
        }

        [Test]
        public void SimulateRep_IncrementsByOne()
        {
            var model = new CameraTestRepModel();
            model.SimulateRep();
            Assert.AreEqual(1, model.Reps);
            model.SimulateRep();
            Assert.AreEqual(2, model.Reps, "Each SimulateRep must increment the count by exactly one.");
        }

        [Test]
        public void SimulateRep_RaisesChanged()
        {
            var model = new CameraTestRepModel();
            int raised = 0;
            model.Changed += () => raised++;
            model.SimulateRep();
            Assert.AreEqual(1, raised, "SimulateRep must raise Changed so the bound HUD re-renders.");
        }

        [Test]
        public void Reset_ReturnsToZero_FromAnyCount()
        {
            var model = new CameraTestRepModel();
            model.SimulateRep();
            model.SimulateRep();
            model.SimulateRep();
            model.Reset();
            Assert.AreEqual(0, model.Reps, "Reset must return the count to its initial 0.");
            Assert.AreEqual(CameraFormStatus.Ready, model.Status);
        }

        [Test]
        public void Reset_RaisesChanged()
        {
            var model = new CameraTestRepModel();
            int raised = 0;
            model.Changed += () => raised++;
            model.Reset();
            Assert.AreEqual(1, raised, "Reset must raise Changed so the HUD re-renders to the initial state.");
        }

        [Test]
        public void StatusFor_IsDeterministicAtThresholds()
        {
            // 0 → Ready, 1..2 → Adjust (detection settling), 3+ → Good.
            Assert.AreEqual(CameraFormStatus.Ready, CameraTestRepModel.StatusFor(0));
            Assert.AreEqual(CameraFormStatus.Adjust, CameraTestRepModel.StatusFor(1));
            Assert.AreEqual(CameraFormStatus.Adjust, CameraTestRepModel.StatusFor(2));
            Assert.AreEqual(CameraFormStatus.Good, CameraTestRepModel.StatusFor(3));
            Assert.AreEqual(CameraFormStatus.Good, CameraTestRepModel.StatusFor(10));
        }

        [Test]
        public void StatusText_MapsEveryStatus()
        {
            Assert.AreEqual("Align in frame", CameraTestRepModel.StatusTextFor(CameraFormStatus.Ready));
            Assert.AreEqual("Hold steady…", CameraTestRepModel.StatusTextFor(CameraFormStatus.Adjust));
            Assert.AreEqual("Good form", CameraTestRepModel.StatusTextFor(CameraFormStatus.Good));
        }

        [Test]
        public void StatusTracksCount_AsRepsAccumulate()
        {
            var model = new CameraTestRepModel();
            model.SimulateRep(); // 1
            Assert.AreEqual(CameraFormStatus.Adjust, model.Status);
            model.SimulateRep(); // 2
            model.SimulateRep(); // 3
            Assert.AreEqual(CameraFormStatus.Good, model.Status, "Form must read Good once detection locks on at 3 reps.");
            Assert.AreEqual("Good form", model.StatusText);
        }
    }
}
