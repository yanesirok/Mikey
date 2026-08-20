using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Regression coverage for the Play Mode bug where
    /// <c>OkinawaMapController.BindWhenReady()</c> (a coroutine started from its
    /// own OnEnable) reached <see cref="OkinawaProgressionController.IsUnlocked"/>
    /// and <see cref="OkinawaProgressionController.Changed"/> before
    /// <see cref="OkinawaProgressionController.Awake"/> had run on the sibling
    /// component — Unity does not guarantee one component's Awake completes
    /// before another component's OnEnable-driven logic runs, so the backing
    /// store (previously built only in Awake) was still null, throwing a
    /// NullReferenceException and leaving the Okinawa screen blank.
    /// <see cref="Level0ProgressionController"/> shared the identical defect and
    /// sits on the same call path: <see cref="OkinawaProgressionController"/>'s
    /// store construction subscribes to <see cref="Level0ProgressionController.Changed"/>.
    ///
    /// EditMode tests never run Unity's Awake/OnEnable/Start Player-loop
    /// callbacks at all (those only fire in Play Mode or a built player), so a
    /// component added here via <see cref="GameObject.AddComponent{T}"/> is
    /// GUARANTEED to have never had Awake called on it — this is the exact
    /// "component exists, Awake has not run" condition that caused the bug,
    /// made deterministic instead of a timing-dependent race.
    /// </summary>
    public class ProgressionControllerInitializationOrderTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            // The controllers under test read real PlayerPrefs, and this suite
            // specifically exercises them right after interactive Play Mode
            // testing — clear any leftover state first so each test starts
            // from the real, guaranteed-fresh default (only Camera Test
            // available), not whatever was last saved from manual play.
            PlayerPrefs.DeleteKey("Mikey.Level0Progress.CompletedTests");
            PlayerPrefs.DeleteKey("Mikey.OkinawaProgress.CompletedLevels");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            PlayerPrefs.DeleteKey("Mikey.Level0Progress.CompletedTests");
            PlayerPrefs.DeleteKey("Mikey.OkinawaProgress.CompletedLevels");
        }

        [Test]
        public void Level0ProgressionController_PublicApi_IsSafeImmediatelyAfterAddComponent_BeforeAwakeEverRuns()
        {
            _go = new GameObject("Level0ProgressionControllerUnderTest");
            var controller = _go.AddComponent<Level0ProgressionController>();

            // No Awake has run (EditMode never calls it). Every public member
            // must still work, not throw a NullReferenceException.
            Assert.DoesNotThrow(() =>
            {
                _ = controller.IsComplete;
                _ = controller.CompletedCount;
                _ = controller.CurrentTest;
                _ = controller.StateOf(Level0Test.CameraTest);
            });

            int changedCount = 0;
            System.Action handler = () => changedCount++;
            Assert.DoesNotThrow(() => controller.Changed += handler, "Subscribing to Changed before Awake must not throw.");

            Assert.DoesNotThrow(() => controller.Complete(Level0Test.CameraTest));
            Assert.AreEqual(1, changedCount, "The pre-Awake subscription must still receive the real Changed notification.");
            Assert.AreEqual(Level0TestState.Complete, controller.StateOf(Level0Test.CameraTest));

            Assert.DoesNotThrow(() => controller.Changed -= handler);
        }

        [Test]
        public void OkinawaProgressionController_PublicApi_IsSafeImmediatelyAfterAddComponent_BeforeAwakeEverRuns()
        {
            _go = new GameObject("OkinawaProgressionControllerUnderTest");
            // Level0ProgressionController exists as a sibling (Okinawa's store
            // construction reads it via GetComponent) but its own Awake has
            // not run either — reproduces the exact chained scenario from the
            // bug report.
            _go.AddComponent<Level0ProgressionController>();
            var controller = _go.AddComponent<OkinawaProgressionController>();

            Assert.DoesNotThrow(() =>
            {
                _ = controller.IsUnlocked(0);
                _ = controller.IsUnlocked(1);
                _ = controller.IsComplete(0);
            }, "IsUnlocked/IsComplete must be safe to call before Awake has run.");

            int changedCount = 0;
            System.Action handler = () => changedCount++;
            Assert.DoesNotThrow(() => controller.Changed += handler, "Subscribing to Changed before Awake must not throw.");

            Assert.IsTrue(controller.IsUnlocked(0), "LVL0 must always be unlocked.");
            Assert.IsFalse(controller.IsUnlocked(1), "LVL1 must stay locked until Level 0 completes.");

            Assert.DoesNotThrow(() => controller.Changed -= handler);
        }

        [Test]
        public void OkinawaProgressionController_ReflectsLevel0Completion_EvenWhenBothAddedWithNoAwake()
        {
            _go = new GameObject("ChainedProgressionUnderTest");
            var level0 = _go.AddComponent<Level0ProgressionController>();
            var okinawa = _go.AddComponent<OkinawaProgressionController>();

            // Drive Level0 to fully complete, entirely pre-Awake, then confirm
            // Okinawa's derived LVL0 completion and the LVL1/LVL2 pair unlock
            // both observe it correctly — proving the two controllers'
            // lazy-initialized stores are wired to each other correctly
            // regardless of when (or whether yet) either Awake has run.
            level0.Complete(Level0Test.CameraTest);
            level0.Complete(Level0Test.PushUps);
            level0.Complete(Level0Test.Squats);
            level0.Complete(Level0Test.WallSit);
            level0.Complete(Level0Test.YokoGeri);

            Assert.IsTrue(okinawa.IsComplete(0));
            Assert.IsTrue(okinawa.IsUnlocked(1));
            Assert.IsTrue(okinawa.IsUnlocked(2));
        }
    }
}
