using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for JapanMapController's chapter-selection, transition and
    /// top-bar behavior. MonoBehaviour internals driven by real pointer input
    /// aren't practical to exercise in EditMode, so — mirroring this
    /// project's established technique (see the old MapProgressionGatingTests) —
    /// this reads the source and asserts the exact control flow exists.
    /// </summary>
    public class JapanMapControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/JapanMapController.cs";

        [Test]
        public void SelectingOkinawa_OpensPanel_AndStartsItsPreview()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_panel.AddToClassList(PanelOpenClass);", source);
            StringAssert.Contains("PlayOkinawaPreview();", source);
            StringAssert.Contains("_panelTitle.text = \"OKINAWA\";", source);
        }

        [Test]
        public void SameChapterTappedAgain_ClosesPanel()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_selectedChapter == chapterId)", source);
            StringAssert.Contains("ClosePanel();", source);
        }

        [Test]
        public void OutsideTap_ClosesPanel()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnOutsideCatcherPointerDown(PointerDownEvent evt) => ClosePanel();", source);
        }

        [Test]
        public void FukuokaAndHiroshima_OpenLockedPanel_WithDisabledCta()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ShowLockedChapterPanel(\"FUKUOKA\", chapterNumber: 1);", source);
            StringAssert.Contains("ShowLockedChapterPanel(\"HIROSHIMA\", chapterNumber: 2);", source);
            StringAssert.Contains("private void ShowLockedChapterPanel(string displayName, int chapterNumber)", source);
            StringAssert.Contains("_panelCta.SetEnabled(false);", source);
            StringAssert.Contains("_panelCta.AddToClassList(LockedCtaClass);", source);
        }

        [Test]
        public void OnScreenChanged_NeverTriggersACloudTransition_OnlyEnterChapterDoes()
        {
            // Reopening Map (from Stats/Techniques/Settings, or fresh from
            // Main Menu) must never play the close/open cloud sequence —
            // only the explicit "Enter Chapter" action does.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private void OnScreenChanged(string screenId)", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("\n        private ", methodStart + 1, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.DoesNotContain("MapCloudTransitionController", body);
            StringAssert.DoesNotContain("PlayJapanToOkinawa", body);
        }

        [Test]
        public void ChapterMarkers_PositionIsAppliedFromCentralizedMapMarkerLayout()
        {
            // Coordinates must not be scattered through UXML/USS — only
            // MapMarkerLayout.Chapters (see MapMarkerLayoutTests).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ApplyChapterPosition(_okinawaNode, OkinawaChapterId, width, height);", source);
            StringAssert.Contains("ApplyChapterPosition(_fukuokaNode, FukuokaChapterId, width, height);", source);
            StringAssert.Contains("ApplyChapterPosition(_hiroshimaNode, HiroshimaChapterId, width, height);", source);
            StringAssert.Contains("MapMarkerLayout.ApplySourceCoordinate(node, chapter.NormalizedX, chapter.NormalizedY, viewportWidth, viewportHeight);", source);
        }

        [Test]
        public void ChapterMarkerPosition_IsConvertedThroughTheCurrentCanvasSize_NotAppliedAsARawPercentage()
        {
            // The bug this guards against: a source-image-normalized
            // coordinate is not a valid viewport percentage once the map art
            // is displayed with a cover-fit crop (see MapCoordinateMapping).
            // BindWhenReady/resize must read the actual current canvas size
            // and route every chapter through it.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_canvas = _root.Q<VisualElement>(\"map-canvas\");", source);
            StringAssert.Contains("_canvas?.resolvedStyle.width ?? 0f;", source);
            StringAssert.Contains("_canvas?.resolvedStyle.height ?? 0f;", source);
        }

        // ---------- markers stay attached to the same source point across canvas resizes ----------

        [Test]
        public void ChapterMarkers_ReapplyPositionOnCanvasGeometryChange_NotJustOnceAtBind()
        {
            // Root cause of the "moves when Game View is maximized" bug: the
            // canvas's resolved size (whatever ApplyAllChapterPositions reads)
            // can change after bind, and a marker's viewport position must be
            // recomputed from the same stored source coordinate whenever that
            // happens — never left stale from the first bind-time computation.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_canvas?.RegisterCallback<GeometryChangedEvent>(OnCanvasGeometryChanged);", source);
            StringAssert.Contains("private void OnCanvasGeometryChanged(GeometryChangedEvent evt)", source);
            StringAssert.Contains("private void ApplyAllChapterPositions()", source);
            // Both the initial bind-time call and every resize call go through
            // the SAME method, so there is exactly one place stored
            // coordinates are ever turned into a viewport position.
            StringAssert.Contains("ApplyAllChapterPositions();", source);
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
        public void EnterChapter_OnlyWorksForOkinawa()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_selectedChapter != OkinawaChapterId || _transitioning)", source);
        }

        [Test]
        public void EnterChapter_PlaysTheCloudTransition_ThenHasEnteredOkinawaChapterScreen()
        {
            // Map Pass 3B: the old ink-fade swap is replaced by
            // MapCloudTransitionController.PlayJapanToOkinawa(), which owns
            // the Show(OkinawaChapterScreenId) call internally at the correct
            // full-cover moment. The immediate _navigator?.Show(...) here is
            // only the fallback for the (should-never-happen) case where that
            // controller is missing from the scene.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("var cloudTransition = GetComponent<MapCloudTransitionController>();", source);
            StringAssert.Contains("yield return cloudTransition.PlayJapanToOkinawa();", source);
            StringAssert.Contains("_navigator?.Show(OkinawaChapterScreenId);", source);
            StringAssert.Contains("OkinawaChapterScreenId = \"mapOkinawa\";", source);
        }

        [Test]
        public void EnteringMapScreen_ResetsToDefaultState()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (screenId == ScreenId)", source);
            StringAssert.Contains("ResetToDefaultState();", source);
            // ResetToDefaultState itself must close the panel and clear the
            // transition overlay so nothing is stuck from a prior visit. It no
            // longer touches Settings at all — the shared modal (see
            // Mikey.UI.Settings.SettingsModalController) manages its own state
            // independently of any screen.
            StringAssert.Contains("private void ResetToDefaultState()", source);
            StringAssert.Contains("ClosePanel();", source);
            StringAssert.Contains("_transitionOverlay?.RemoveFromClassList(TransitionVisibleClass);", source);
        }

        [Test]
        public void NoLongerOwnsSettings_TheSharedModalControllerDoesInstead()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("MapSettingsModalBinder", source);
            StringAssert.DoesNotContain("_settingsModal", source);
            StringAssert.DoesNotContain("IAudioSettings", source);
        }

        [Test]
        public void TopbarMapButton_ResetsInPlace_EvenWhenAlreadyOnJapan()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnTopbarMapClicked()", source);
            StringAssert.Contains("ResetToDefaultState();", source);
            StringAssert.Contains("_navigator?.Show(ScreenId);", source);
        }

        [Test]
        public void TopbarMapButton_IsTheExplicitReturnToWorldAction_ResetsContextToJapan()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("MapNavigationState.Current = MapContext.JapanWorld;", source);
        }

        [Test]
        public void ReturningToMapScreen_WhileContextIsOkinawa_RedirectsBackToOkinawa_InsteadOfResettingToJapan()
        {
            // Returning to Map from Stats/Techniques/Settings must restore the
            // Okinawa chapter, not throw the player back to the Japan world map.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (MapNavigationState.Current == MapContext.OkinawaChapter)", source);
            StringAssert.Contains("_navigator?.Show(OkinawaChapterScreenId);", source);
        }

        [Test]
        public void ReturningToMapScreen_WhileContextIsJapan_RestoresJapanDefaults()
        {
            // The Japan-context branch (the "else" of the Okinawa redirect)
            // still runs the normal reset — leaving Japan temporarily and
            // coming back restores the Japan world map, not a stale panel.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (screenId == ScreenId)", source);
            StringAssert.Contains("ResetToDefaultState();", source);
        }

        [Test]
        public void NeverCallsIntoMapPanZoomController_SoPopupSelectionCannotResetPanOrZoom()
        {
            // Marker selection / panel open-close is purely local UI state; it
            // must never touch pan/zoom, which only resets on a genuine
            // ScreenManager screen change (MapPanZoomController's own
            // ScreenChanged subscription), not on in-screen interactions. (A
            // bare mention in the class doc-comment, as a "see also" cross-
            // reference, is fine — only real coupling is disallowed.)
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("GetComponent<MapPanZoomController>", source);
            StringAssert.DoesNotContain("MapPanZoomController _", source);
            StringAssert.DoesNotContain("MapPanZoomController.", source);
        }

        [Test]
        public void TechniquesButton_ReusesExistingProgressionGating()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State)", source);
        }

        [Test]
        public void LeavingMapScreen_PausesOkinawaPreview()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("StopOkinawaPreview();", source);
        }

        [Test]
        public void OnDisable_CleansUpVideoPlayerAndRenderTexture_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_okinawaPlayer.Stop();", source);
            StringAssert.Contains("Destroy(_okinawaPlayer.gameObject);", source);
            StringAssert.Contains("_okinawaRenderTexture.Release();", source);
        }
    }
}
