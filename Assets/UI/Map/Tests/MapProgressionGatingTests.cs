using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for Map's Level-1-unlock gate on Okinawa's "START LESSON" action
    /// (Phase 7): before Level 1 unlocks, a Map visit reached through the
    /// developer route (Profile's still-static go-map) must not let Start Lesson
    /// jump straight to Techniques — the button is disabled and dimmed instead.
    /// Verified by reading the source, mirroring MapLevelPreviewControllerTests'
    /// established technique for MonoBehaviour internals not practical to drive
    /// through a live panel in EditMode.
    /// </summary>
    public class MapProgressionGatingTests
    {
        private const string SourcePath = "Assets/UI/Map/MapLevelPreviewController.cs";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        [Test]
        public void StartLesson_IsANoOp_WhenLevel1NotYetUnlocked()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public void StartLesson()", source);
            StringAssert.Contains("if (_progress != null && !TutorialProgressPresenter.IsMapUnlocked(_progress.State))", source);
            StringAssert.Contains("_navigator?.Show(StartLessonTarget);", source,
                "Start Lesson must still route through the existing navigator once unlocked.");
        }

        [Test]
        public void RefreshStartLessonLock_DisablesAndDimsTheExistingCta()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void RefreshStartLessonLock()", source);
            StringAssert.Contains("_startButton.SetEnabled(unlocked);", source);
            StringAssert.Contains("_startButton.AddToClassList(LockedCtaClass);", source);
        }

        [Test]
        public void LockRefresh_RunsOnBind_OnMapEntry_AndOnProgressChange()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_progress.Changed += RefreshStartLessonLock;", source,
                "A progression change made while already on Map (e.g. a developer control) must refresh the CTA lock.");

            // Called once unconditionally right after binding, and again inside
            // OnScreenChanged's Map-entry branch (alongside SelectDefaultCheckpoint).
            int occurrences = 0;
            int index = 0;
            while ((index = source.IndexOf("RefreshStartLessonLock();", index, System.StringComparison.Ordinal)) >= 0)
            {
                occurrences++;
                index += 1;
            }
            Assert.GreaterOrEqual(occurrences, 2,
                "RefreshStartLessonLock() must be called both after binding and on entering the Map screen.");
        }

        [Test]
        public void OnDisable_UnsubscribesFromProgressChanged_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_progress.Changed -= RefreshStartLessonLock;", source);
        }

        // No Map layout, composition, or asset change — only a new, additive
        // dimming rule for the gating behavior (mirrors .tq-lesson--locked's
        // opacity-only idiom). The pre-existing ".map-detail__cta {" rule itself
        // (checked exhaustively by MapScreenUxmlTests) is untouched.
        [Test]
        public void LockedCtaStyle_IsAdditiveOpacityOnly()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            StringAssert.Contains(".map-detail__cta--locked {", uss);
            StringAssert.Contains("opacity: 0.5;", uss);
        }
    }
}
