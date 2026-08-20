using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Combine.Tests
{
    /// <summary>
    /// Contract for the real Level 0 Combine checklist screen's progression
    /// behavior — sequential test unlock derives entirely from
    /// <see cref="Progression.ILevel0Progress"/> (never five unrelated UI-only
    /// booleans), completing a test never auto-starts the next, and the legacy
    /// "combine-start-lvl1" bridge only fires once Level 0 is fully complete.
    /// Read via source assertion, mirroring the rest of this codebase's
    /// controller-contract tests.
    /// </summary>
    public class CombineProgressionTests
    {
        private const string ControllerPath = "Assets/UI/Combine/CombineScreenController.cs";

        private static string Source() => File.ReadAllText(ControllerPath);

        [Test]
        public void IsCombineEntry_TrueOnlyForCombineScreenId()
        {
            Assert.IsTrue(CombineScreenController.IsCombineEntry("combine"));
            Assert.IsFalse(CombineScreenController.IsCombineEntry("camTest"));
            Assert.IsFalse(CombineScreenController.IsCombineEntry("combineIntro"));
            Assert.IsFalse(CombineScreenController.IsCombineEntry(null));
        }

        [Test]
        public void GenuineEntry_AlwaysReSelectsTheCurrentDefaultTest()
        {
            string source = Source();
            StringAssert.Contains("private void OnScreenEntered(string screenId)", source);
            StringAssert.Contains("if (!IsCombineEntry(screenId))", source);
            StringAssert.Contains("_viewModel.SelectDefault();", source,
                "A genuine Combine entry must re-select the current available (or most recently completed) test.");
        }

        [Test]
        public void StartButton_OnlyActsOnTheCurrentlyAvailableSelectedTest()
        {
            string source = Source();
            StringAssert.Contains("private void OnStartClicked()", source);
            StringAssert.Contains("if (_viewModel.StateOf(test) != Level0TestState.Available)", source);
            StringAssert.Contains("return;", source);
        }

        [Test]
        public void DestinationFor_RoutesEachTestToItsOwnScreen()
        {
            Assert.AreEqual("camTest", CombineScreenController.DestinationFor(Progression.Level0Test.CameraTest));
            Assert.AreEqual("combinePushups", CombineScreenController.DestinationFor(Progression.Level0Test.PushUps));
            Assert.AreEqual("combineSquats", CombineScreenController.DestinationFor(Progression.Level0Test.Squats));
            Assert.AreEqual("combineWallsit", CombineScreenController.DestinationFor(Progression.Level0Test.WallSit));
            Assert.AreEqual("combineYokogeri", CombineScreenController.DestinationFor(Progression.Level0Test.YokoGeri));
        }

        [Test]
        public void CompletingATest_NeverAutoNavigates_TheOnlyNavigationHereIsTheExplicitStartAction()
        {
            // Completion of every one of the five tests happens on OTHER screens
            // (camTest's camera-test-complete, or — for the four not-yet-built
            // tests — nowhere at all, since no button anywhere calls
            // Complete(test) for those). CombineScreenController itself never
            // calls Complete on anything; it only reads state and renders it.
            string source = Source();
            StringAssert.DoesNotContain(".Complete(", source,
                "CombineScreenController must never mark a test complete itself — only the owning test screen (or the real backend) does that.");
        }

        [Test]
        public void LegacyBridge_OnlyFiresOnceLevel0IsFullyComplete()
        {
            string source = Source();
            StringAssert.Contains("private void OnStartLevel1()", source);
            StringAssert.Contains("if (!_viewModel.IsLevel0Complete)", source);
            StringAssert.Contains("return;", source);
        }

        [Test]
        public void LegacyBridge_AdvancesCombineCompletedThenLevel1Unlocked_AndOpensMap()
        {
            string source = Source();
            StringAssert.Contains("_tutorialProgress?.Advance(TutorialProgressState.CombineCompleted);", source);
            StringAssert.Contains("_tutorialProgress?.Advance(TutorialProgressState.Level1Unlocked);", source);
            StringAssert.Contains("_navigator?.Show(\"map\");", source);
        }

        [Test]
        public void LegacyBridgeButton_IsControllerBound_NotAStaticGoNavigator()
        {
            string source = Source();
            StringAssert.Contains("_startLvl1Button.clicked += OnStartLevel1;", source);
        }

        [Test]
        public void RowClicks_SelectThatRowsTest_ViaTheViewModel_WhichGuardsLockedRows()
        {
            string source = Source();
            StringAssert.Contains("_viewModel.Select(test);", source);
        }

        [Test]
        public void OnDisable_UnsubscribesFromScreenChangedAndLevel0Changed_NoLeak()
        {
            string source = Source();
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenEntered;", source);
            StringAssert.Contains("_level0.Changed -= OnLevel0Changed;", source);
        }
    }
}
