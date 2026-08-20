using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Practice.Tests
{
    /// <summary>
    /// Contract for the "first lesson start" and "lesson completion" progression
    /// signals: entering practice marks <c>LessonStarted</c>, and completing the
    /// gated session marks <c>LessonCompleted</c> before returning to Techniques —
    /// both via the forward-only Advance so replaying an already-completed lesson
    /// never regresses progress. Verified by reading the source, mirroring
    /// MapLevelPreviewControllerTests' established technique.
    /// </summary>
    public class PracticeProgressionTests
    {
        private const string SourcePath = "Assets/UI/Practice/PracticeController.cs";

        [Test]
        public void EnteringPractice_AdvancesLessonStarted()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenEntered(string screenId)", source);
            StringAssert.Contains("_progress?.Advance(TutorialProgressState.LessonStarted);", source);
        }

        [Test]
        public void CompletingSession_AdvancesLessonCompleted_OnlyWhenGatedCanCompleteIsTrue()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnCompleteClicked()", source);
            StringAssert.Contains("if (!_model.CanComplete)", source,
                "Completion must remain gated on the session actually being complete.");
            StringAssert.Contains("_progress?.Advance(TutorialProgressState.LessonCompleted);", source);
            StringAssert.Contains("_navigator?.Show(CompleteTarget);", source);
        }

        [Test]
        public void InitialBind_TreatsAlreadyActivePracticeAsEntry()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("OnScreenEntered(_navigator.CurrentScreen);", source,
                "If practice is already active when binding finishes, it must be treated as a genuine entry (reset + Advance), not skipped.");
        }
    }
}
