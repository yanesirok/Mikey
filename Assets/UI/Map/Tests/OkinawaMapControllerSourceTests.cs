using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for OkinawaMapController's level-selection, progression
    /// gating, routing and top-bar behavior. Read via source assertion for
    /// the same reason as JapanMapControllerSourceTests.
    /// </summary>
    public class OkinawaMapControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/OkinawaMapController.cs";

        [Test]
        public void MissionMarkers_PositionAndTypeAreAppliedFromCentralizedMapMarkerLayout()
        {
            // Mission type must not be inferred from a static CSS class baked
            // into MikeyApp.uxml — it's read from MapMarkerLayout.Missions at
            // bind time and applied here (see MapMarkerLayoutTests).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ApplyMissionLayout(_levelNodes[i], levelIndex);", source);
            StringAssert.Contains("MapMarkerLayout.ApplyNormalizedPosition(node, mission.NormalizedX, mission.NormalizedY);", source);
            StringAssert.Contains("mission.Type == MissionMarkerType.Fight ? FightIconClass : TrainingIconClass", source);
        }

        [Test]
        public void Level0_IsAlwaysUnlocked()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.IsMatch(@"case 0:\s*return false;", source);
        }

        [Test]
        public void Level1_GatedByLevel1Unlocked_ViaExistingPresenter()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("TutorialProgressPresenter.IsMapUnlocked(_progress.State)", source);
        }

        [Test]
        public void Levels2Through5_AreAlwaysLocked_NoGameplayYet()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.IsMatch(@"default:\s*return true;", source);
        }

        [Test]
        public void Level0Begin_RoutesToCombineIntro()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("CombineIntroScreenId = \"combineIntro\";", source);
            StringAssert.Contains("_navigator?.Show(CombineIntroScreenId);", source);
        }

        [Test]
        public void Level1Start_RoutesToExistingTechniquesFlow()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("TechniquesScreenId = \"techniques\";", source);
            StringAssert.Contains("_navigator?.Show(TechniquesScreenId);", source);
        }

        [Test]
        public void LockedLevel_CtaClickIsANoOp()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_selectedLevel < 0 || IsLevelLocked(_selectedLevel))", source);
            StringAssert.Contains("return;", source);
        }

        [Test]
        public void SameLevelTappedAgain_ClosesPopup()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_selectedLevel == index)", source);
            StringAssert.Contains("ClosePanel();", source);
        }

        [Test]
        public void OutsideTap_ClosesPopup()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnOutsideCatcherPointerDown(PointerDownEvent evt) => ClosePanel();", source);
        }

        [Test]
        public void EnteringOkinawaScreen_ResetsSelection_AndFadesOutTheIncomingTransitionOverlay()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnEnteredScreen()", source);
            StringAssert.Contains("ClosePanel();", source);
            StringAssert.Contains("_transitionOverlay?.RemoveFromClassList(TransitionVisibleClass);", source);
        }

        [Test]
        public void EnteringOkinawaScreen_RecordsOkinawaAsTheCurrentMapContext()
        {
            // So a later temporary trip to Stats/Techniques/Settings and back
            // restores Okinawa instead of the Japan world map (see
            // JapanMapController.OnScreenChanged).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("MapNavigationState.Current = MapContext.OkinawaChapter;", source);
        }

        [Test]
        public void TopbarMapButton_IsTheExplicitReturnToWorldAction_ResetsContextToJapan_BeforeNavigating()
        {
            // Custom-wired (not a plain "go-" navigator): the context write
            // must happen deterministically before Show() is called, which a
            // second handler on ScreenManager's generically-wired click
            // couldn't guarantee the ordering of.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnTopbarMapClicked()", source);
            StringAssert.Contains("MapNavigationState.Current = MapContext.JapanWorld;", source);
            StringAssert.Contains("_navigator?.Show(JapanMapScreenId);", source);
        }

        [Test]
        public void NeverCallsIntoMapPanZoomController_SoPopupSelectionCannotResetPanOrZoom()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("MapPanZoomController", source);
        }

        [Test]
        public void AlreadyActiveOnBind_StillFadesOutTransitionOverlay()
        {
            // Mirrors the codebase's established defensive check (e.g.
            // MapLevelPreviewController's old SelectDefaultCheckpoint-on-bind):
            // if this screen is somehow already active when binding finishes,
            // the entry behavior must still run once.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_navigator.CurrentScreen == ScreenId)", source);
            StringAssert.Contains("OnEnteredScreen();", source);
        }

        [Test]
        public void ProgressionChanges_RefreshLevelLockStatesLive()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_progress.Changed += OnProgressChanged;", source);
            StringAssert.Contains("private void OnProgressChanged() => RefreshLevelLockStates();", source);
        }

        [Test]
        public void TechniquesButton_ReusesExistingProgressionGating()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State)", source);
        }

        [Test]
        public void NoLongerOwnsSettings_TheSharedModalControllerDoesInstead()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("MapSettingsModalBinder", source);
            StringAssert.DoesNotContain("_settingsModal", source);
            StringAssert.DoesNotContain("IAudioSettings", source);
        }
    }
}
