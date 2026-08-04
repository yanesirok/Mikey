using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Unit coverage for the per-frame 3D scorer: elbow angle measurement, the visibility
    /// gate, the push-up-position gate, and the straight-vs-bent form fault.
    /// </summary>
    public class PushUpFormEvaluatorTests
    {
        [Test]
        public void StraightPlank_IsGoodAndInPosition()
        {
            var evaluator = new PushUpFormEvaluator();
            FormAssessment a = evaluator.Evaluate(PoseTestFrames.Build(elbowAngleDeg: 170f, hipOffset: 0f));

            Assert.AreEqual(PushUpFault.None, a.Fault);
            Assert.IsTrue(a.PostureValid);
            Assert.AreEqual(string.Empty, a.Cue);
        }

        [Test]
        public void MeasuresElbowAngle()
        {
            var evaluator = new PushUpFormEvaluator();
            Assert.AreEqual(170f, evaluator.Evaluate(PoseTestFrames.Build(170f)).ElbowAngleDeg, 0.5f);
            Assert.AreEqual(90f, evaluator.Evaluate(PoseTestFrames.Build(90f)).ElbowAngleDeg, 0.5f);
        }

        [Test]
        public void MildBend_IsNotStraightButStillCountable()
        {
            var evaluator = new PushUpFormEvaluator();
            FormAssessment a = evaluator.Evaluate(PoseTestFrames.Build(elbowAngleDeg: 90f, hipOffset: 0.06f));

            Assert.AreEqual(PushUpFault.NotStraight, a.Fault);
            Assert.IsTrue(a.PostureValid, "A bent-but-plank body still counts (form fault only).");
            Assert.AreEqual("Держи тело прямым", a.Cue);
        }

        [Test]
        public void SmallDeviation_StaysGood()
        {
            var evaluator = new PushUpFormEvaluator();
            Assert.AreEqual(PushUpFault.None, evaluator.Evaluate(PoseTestFrames.Build(170f, hipOffset: 0.02f)).Fault);
        }

        [Test]
        public void BentBody_IsNotAPushUpPosition()
        {
            var evaluator = new PushUpFormEvaluator();
            FormAssessment a = evaluator.Evaluate(PoseTestFrames.Build(elbowAngleDeg: 90f, hipOffset: 0.12f));

            Assert.AreEqual(PushUpFault.NotInPosition, a.Fault);
            Assert.IsFalse(a.PostureValid, "A sharply bent body must not count as a push-up.");
            Assert.AreEqual("Прими упор лёжа", a.Cue);
        }

        [Test]
        public void LowVisibility_ReportsNotInFrame()
        {
            var evaluator = new PushUpFormEvaluator();
            FormAssessment a = evaluator.Evaluate(PoseTestFrames.Build(elbowAngleDeg: 170f, visibility: 0.3f));

            Assert.AreEqual(PushUpFault.BodyNotVisible, a.Fault);
            Assert.IsFalse(a.BodyVisible);
            Assert.AreEqual("В кадр", a.Cue);
            Assert.IsNaN(a.ElbowAngleDeg);
        }

        [Test]
        public void OutOfFrameAnkle_ReportsNotVisible()
        {
            var evaluator = new PushUpFormEvaluator();
            // Лодыжка «за кадром» (x > 1) с высокой visibility — как MediaPipe дорисовывает на устройстве.
            FormAssessment a = evaluator.Evaluate(PoseTestFrames.Build(170f, ankleX: 1.05f));
            Assert.AreEqual(PushUpFault.BodyNotVisible, a.Fault);
        }
    }
}
