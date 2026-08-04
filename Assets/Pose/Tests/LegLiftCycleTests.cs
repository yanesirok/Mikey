using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class LegLiftCycleTests
    {
        [Test]
        public void CompletesCycleOnReturnToGround()
        {
            var c = new LegLiftCycle();
            Assert.IsFalse(c.Update(0.0f, 0.0));
            Assert.IsFalse(c.Update(1.2f, 0.5));           // поднялась
            Assert.AreEqual(LiftPhase.Lifted, c.Phase);
            Assert.IsFalse(c.Update(1.5f, 1.0));
            Assert.IsTrue(c.Update(0.1f, 1.5));            // вернулась — цикл завершён
            Assert.AreEqual(LiftPhase.Grounded, c.Phase);
            Assert.AreEqual(1.0, c.LiftedSeconds, 1e-6);   // подъём длился с 0.5 до 1.5
        }

        [Test]
        public void LiftedSecondsMeasuresFromLiftStart()
        {
            var c = new LegLiftCycle();
            c.Update(0.0f, 0.0);
            c.Update(1.2f, 1.0);                            // старт подъёма
            c.Update(1.4f, 2.0);
            Assert.AreEqual(1.0, c.LiftedSeconds, 1e-6);
            c.Update(0.1f, 3.5);
            Assert.AreEqual(2.5, c.LiftedSeconds, 1e-6);    // длительность завершённого
        }

        [Test]
        public void TooShortLiftDoesNotComplete()
        {
            var c = new LegLiftCycle(minLiftSeconds: 0.2);
            c.Update(0.0f, 0.0);
            c.Update(1.2f, 0.05);
            Assert.IsFalse(c.Update(0.1f, 0.1));            // дрожание, не мах
        }

        [Test]
        public void HysteresisHoldsPhaseBetweenThresholds()
        {
            var c = new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f);
            c.Update(0.0f, 0.0);
            c.Update(0.7f, 0.5);                            // между порогами — всё ещё на полу
            Assert.AreEqual(LiftPhase.Grounded, c.Phase);
            c.Update(1.1f, 1.0);
            c.Update(0.7f, 1.5);                            // между порогами — всё ещё поднята
            Assert.AreEqual(LiftPhase.Lifted, c.Phase);
        }

        [Test]
        public void ResetReturnsToGrounded()
        {
            var c = new LegLiftCycle();
            c.Update(0.0f, 0.0);
            c.Update(1.2f, 1.0);
            c.Reset();
            Assert.AreEqual(LiftPhase.Grounded, c.Phase);
            Assert.AreEqual(0.0, c.LiftedSeconds, 1e-6);
        }
    }
}
