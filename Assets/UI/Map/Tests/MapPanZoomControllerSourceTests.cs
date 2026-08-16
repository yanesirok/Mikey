using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapPanZoomController's wiring: a drag threshold distinguishes
    /// a marker tap from an intentional pan (pointer capture only begins once the
    /// threshold is crossed, so a plain tap is never swallowed), zoom has both a
    /// wheel (desktop/Editor) and a two-finger pinch source read via the Input
    /// System's Touchscreen (not the legacy UnityEngine.Input class, which this
    /// project has disabled), pan/zoom resets on every fresh entry to its
    /// configured screen, and handlers are unregistered on disable (no leak).
    /// Verified by reading the source, mirroring the established
    /// source-assertion technique used elsewhere in this project.
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
                "Pointer capture must only begin once the drag threshold is crossed, so a plain tap on a marker is never swallowed.");
        }

        [Test]
        public void Zoom_HasWheelSource_ForDesktopAndEditor()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnWheel(WheelEvent evt)", source);
            StringAssert.Contains("SetZoom(_zoom + direction * WheelZoomStep);", source);
        }

        [Test]
        public void WheelZoom_UsesAFixedStep_NotTheRawEventDelta()
        {
            // WheelEvent.delta magnitude for "one notch" varies a lot by mouse/
            // OS/trackpad; only the event's sign is used, with a fixed,
            // predictable step, so two ordinary notches reliably reach
            // approximately DefaultZoom from MinZoom on any device.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float WheelZoomStep = 0.2f;", source);
            StringAssert.Contains("float direction = evt.delta.y < 0f ? 1f : -1f;", source);
            StringAssert.DoesNotContain("evt.delta.y * WheelZoomStep", source,
                "The raw delta magnitude must not scale the zoom step — only its sign should.");
        }

        [Test]
        public void PinchSensitivity_IsIndependentOfWheelZoomStep()
        {
            // Pinch uses a distance ratio (_pinchStartZoom * ratio), a
            // completely separate code path from the wheel's fixed step — so
            // tuning wheel sensitivity can never make pinch more twitchy.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("SetZoom(_pinchStartZoom * ratio);", source);

            int updateStart = source.IndexOf("private void Update()", System.StringComparison.Ordinal);
            int updateEnd = source.IndexOf("private void OnPointerDown", System.StringComparison.Ordinal);
            Assert.Greater(updateStart, -1);
            Assert.Greater(updateEnd, updateStart);
            string pinchRegion = source.Substring(updateStart, updateEnd - updateStart);
            StringAssert.DoesNotContain("WheelZoomStep", pinchRegion);
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
        public void ChangingZoom_ReClampsTheExistingPan()
        {
            // The allowed pan range depends on the current zoom
            // (MapPanZoomMath.MaxPanForZoom) — zooming out must immediately
            // re-clamp any existing pan against the new, smaller range, or a
            // stale pan offset from a higher zoom would expose background.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void SetZoom(float zoom)", source);
            StringAssert.Contains("SetPan(_panX, _panY);", source);
        }

        [Test]
        public void EnteringItsConfiguredScreen_ResetsPanAndZoom()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenChanged(string changedScreenId)", source);
            StringAssert.Contains("if (changedScreenId == screenId)", source);
            StringAssert.Contains("ResetTransform();", source);
        }

        [Test]
        public void ResetTransform_HasExactlyTwoCallSites_BindAndScreenChanged()
        {
            // ResetTransform (and so the opening zoom animation it starts) must
            // only ever run on a fresh entry to this screen — never from an
            // in-screen interaction like opening/closing a popup, selecting
            // another marker, or opening Settings (those live in
            // JapanMapController/OkinawaMapController and never call this
            // MonoBehaviour at all — see their own
            // NeverCallsIntoMapPanZoomController source tests).
            string source = File.ReadAllText(SourcePath);
            int callCount = CountOccurrences(source, "ResetTransform();");
            Assert.AreEqual(2, callCount,
                "Expected exactly two call sites: BindWhenReady's initial bind, and OnScreenChanged's fresh-entry reset.");
        }

        [Test]
        public void OpeningZoomAnimation_StartsAtMinZoom_EndsAtDefaultZoom()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_zoom = MapPanZoomMath.MinZoom;", source,
                "ResetTransform must snap to the legal fully-zoomed-out cover scale before animating in.");
            StringAssert.Contains("float startZoom = MapPanZoomMath.MinZoom;", source);
            StringAssert.Contains("float targetZoom = MapPanZoomMath.DefaultZoom;", source);
            StringAssert.Contains("SetZoom(targetZoom);", source,
                "The animation must settle exactly on DefaultZoom, not an interpolated near-miss.");
        }

        [Test]
        public void OpeningZoomAnimation_UsesEaseOutEasing_AndOnlyEverCallsSetZoom()
        {
            // Every frame of the animation must route through SetZoom (which
            // always clamps via MapPanZoomMath.ClampZoom/ClampPan) — so the
            // animation can never itself produce an out-of-range zoom or a pan
            // that reveals background, by construction.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("MapPanZoomMath.EaseOutCubic(elapsed / IntroZoomDurationSeconds)", source);
            StringAssert.Contains("SetZoom(Mathf.LerpUnclamped(startZoom, targetZoom, t));", source);
        }

        [TestCase("OnWheel")]
        [TestCase("OnPointerMove")]
        public void RealUserInput_CancelsTheOpeningAnimation(string methodName)
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf($"private void {methodName}", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1, $"Expected a {methodName} method.");
            int methodEnd = source.IndexOf("\n        private ", methodStart + 1, System.StringComparison.Ordinal);
            if (methodEnd < 0)
                methodEnd = source.Length;
            string methodBody = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("CancelIntroZoomAnimation();", methodBody,
                $"{methodName} must cancel the opening animation immediately rather than fighting it.");
        }

        [Test]
        public void PinchStarting_CancelsTheOpeningAnimation()
        {
            string source = File.ReadAllText(SourcePath);
            int updateStart = source.IndexOf("private void Update()", System.StringComparison.Ordinal);
            int updateEnd = source.IndexOf("private void OnPointerDown", System.StringComparison.Ordinal);
            Assert.Greater(updateStart, -1);
            Assert.Greater(updateEnd, updateStart);
            string updateBody = source.Substring(updateStart, updateEnd - updateStart);

            int pinchStartIndex = updateBody.IndexOf("_isPinching = true;", System.StringComparison.Ordinal);
            int cancelIndex = updateBody.IndexOf("CancelIntroZoomAnimation();", System.StringComparison.Ordinal);
            Assert.Greater(pinchStartIndex, -1, "Expected the pinch-start branch in Update().");
            Assert.Greater(cancelIndex, pinchStartIndex,
                "CancelIntroZoomAnimation() must be called once pinching is detected as starting.");
        }

        [Test]
        public void CancelledAnimation_IsNeverRequeued()
        {
            // CancelIntroZoomAnimation only stops and clears the routine
            // reference — it must never itself start a new one (that would be
            // "queuing the animation afterward", which the brief forbids).
            string source = File.ReadAllText(SourcePath);
            int cancelStart = source.IndexOf("private void CancelIntroZoomAnimation()", System.StringComparison.Ordinal);
            Assert.Greater(cancelStart, -1);
            string cancelBody = source.Substring(cancelStart, source.Length - cancelStart);
            int nextMethod = cancelBody.IndexOf("\n        private ", 1, System.StringComparison.Ordinal);
            if (nextMethod > 0)
                cancelBody = cancelBody.Substring(0, nextMethod);
            StringAssert.DoesNotContain("StartCoroutine", cancelBody);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) != -1)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        [Test]
        public void ScreenAndElementNames_AreConfigurablePerInstance()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("[SerializeField] private string screenId", source,
                "Screen id must be configurable so one instance can drive the Japan map and another the Okinawa map.");
            StringAssert.Contains("[SerializeField] private string viewportElementName", source);
            StringAssert.Contains("[SerializeField] private string canvasElementName", source);
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
