using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapPanZoomController's wiring: a drag threshold distinguishes
    /// a hotspot tap from an intentional pan (pointer capture only begins once the
    /// threshold is crossed, so a plain tap is never swallowed), zoom has both a
    /// wheel (desktop/Editor) and a two-finger pinch source read via the Input
    /// System's Touchscreen (not the legacy UnityEngine.Input class, which this
    /// project has disabled), pan/zoom resets on every fresh Map entry, and
    /// handlers are unregistered on disable (no leak). Verified by reading the
    /// source, mirroring MapLevelPreviewControllerTests' established technique.
    /// </summary>
    public class MapPanZoomControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapPanZoomController.cs";

        [Test]
        public void Pan_UsesDragThreshold_BeforeCapturingThePointer()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("DragThresholdPixels", source);
            StringAssert.Contains("if (delta.sqrMagnitude < DragThresholdPixels * DragThresholdPixels)", source);
            StringAssert.Contains("_viewport.CapturePointer(_activePointerId);", source,
                "Pointer capture must only begin once the drag threshold is crossed, so a plain tap on a hotspot is never swallowed.");
        }

        [Test]
        public void Zoom_HasWheelSource_ForDesktopAndEditor()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnWheel(WheelEvent evt)", source);
            StringAssert.Contains("SetZoom(_zoom + delta);", source);
        }

        [Test]
        public void Zoom_HasPinchSource_ViaInputSystemTouchscreen_NotLegacyInput()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("using UnityEngine.InputSystem;", source);
            StringAssert.Contains("Touchscreen.current", source);
            StringAssert.DoesNotContain("UnityEngine.Input.", source,
                "This project's active input handler is the Input System package; the legacy UnityEngine.Input class is disabled.");
            StringAssert.DoesNotContain("Input.touchCount", source,
                "This project's active input handler is the Input System package; the legacy UnityEngine.Input class is disabled.");
        }

        [Test]
        public void Pinch_CancelsAnyInProgressDrag()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("EndDrag(); // a second finger landing mid-drag means this is a pinch, not a pan.", source);
        }

        [Test]
        public void PanAndZoom_AreClampedThroughMapPanZoomMath()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("MapPanZoomMath.ClampPan(", source);
            StringAssert.Contains("MapPanZoomMath.ClampZoom(", source);
        }

        [Test]
        public void EnteringMapScreen_ResetsPanAndZoom()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenChanged(string screenId)", source);
            StringAssert.Contains("if (screenId == ScreenId)", source);
            StringAssert.Contains("ResetTransform();", source);
        }

        [Test]
        public void OnDisable_UnregistersCallbacks_AndUnsubscribesScreenChanged_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_viewport.UnregisterCallback(_onPointerDown);", source);
            StringAssert.Contains("_viewport.UnregisterCallback(_onPointerMove);", source);
            StringAssert.Contains("_viewport.UnregisterCallback(_onPointerUp);", source);
            StringAssert.Contains("_viewport.UnregisterCallback(_onWheel);", source);
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenChanged;", source);
        }
    }
}
