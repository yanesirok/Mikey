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
            StringAssert.Contains("ApplyMissionLayout(_levelNodes[i], levelIndex, viewportWidth, viewportHeight);", source);
            StringAssert.Contains("MapMarkerLayout.ApplySourceCoordinate(node, mission.NormalizedX, mission.NormalizedY, viewportWidth, viewportHeight);", source);
            StringAssert.Contains("icon.AddToClassList(IconClassFor(mission.Type));", source);
        }

        [Test]
        public void MissionMarkerPosition_IsConvertedThroughTheCurrentViewportSize_NotAppliedAsARawPercentage()
        {
            // Same bug/fix as JapanMapControllerSourceTests — Okinawa's
            // mission map uses the identical cover-fit background art
            // pattern (".okinawa-canvas-art"), so it needs the same
            // conversion (see MapCoordinateMapping).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_root.Q<VisualElement>(\"okinawa-canvas\");", source);
            StringAssert.Contains("canvas?.resolvedStyle.width ?? 0f;", source);
            StringAssert.Contains("canvas?.resolvedStyle.height ?? 0f;", source);
        }

        [Test]
        public void IconClassFor_MapsAllFourMissionTypesToTheirOwnClass()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("case MissionMarkerType.Special:", source);
            StringAssert.Contains("return SpecialIconClass;", source);
            StringAssert.Contains("case MissionMarkerType.Fight:", source);
            StringAssert.Contains("return FightIconClass;", source);
            StringAssert.Contains("case MissionMarkerType.BossFight:", source);
            StringAssert.Contains("return BossFightIconClass;", source);
            StringAssert.Contains("private const string SpecialIconClass = \"level-node__icon--special\";", source);
            StringAssert.Contains("private const string TrainingIconClass = \"level-node__icon--training\";", source);
            StringAssert.Contains("private const string FightIconClass = \"level-node__icon--fight\";", source);
            StringAssert.Contains("private const string BossFightIconClass = \"level-node__icon--boss-fight\";", source);
        }

        [Test]
        public void LevelCount_IsSeven_MatchingOkinawasFinalMvpMissionSet()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const int LevelCount = 7;", source);
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
        public void Levels2Through6_AreAlwaysLocked_NoGameplayYet()
        {
            // LevelCount == 7 (LVL 0-6) means this "default:" case now
            // matches indices 2-6.
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
