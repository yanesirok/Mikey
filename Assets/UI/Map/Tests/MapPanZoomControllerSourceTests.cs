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
            // OnScreenChanged now early-returns for a different screen AND
            // (camera refinement pass) for an in-progress cloud transition —
            // see OnScreenChanged_SkipsResetTransform_WhileCloudTransitionIsRunning
            // — rather than nesting the whole body inside a positive check.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenChanged(string changedScreenId)", source);
            StringAssert.Contains("if (changedScreenId != screenId)", source);
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

        // ---------- cloud transition input lock: pan/wheel/pinch must all yield while clouds are closing/revealing ----------

        [Test]
        public void Update_BlocksPinchPolling_WhileCloudTransitionIsRunning()
        {
            // Pinch bypasses the UI Toolkit event tree entirely (it polls
            // Touchscreen directly), so picking-mode alone can never block
            // it — the guard has to live here, at the top of Update().
            string source = File.ReadAllText(SourcePath);
            int updateStart = source.IndexOf("private void Update()", System.StringComparison.Ordinal);
            int updateEnd = source.IndexOf("private void OnPointerDown", System.StringComparison.Ordinal);
            Assert.Greater(updateStart, -1);
            Assert.Greater(updateEnd, updateStart);
            string updateBody = source.Substring(updateStart, updateEnd - updateStart);
            StringAssert.Contains("if (!_bound || MapCloudTransitionController.IsTransitioning)", updateBody);
        }

        [Test]
        public void OnPointerDown_RejectsNewDrags_WhileCloudTransitionIsRunning()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private void OnPointerDown(PointerDownEvent evt)", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void OnPointerMove", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("if (_pointerDown || _isPinching || MapCloudTransitionController.IsTransitioning)", body);
        }

        [Test]
        public void OnWheel_BlocksZoom_AndStopsPropagation_WhileCloudTransitionIsRunning()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private void OnWheel(WheelEvent evt)", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void EndDrag", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int guardIndex = body.IndexOf("if (MapCloudTransitionController.IsTransitioning)", System.StringComparison.Ordinal);
            Assert.Greater(guardIndex, -1, "Expected the guard to be the first check in OnWheel.");
            int guardStopPropagation = body.IndexOf("evt.StopPropagation();", guardIndex, System.StringComparison.Ordinal);
            int guardReturn = body.IndexOf("return;", guardIndex, System.StringComparison.Ordinal);
            Assert.Greater(guardStopPropagation, guardIndex);
            Assert.Greater(guardReturn, guardStopPropagation);
        }

        [Test]
        public void CloudTransitionGuards_CheckTheControllerDirectly_NotOnlyPickingMode()
        {
            // Belt-and-suspenders proof that all three input sources
            // (pinch via Update, drag via OnPointerDown, wheel via OnWheel)
            // check MapCloudTransitionController.IsTransitioning — picking-
            // mode on the cloud layer container is a separate, additional
            // mechanism that only helps with marker taps/outside-tap close.
            string source = File.ReadAllText(SourcePath);
            int occurrences = CountOccurrences(source, "MapCloudTransitionController.IsTransitioning");
            Assert.AreEqual(4, occurrences, "Expected 4 guard sites: Update(), OnPointerDown(), OnWheel(), and (camera refinement pass) OnScreenChanged().");
        }

        // ---------- OnScreenChanged must not fight a transferred cloud-transition view ----------

        [Test]
        public void OnScreenChanged_SkipsResetTransform_WhileCloudTransitionIsRunning()
        {
            // A Japan<->Okinawa cloud transition sets the destination
            // screen's view itself (SetViewToSourceFocalPoint, called by
            // MapCloudTransitionController while hidden) — the normal
            // fresh-entry ResetTransform() must not run mid-transition and
            // overwrite that transferred, spatially-continuous view.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private void OnScreenChanged(string changedScreenId)", System.StringComparison.Ordinal);
            int methodStart2 = source.IndexOf("private void ResetTransform()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            Assert.Greater(methodStart2, methodStart);
            string body = source.Substring(methodStart, methodStart2 - methodStart);

            int guardIndex = body.IndexOf("if (MapCloudTransitionController.IsTransitioning)", System.StringComparison.Ordinal);
            int resetIndex = body.IndexOf("ResetTransform();", System.StringComparison.Ordinal);
            Assert.Greater(guardIndex, -1, "Expected an IsTransitioning guard inside OnScreenChanged.");
            Assert.Greater(resetIndex, -1);
            Assert.Less(guardIndex, resetIndex, "The guard must come before the ResetTransform() call it protects.");
        }

        [Test]
        public void ResetTransform_HasExactlyTwoCallSites_BindAndScreenChanged_UnchangedByTheGuard()
        {
            // The guard added to OnScreenChanged must short-circuit BEFORE
            // calling ResetTransform(), not remove or duplicate the call
            // site itself.
            string source = File.ReadAllText(SourcePath);
            int callCount = CountOccurrences(source, "ResetTransform();");
            Assert.AreEqual(2, callCount);
        }

        // ---------- Japan world map's opening camera focuses on the player's current chapter ----------

        [Test]
        public void JapanWorldScreenId_MatchesTheConfiguredJapanScreenId_Map()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const string JapanWorldScreenId = \"map\";", source);
        }

        [Test]
        public void PlayIntroZoomAnimation_ResolvesChapterFocus_OnlyForTheJapanWorldScreen()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private IEnumerator PlayIntroZoomAnimation()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("private void ApplyZoomTowardChapterFocus", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("screenId == JapanWorldScreenId", body);
            StringAssert.Contains("MapMarkerLayout.TryGetCurrentChapterFocalPoint(out focusSourceX, out focusSourceY)", body);
        }

        [Test]
        public void PlayIntroZoomAnimation_UsesChapterFocusBranch_ButKeepsThePlainCenterPathIntact()
        {
            // The non-focus path (Okinawa's own screen, or Japan if no
            // focal point resolves) must still literally use the original
            // SetZoom(Mathf.LerpUnclamped(...)) calls — proves the existing
            // plain-center opening behavior is preserved unchanged, not
            // replaced outright.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("SetZoom(Mathf.LerpUnclamped(startZoom, targetZoom, t));", source);
            StringAssert.Contains("SetZoom(targetZoom);", source);
            StringAssert.Contains("ApplyZoomTowardChapterFocus(Mathf.LerpUnclamped(startZoom, targetZoom, t), focusSourceX, focusSourceY);", source);
            StringAssert.Contains("ApplyZoomTowardChapterFocus(targetZoom, focusSourceX, focusSourceY);", source);
        }

        [Test]
        public void ApplyZoomTowardChapterFocus_RoutesThroughSourceToViewport_AndPanForTarget()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private void ApplyZoomTowardChapterFocus", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("public bool TryGetCurrentSourceFocalPoint", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("MapCoordinateMapping.SourceToViewport(", body);
            StringAssert.Contains("MapPanZoomMath.PanForTarget(canvasNormalizedX, 0.5f, _zoom, viewportWidth)", body);
            StringAssert.Contains("MapPanZoomMath.PanForTarget(canvasNormalizedY, ChapterFocusTargetNormalizedY, _zoom, viewportHeight)", body);
        }

        [Test]
        public void ChapterFocusTarget_IsBelowDeadCenter_SoTheMarkerNeverSitsUnderTheTopbar()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float ChapterFocusTargetNormalizedY = 0.56f;", source);
        }

        [Test]
        public void ChapterFocus_NeverMutatesStoredMarkerCoordinates_OnlyReadsThem()
        {
            // MapMarkerLayout.TryGetCurrentChapterFocalPoint is a read-only
            // query; nothing in this controller may write back to
            // MapMarkerLayout's chapter data.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("MapMarkerLayout.Chapters[", source);
        }

        // ---------- spatial continuity: capture and reconstruct a view across a Japan<->Okinawa cloud transition ----------

        [Test]
        public void ScreenId_And_CurrentZoom_AreExposedReadOnly_ForMapCloudTransitionController()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public string ScreenId => screenId;", source);
            StringAssert.Contains("public float CurrentZoom => _zoom;", source);
        }

        [Test]
        public void TryGetCurrentSourceFocalPoint_InvertsThroughCanvasNormalizedAtViewportCenter_AndViewportToSource()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public bool TryGetCurrentSourceFocalPoint", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("public void SetViewToSourceFocalPoint", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("MapPanZoomMath.CanvasNormalizedAtViewportCenter(_panX, _zoom, viewportWidth)", body);
            StringAssert.Contains("MapPanZoomMath.CanvasNormalizedAtViewportCenter(_panY, _zoom, viewportHeight)", body);
            StringAssert.Contains("MapCoordinateMapping.ViewportToSource(", body);
            StringAssert.Contains("return false;", body, "Must report failure (not a fabricated point) while not yet bound/laid out.");
        }

        [Test]
        public void TryGetCurrentSourceFocalPoint_RequiresBound_AndAPositiveViewportSize()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public bool TryGetCurrentSourceFocalPoint", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public void SetViewToSourceFocalPoint", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("!_bound || viewportWidth <= 0f || viewportHeight <= 0f", body);
        }

        [Test]
        public void SetViewToSourceFocalPoint_IsPublic_BypassesTheOpeningAnimation_AndClampsZoom()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public void SetViewToSourceFocalPoint(float sourceNormalizedX, float sourceNormalizedY, float zoom)", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1, "Expected a public SetViewToSourceFocalPoint(sourceNormalizedX, sourceNormalizedY, zoom) method.");
            int methodEnd = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("CancelIntroZoomAnimation();", body, "Must cancel any in-flight opening animation rather than fighting it.");
            StringAssert.Contains("MapPanZoomMath.ClampZoom(zoom)", body);
            StringAssert.Contains("MapCoordinateMapping.SourceToViewport(", body);
            StringAssert.Contains("MapPanZoomMath.PanForTarget(canvasNormalizedX, 0.5f, _zoom, viewportWidth)", body);
            StringAssert.Contains("MapPanZoomMath.PanForTarget(canvasNormalizedY, 0.5f, _zoom, viewportHeight)", body, "Spatial continuity targets dead viewport center — never the chapter-focus off-center offset.");
        }

        [Test]
        public void SetViewToSourceFocalPoint_NeverMutatesStoredMarkerOrChapterData()
        {
            // Reading MapMarkerLayout.SourceImageWidth/Height (the shared
            // source-image dimensions, same as every other coordinate
            // conversion in this codebase) is legitimate and required here
            // — what must never appear is a write back into the stored
            // Chapters/Missions arrays themselves.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public void SetViewToSourceFocalPoint(float sourceNormalizedX, float sourceNormalizedY, float zoom)", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.DoesNotContain("MapMarkerLayout.Chapters[", body);
            StringAssert.DoesNotContain("MapMarkerLayout.Missions[", body);
        }

        // ---------- AnimateViewToSourceFocalPoint: the transition's shared smooth camera move (Map Pass 3D) ----------

        [Test]
        public void AnimateViewToSourceFocalPoint_IsPublic_ReturnsAnEnumerator_ForTheCallerToOwnTheCoroutine()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public IEnumerator AnimateViewToSourceFocalPoint(float targetSourceX, float targetSourceY, float targetZoom, float durationSeconds)", source);
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_CancelsAnyInFlightOpeningAnimation()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("private void CancelIntroZoomAnimation()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("CancelIntroZoomAnimation();", body);
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_InterpolatesBothPanAndZoom_FromTheirCurrentValues()
        {
            // Must capture the CURRENT pan/zoom as the interpolation start —
            // a genuine smooth move from wherever the camera already is,
            // never a jump-then-zoom.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void CancelIntroZoomAnimation()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("float startPanX = _panX;", body);
            StringAssert.Contains("float startPanY = _panY;", body);
            StringAssert.Contains("float startZoom = _zoom;", body);
            StringAssert.Contains("Mathf.LerpUnclamped(startZoom, clampedTargetZoom, t)", body);
            StringAssert.Contains("Mathf.LerpUnclamped(startPanX, targetPanX, t)", body);
            StringAssert.Contains("Mathf.LerpUnclamped(startPanY, targetPanY, t)", body);
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_UsesEaseInOutCubic_NotEaseOutCubic()
        {
            // Distinct from the plain opening animation's EaseOutCubic (which
            // assumes a dead-stop start at MinZoom) — this one starts from an
            // arbitrary in-progress view, so it needs a full ease-in-out.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void CancelIntroZoomAnimation()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("MapPanZoomMath.EaseInOutCubic(elapsed / durationSeconds)", body);
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_ComputesTheTargetPanOnce_UsingTheFinalTargetZoom_NotRecomputedEveryFrame()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void CancelIntroZoomAnimation()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int targetComputedIndex = body.IndexOf("MapCoordinateMapping.SourceToViewport(", System.StringComparison.Ordinal);
            int loopIndex = body.IndexOf("while (elapsed < durationSeconds)", System.StringComparison.Ordinal);
            Assert.Greater(targetComputedIndex, -1);
            Assert.Greater(loopIndex, -1);
            Assert.Less(targetComputedIndex, loopIndex, "The target pan must be computed once, BEFORE the animation loop starts.");
            StringAssert.DoesNotContain("MapCoordinateMapping.SourceToViewport(", body.Substring(loopIndex), "SourceToViewport must not be called again inside the loop.");
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_SnapsExactlyToTheTargetAfterTheLoop()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void CancelIntroZoomAnimation()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int loopEnd = body.LastIndexOf("yield return null;", System.StringComparison.Ordinal);
            Assert.Greater(loopEnd, -1);
            string afterLoop = body.Substring(loopEnd);
            StringAssert.Contains("_zoom = clampedTargetZoom;", afterLoop);
            StringAssert.Contains("SetPan(targetPanX, targetPanY);", afterLoop);
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_ClampsTheTargetZoom_NeverExceedsMinOrMaxZoom()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void CancelIntroZoomAnimation()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("float clampedTargetZoom = MapPanZoomMath.ClampZoom(targetZoom);", body);
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_NeverCallsStartCoroutine_TheCallerOwnsTheCoroutine()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator AnimateViewToSourceFocalPoint", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private void CancelIntroZoomAnimation()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.DoesNotContain("StartCoroutine", body);
        }
    }
}
