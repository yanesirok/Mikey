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
        public void OnEnteredScreen_NeverTriggersACloudTransition_OnlyTheMapButtonDoes()
        {
            // Reopening Okinawa (redirected here from Japan's OnScreenChanged,
            // or fresh) must never play the close/open cloud sequence — only
            // the explicit top-bar "Map" action (leaving Okinawa) does.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private void OnEnteredScreen()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            string body = source.Substring(methodStart);
            int nextBrace = body.IndexOf("\n        }", System.StringComparison.Ordinal);
            Assert.Greater(nextBrace, -1);
            body = body.Substring(0, nextBrace);
            StringAssert.DoesNotContain("MapCloudTransitionController", body);
            StringAssert.DoesNotContain("PlayOkinawaToJapan", body);
        }

        [Test]
        public void MissionMarkers_PositionAndTypeAreAppliedFromCentralizedMapMarkerLayout()
        {
            // Mission type must not be inferred from a static CSS class baked
            // into MikeyApp.uxml — it's read from MapMarkerLayout.Missions at
            // bind time and applied here (see MapMarkerLayoutTests).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ApplyMissionLayout(_levelNodes[i], i, width, height);", source);
            StringAssert.Contains("MapMarkerLayout.ApplySourceCoordinate(node, mission.NormalizedX, mission.NormalizedY, viewportWidth, viewportHeight);", source);
            StringAssert.Contains("icon.AddToClassList(IconClassFor(mission.Type));", source);
        }

        [Test]
        public void MissionMarkerPosition_IsConvertedThroughTheCurrentCanvasSize_NotAppliedAsARawPercentage()
        {
            // Same bug/fix as JapanMapControllerSourceTests — Okinawa's
            // mission map uses the identical cover-fit background art
            // pattern (".okinawa-canvas-art"), so it needs the same
            // conversion (see MapCoordinateMapping).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_canvas = _root.Q<VisualElement>(\"okinawa-canvas\");", source);
            StringAssert.Contains("_canvas?.resolvedStyle.width ?? 0f;", source);
            StringAssert.Contains("_canvas?.resolvedStyle.height ?? 0f;", source);
        }

        // ---------- markers stay attached to the same source point across canvas resizes ----------

        [Test]
        public void MissionMarkers_ReapplyPositionOnCanvasGeometryChange_NotJustOnceAtBind()
        {
            // Same root cause/fix as Japan: the canvas's resolved size can
            // change after bind (Game View maximize/restore, aspect change,
            // device rotation), so every mission marker's position must be
            // recomputed from its stored source coordinate whenever that
            // happens — never left stale from the first bind-time computation.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_canvas?.RegisterCallback<GeometryChangedEvent>(OnCanvasGeometryChanged);", source);
            StringAssert.Contains("private void OnCanvasGeometryChanged(GeometryChangedEvent evt)", source);
            StringAssert.Contains("private void ApplyAllMissionPositions()", source);
            StringAssert.Contains("ApplyAllMissionPositions();", source);
        }

        [Test]
        public void CanvasGeometryChange_IsChangeGated_OnCachedWidthAndHeight()
        {
            // Mirrors SafeAreaController's cache-and-compare pattern — a
            // spurious geometry event that didn't actually resize the canvas
            // must be a cheap no-op, not a redundant reapplication.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private float _lastCanvasWidth;", source);
            StringAssert.Contains("private float _lastCanvasHeight;", source);
            StringAssert.Contains("if (width == _lastCanvasWidth && height == _lastCanvasHeight)", source);
            StringAssert.Contains("return;", source);
        }

        [Test]
        public void CanvasGeometryCallback_IsUnregistered_OnDisable_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_canvas?.UnregisterCallback<GeometryChangedEvent>(OnCanvasGeometryChanged);", source);
        }

        [Test]
        public void ResizeHandling_DoesNotPollEveryFrame_NoUpdateMethodAdded()
        {
            // The fix must be purely event-driven (GeometryChangedEvent), not
            // a MonoBehaviour.Update() loop doing per-frame layout work.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("private void Update()", source);
        }

        [Test]
        public void MarkerClickHandlers_AreWiredOnlyOnceAtBind_NotOnEveryResize()
        {
            // Click-handler wiring must stay a one-time bind-time concern,
            // separate from ApplyAllMissionPositions — resizing must never
            // re-subscribe/duplicate click handlers.
            string source = File.ReadAllText(SourcePath);
            int wireIndex = source.IndexOf("_levelClickHandlers[i] = handler;", System.StringComparison.Ordinal);
            int applyAllIndex = source.IndexOf("private void ApplyAllMissionPositions()", System.StringComparison.Ordinal);
            Assert.Greater(wireIndex, -1, "Expected click-handler wiring in BindWhenReady.");
            Assert.Greater(applyAllIndex, -1, "Expected ApplyAllMissionPositions to exist.");
            StringAssert.DoesNotContain("_levelClickHandlers[i] = handler;", source.Substring(applyAllIndex));
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
        public void LockState_DelegatesEntirelyToOkinawaProgress_NotTheLegacyLinearEnum()
        {
            // The paired-unlock model (IOkinawaProgress) is now the sole
            // authority for LVL 0-6 lock state; the old hardcoded
            // case-0/case-1/default switch (and its dependency on
            // TutorialProgressPresenter.IsMapUnlocked) is gone entirely.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_okinawaProgress == null", source);
            StringAssert.Contains("return index != 0;", source);
            StringAssert.Contains("return !_okinawaProgress.IsUnlocked(index);", source);
            StringAssert.DoesNotContain("TutorialProgressPresenter.IsMapUnlocked", source);
        }

        [Test]
        public void ComingSoonCta_ShownForAnUnlockedLevelWithNoRealDestinationScreen()
        {
            // LVL2-6 have no built mission screen yet — once unlocked they must
            // read as genuinely available (distinct from Locked) but not
            // actionable, rather than fabricating a destination.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private static bool HasRealDestination(int index) => index == 0 || index == 1;", source);
            StringAssert.Contains("bool comingSoon = !locked && !HasRealDestination(index);", source);
            StringAssert.Contains("_panelCtaText.text = \"COMING SOON\";", source);
            StringAssert.Contains("ComingSoonCtaClass", source);
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
        public void TopbarMapButton_PlaysTheCloudTransition_WhichResetsContextToJapan()
        {
            // Map Pass 3B: the old instant swap is replaced by
            // MapCloudTransitionController.PlayOkinawaToJapan(), which sets
            // MapNavigationState.Current = MapContext.JapanWorld and calls
            // Show(JapanMapScreenId) itself at the correct full-cover moment.
            // The direct MapNavigationState/Show calls here are only the
            // fallback for the (should-never-happen) case where that
            // controller is missing from the scene.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnTopbarMapClicked()", source);
            StringAssert.Contains("_cloudTransitionRoutine = StartCoroutine(PlayCloudTransitionThenReturnToJapan());", source);
            StringAssert.Contains("yield return cloudTransition.PlayOkinawaToJapan();", source);
            StringAssert.Contains("MapNavigationState.Current = MapContext.JapanWorld;", source);
            StringAssert.Contains("_navigator?.Show(JapanMapScreenId);", source);
        }

        [Test]
        public void TopbarMapButton_IgnoredWhileACloudTransitionIsAlreadyInFlight()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (MapCloudTransitionController.IsTransitioning)", source);
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
        public void OkinawaProgressChanges_AlsoRefreshLevelLockStatesLive()
        {
            // A single subscription to IOkinawaProgress.Changed must be enough
            // to catch LVL0-6 unlock changes (including ones driven purely by
            // Level 0 Combine progress, forwarded through OkinawaProgressionStore).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_okinawaProgress.Changed += OnProgressChanged;", source);
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
