using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Regression guard for this HUD-only redesign pass: the approved pan/zoom
    /// mechanics (MapPanZoomMath/MapPanZoomController) and every marker's
    /// position must be byte-identical to before it, since this pass is
    /// USS/UXML-only. Real behavioral assertions for the math constants
    /// (directly exercisable), source-text assertions for the controller
    /// constants, and raw-text assertions for marker coordinates in Map.uss.
    /// </summary>
    public class MapMechanicsAndMarkersUnchangedTests
    {
        private const string ControllerSourcePath = "Assets/UI/Map/MapPanZoomController.cs";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        // ---------- approved mechanics: MapPanZoomMath ----------

        [Test]
        public void MinZoom_IsStill1()
        {
            Assert.AreEqual(1f, MapPanZoomMath.MinZoom);
        }

        [Test]
        public void DefaultZoom_IsStillApproximately1Point4()
        {
            Assert.AreEqual(1.4f, MapPanZoomMath.DefaultZoom, 0.0001f);
        }

        [Test]
        public void MaxZoom_IsUnchanged()
        {
            Assert.AreEqual(2.5f, MapPanZoomMath.MaxZoom);
        }

        // ---------- approved mechanics: MapPanZoomController (source-text; MonoBehaviour internals) ----------

        [Test]
        public void WheelZoomStep_IsUnchanged_Point2()
        {
            string source = File.ReadAllText(ControllerSourcePath);
            StringAssert.Contains("private const float WheelZoomStep = 0.2f;", source);
        }

        [Test]
        public void OpeningZoomAnimationDuration_IsUnchanged_Point4Seconds()
        {
            string source = File.ReadAllText(ControllerSourcePath);
            StringAssert.Contains("private const float IntroZoomDurationSeconds = 0.4f;", source);
        }

        [Test]
        public void OpeningAnimation_StillCancelsOnRealInput()
        {
            string source = File.ReadAllText(ControllerSourcePath);
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf("CancelIntroZoomAnimation();", index, System.StringComparison.Ordinal)) != -1)
            {
                count++;
                index += 1;
            }
            // OnWheel, OnPointerMove (drag threshold), Update (pinch start),
            // OnDisable, and the definition itself's early-return guard.
            Assert.GreaterOrEqual(count, 4, "Expected the input-cancellation call sites to remain intact.");
        }

        [Test]
        public void PinchMath_IsStillUnchanged_DistanceRatio()
        {
            string source = File.ReadAllText(ControllerSourcePath);
            StringAssert.Contains("SetZoom(_pinchStartZoom * ratio);", source);
        }

        // ---------- marker coordinates: byte-identical to the final-assets pass ----------

        [TestCase(".chapter-node--okinawa", "left: 30%;", "top: 62%;")]
        [TestCase(".chapter-node--tohoku", "left: 47%;", "top: 30%;")]
        [TestCase(".level-node--0", "left: 22%;", "top: 78%;")]
        [TestCase(".level-node--1", "left: 34%;", "top: 60%;")]
        [TestCase(".level-node--2", "left: 48%;", "top: 68%;")]
        [TestCase(".level-node--3", "left: 60%;", "top: 48%;")]
        [TestCase(".level-node--4", "left: 72%;", "top: 58%;")]
        [TestCase(".level-node--5", "left: 82%;", "top: 36%;")]
        public void MarkerCoordinate_IsUnchanged(string selector, string expectedLeft, string expectedTop)
        {
            string uss = File.ReadAllText(UssPath);
            int start = uss.IndexOf(selector + " {", System.StringComparison.Ordinal);
            if (start < 0)
                start = uss.IndexOf(selector + "{", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, $"Expected a '{selector}' rule in Map.uss.");
            int end = uss.IndexOf('}', start);
            string block = uss.Substring(start, end - start);
            StringAssert.Contains(expectedLeft, block, $"'{selector}' must keep its approved position — marker calibration is out of scope for this pass.");
            StringAssert.Contains(expectedTop, block);
        }

        [Test]
        public void MarkerBadgeAndLabelStyling_IsUnchanged()
        {
            // Spot-checks that marker visuals themselves (badge size, dot,
            // selected/locked states) weren't touched by the HUD redesign.
            string uss = File.ReadAllText(UssPath);
            StringAssert.Contains(".chapter-node__badge {\n    width: 60px;\n    height: 60px;", uss.Replace("\r\n", "\n"));
            StringAssert.Contains(".level-node__badge {\n    width: 52px;\n    height: 52px;", uss.Replace("\r\n", "\n"));
        }
    }
}
