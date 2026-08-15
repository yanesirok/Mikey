using System.IO;
using NUnit.Framework;
using Mikey.UI.Map;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapLevelPreviewController's navigation constants and its
    /// checkpoint-select / toggle-close / tap-outside-close / Start CTA / leave-Map
    /// wiring. The constants are checked directly (Mikey.UI.Map.Tests references
    /// Mikey.UI.Map); the wiring itself is verified by reading the source,
    /// mirroring ScreenNavigatorTests' established technique for MonoBehaviour
    /// internals not practical to drive through a live UI Toolkit panel in
    /// EditMode.
    /// </summary>
    public class MapLevelPreviewControllerTests
    {
        private const string SourcePath = "Assets/UI/Map/MapLevelPreviewController.cs";

        [Test]
        public void ScreenId_IsMap()
        {
            Assert.AreEqual("map", MapLevelPreviewController.ScreenId);
        }

        [Test]
        public void StatsTarget_IsProfile()
        {
            Assert.AreEqual("profile", MapLevelPreviewController.StatsTarget);
        }

        [Test]
        public void TechniquesTarget_IsTechniques()
        {
            Assert.AreEqual("techniques", MapLevelPreviewController.TechniquesTarget);
        }

        [Test]
        public void SelectCheckpoint_OnAlreadySelectedNode_TogglesPanelClosed()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public void SelectCheckpoint(string nodeElementName)", source);

            int selectMethodStart = source.IndexOf("public void SelectCheckpoint(string nodeElementName)", System.StringComparison.Ordinal);
            int nextMethodStart = source.IndexOf("public void ClosePanel()", selectMethodStart, System.StringComparison.Ordinal);
            Assert.Greater(nextMethodStart, selectMethodStart, "Expected ClosePanel() to follow SelectCheckpoint() in source.");
            string selectMethodBody = source.Substring(selectMethodStart, nextMethodStart - selectMethodStart);

            StringAssert.Contains("if (_selectedNodeName == nodeElementName)", selectMethodBody);
            StringAssert.Contains("ClosePanel();", selectMethodBody,
                "Tapping the already-selected checkpoint again must close the panel instead of reopening it.");
        }

        [Test]
        public void SelectCheckpoint_OnNewNode_OpensPanel_AndMarksItSelected()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_checkpointButtons[nodeElementName].AddToClassList(SelectedNodeClass);", source);
            StringAssert.Contains("_detailPanel?.AddToClassList(PanelOpenClass);", source);
        }

        [Test]
        public void SelectCheckpoint_FallsBackSafely_WhenNoClipBound()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("|| binding.previewClip == null)", source);
            StringAssert.Contains("ShowFallback();", source);
        }

        [Test]
        public void PanelIsHiddenByDefault_OnBind_AndOnEveryMapEntry()
        {
            string source = File.ReadAllText(SourcePath);
            // ClosePanel() (the panel's hidden-by-default state) must run both
            // right after binding and again every time Map is (re-)entered.
            int occurrences = CountOccurrences(source, "ClosePanel();");
            Assert.GreaterOrEqual(occurrences, 2,
                "ClosePanel() must be called both after binding and on entering the Map screen.");
        }

        [Test]
        public void TapOutside_ClosesPanel_ButSkipsTapsInsidePanelOrOnAnyHotspot()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenPointerDown(PointerDownEvent evt)", source);
            StringAssert.Contains("IsSelfOrDescendant(_detailPanel, target)", source,
                "A tap inside the panel itself must not be treated as an outside tap.");
            StringAssert.Contains("IsSelfOrDescendant(hotspot, target)", source,
                "A tap on any hotspot must not be treated as an outside tap — hotspots handle their own taps via SelectCheckpoint.");
            StringAssert.Contains("ClosePanel();", source);
        }

        [Test]
        public void StartCheckpoint_IsANoOp_WhenBelowRequiredState()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void StartCheckpoint(CheckpointBinding binding)", source);
            StringAssert.Contains("if (_progress != null && _progress.State < binding.requiredState)", source);
            StringAssert.Contains("_navigator?.Show(binding.navigationTarget);", source,
                "Each checkpoint's CTA must still route through the existing navigator once unlocked.");
        }

        [Test]
        public void LeavingMapScreen_PausesActivePreview()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenChanged(string screenId)", source);
            StringAssert.Contains("PausePlayback();", source);
        }

        [Test]
        public void OnDisable_UnbindsCheckpointAndActionHandlers_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("node.UnregisterCallback(kvp.Value);", source);
            StringAssert.Contains("_actionBindings[i].Unbind();", source);
            StringAssert.Contains("_actionBindings.Clear();", source);
            StringAssert.Contains("_mapScreen.UnregisterCallback(_outsideClickCallback);", source);
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenChanged;", source);
        }

        private static int CountOccurrences(string source, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }
    }
}
