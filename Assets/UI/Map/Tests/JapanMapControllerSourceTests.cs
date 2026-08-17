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
        public void ChapterMarkers_PositionIsAppliedFromCentralizedMapMarkerLayout()
        {
            // Coordinates must not be scattered through UXML/USS — only
            // MapMarkerLayout.Chapters (see MapMarkerLayoutTests).
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ApplyChapterPosition(_okinawaNode, OkinawaChapterId);", source);
            StringAssert.Contains("ApplyChapterPosition(_fukuokaNode, FukuokaChapterId);", source);
            StringAssert.Contains("ApplyChapterPosition(_hiroshimaNode, HiroshimaChapterId);", source);
            StringAssert.Contains("MapMarkerLayout.ApplyNormalizedPosition(node, chapter.NormalizedX, chapter.NormalizedY);", source);
        }

        [Test]
        public void EnterChapter_OnlyWorksForOkinawa()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_selectedChapter != OkinawaChapterId || _transitioning)", source);
        }

        [Test]
        public void EnterChapter_PlaysInkFadeTransition_ThenShowsOkinawaChapterScreen()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_transitionOverlay?.AddToClassList(TransitionVisibleClass);", source);
            StringAssert.Contains("yield return new WaitForSeconds(TransitionSeconds);", source);
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
