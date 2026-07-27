using System.IO;
using NUnit.Framework;
using Mikey.UI.Map;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapLevelPreviewController's navigation/entry constants and its
    /// checkpoint-select / Start Lesson / leave-Map wiring. The constants are
    /// checked directly (Mikey.UI.Map.Tests references Mikey.UI.Map); the wiring
    /// itself is verified by reading the source, mirroring
    /// ScreenNavigatorTests.NavigatorBindings_AreUnregisteredOnDisable — the same
    /// established technique this codebase uses for MonoBehaviour internals that
    /// aren't practical to drive through a live UI Toolkit panel in EditMode.
    /// </summary>
    public class MapLevelPreviewControllerTests
    {
        private const string SourcePath = "Assets/UI/Map/MapLevelPreviewController.cs";

        // Start Lesson targets the existing 'techniques' screen.
        [Test]
        public void StartLessonTarget_IsTechniques()
        {
            Assert.AreEqual("techniques", MapLevelPreviewController.StartLessonTarget);
        }

        // The controller reacts to the Map screen id (used to pause on leaving it).
        [Test]
        public void ScreenId_IsMap()
        {
            Assert.AreEqual("map", MapLevelPreviewController.ScreenId);
        }

        // Selecting a checkpoint opens the detail panel and marks it the active
        // (red) checkpoint.
        [Test]
        public void SelectCheckpoint_OpensPanel_AndMarksNodeSelected()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public void SelectCheckpoint(", source);
            StringAssert.Contains("_detailPanel?.AddToClassList(OpenClass)", source,
                "Selecting a checkpoint must open the detail panel.");
            StringAssert.Contains("current.AddToClassList(SelectedNodeClass)", source,
                "Selecting a checkpoint must mark its node the active (red) checkpoint.");
        }

        // A checkpoint with no bound preview clip (or a runtime video error) lands
        // on the safe static fallback rather than a broken/blank video.
        [Test]
        public void SelectCheckpoint_FallsBackSafely_WhenNoClipBound()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (binding.previewClip == null)", source);
            StringAssert.Contains("ShowFallback(nodeElementName)", source);
        }

        // "START LESSON" routes through the existing IScreenNavigator, not a
        // bespoke navigation path.
        [Test]
        public void StartLesson_RoutesThroughExistingNavigator()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_navigator?.Show(StartLessonTarget)", source);
        }

        // Leaving the Map screen pauses the active inline preview (no hidden video
        // keeps playing behind another screen).
        [Test]
        public void LeavingMapScreen_PausesActivePreview()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (screenId != ScreenId)", source);
            StringAssert.Contains("PausePlayback()", source);
        }

        // Closing the panel also pauses playback and deselects the checkpoint, so
        // no orphaned "selected" (red) node is left behind once the panel is gone.
        [Test]
        public void ClosePanel_PausesPlayback_AndDeselectsNode()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public void ClosePanel()", source);
            StringAssert.Contains("node.RemoveFromClassList(SelectedNodeClass)", source);
        }

        // Unbinding on disable removes every checkpoint click handler, the panel
        // button handlers and the navigator subscription (no duplicate-subscription
        // leak across enable/disable cycles), mirroring PracticeController/ScreenManager.
        [Test]
        public void OnDisable_UnbindsAllHandlers_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_checkpointClickHandlers.TryGetValue(kvp.Key, out Action handler)", source);
            StringAssert.Contains("kvp.Value.clicked -= handler", source);
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenChanged", source);
        }
    }
}
