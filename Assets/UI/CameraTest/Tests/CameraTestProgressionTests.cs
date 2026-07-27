using System.IO;
using NUnit.Framework;

namespace Mikey.UI.CameraTest.Tests
{
    /// <summary>
    /// Contract for the "Starting Combine" progression signal: entering camTest
    /// marks <c>CombineStarted</c> via the forward-only Advance, so re-entering
    /// camTest after progress has already moved past this point (e.g. via
    /// Profile's dock) never regresses it. Verified by reading the source,
    /// mirroring MapLevelPreviewControllerTests' established technique for
    /// MonoBehaviour internals not practical to drive through a live panel in
    /// EditMode.
    /// </summary>
    public class CameraTestProgressionTests
    {
        private const string SourcePath = "Assets/UI/CameraTest/CameraTestController.cs";

        [Test]
        public void EnteringCamTest_AdvancesCombineStarted()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenEntered(string screenId)", source);
            StringAssert.Contains("_progress?.Advance(TutorialProgressState.CombineStarted);", source);
        }

        [Test]
        public void InitialBind_TreatsAlreadyActiveCamTestAsEntry()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("OnScreenEntered(_navigator.CurrentScreen);", source,
                "If camTest is already active when binding finishes, it must be treated as a genuine entry (reset + Advance), not skipped.");
        }
    }
}
