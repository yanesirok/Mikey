using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Regression guard for the approved pan/zoom mechanics
    /// (MapPanZoomMath/MapPanZoomController), which stay byte-identical
    /// across the Map Pass 3A marker-calibration work: only marker
    /// presentation (position, icon, type) changed in that pass — see
    /// MapMarkerLayoutTests and MapMarkerAssetsTests for its coverage. Real
    /// behavioral assertions for the math constants (directly exercisable)
    /// and source-text assertions for the controller constants.
    /// </summary>
    public class MapMechanicsAndMarkersUnchangedTests
    {
        private const string ControllerSourcePath = "Assets/UI/Map/MapPanZoomController.cs";

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
    }
}
