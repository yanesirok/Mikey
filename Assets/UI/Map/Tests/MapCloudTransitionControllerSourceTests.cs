using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapCloudTransitionController (Map Pass 3D, cleanup): the
    /// Japan&lt;-&gt;Okinawa transition is now a smooth CAMERA pan/zoom move
    /// only — no cloud animation of any kind remains — plus cloud rest
    /// reprojection on resize, input lock, and re-entrancy guard. Read via
    /// source assertion for the same reason as the other Map controller
    /// source-text tests (MonoBehaviour coroutine internals aren't practical
    /// to exercise in EditMode).
    /// </summary>
    public class MapCloudTransitionControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapCloudTransitionController.cs";

        // ---------- no cloud animation remains at all ----------

        [Test]
        public void NoCloudAnimationMethodsRemain()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("AnimateCloudSet", source);
            StringAssert.DoesNotContain("ApplyStaggeredFrame", source);
            StringAssert.DoesNotContain("ComputeClosedPreset", source);
        }

        [Test]
        public void NoCloseExpansionFactor_UsedByActiveTransitionLogic()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("CloseExpansionFactor", source);
            StringAssert.DoesNotContain("ComputeClosedLayout", source);
            StringAssert.DoesNotContain("CloudExpansionAnchor", source);
        }

        [Test]
        public void NoDecorativeCloudOpacityRamp_UsedByActiveTransitionLogic()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("FullCoverOpacity", source);
            StringAssert.DoesNotContain(".style.opacity", source);
        }

        [Test]
        public void NoStaggerOrCloseRevealTimingConstantsRemain()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("StaggerSeconds", source);
            StringAssert.DoesNotContain("CloudCloseDurationSeconds", source);
            StringAssert.DoesNotContain("CloudRevealDurationSeconds", source);
            StringAssert.DoesNotContain("FullCoverHoldSeconds", source);
        }

        [Test]
        public void NoMapCloudMathReference_TheClassWasDeleted()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("MapCloudMath", source);
        }

        [Test]
        public void MapCloudMathClassFile_NoLongerExists()
        {
            Assert.IsFalse(File.Exists("Assets/UI/Map/MapCloudMath.cs"), "MapCloudMath.cs should have been deleted — it existed solely to drive the removed cloud animation.");
        }

        [Test]
        public void PlayJapanToOkinawa_And_PlayOkinawaToJapan_NeverApplyAnyCloudPresetDuringTheTransition()
        {
            // Clouds stay at their fixed rest composition the whole time —
            // the only two call sites for MapCloudLayout.ApplyPreset in this
            // whole file are the bind-time/resize-reprojection helpers
            // (ApplyJapanRest/ApplyOkinawaRest), never inside the transition
            // methods themselves.
            string source = File.ReadAllText(SourcePath);
            int japanToOkinawaStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int japanToOkinawaEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", japanToOkinawaStart, System.StringComparison.Ordinal);
            Assert.Greater(japanToOkinawaStart, -1);
            Assert.Greater(japanToOkinawaEnd, japanToOkinawaStart);
            string japanToOkinawaBody = source.Substring(japanToOkinawaStart, japanToOkinawaEnd - japanToOkinawaStart);
            StringAssert.DoesNotContain("MapCloudLayout.ApplyPreset", japanToOkinawaBody);
            StringAssert.DoesNotContain("MapCloudLayout.Apply(", japanToOkinawaBody);

            int okinawaToJapanEnd = source.IndexOf("private static void SetInputLock", japanToOkinawaEnd, System.StringComparison.Ordinal);
            Assert.Greater(okinawaToJapanEnd, japanToOkinawaEnd);
            string okinawaToJapanBody = source.Substring(japanToOkinawaEnd, okinawaToJapanEnd - japanToOkinawaEnd);
            StringAssert.DoesNotContain("MapCloudLayout.ApplyPreset", okinawaToJapanBody);
            StringAssert.DoesNotContain("MapCloudLayout.Apply(", okinawaToJapanBody);
        }

        // ---------- Japan -> Okinawa: camera approach dives toward the Okinawa marker, then settles deeper ----------

        [Test]
        public void JapanToOkinawa_ApproachDivesTowardTheOkinawaChapterMarker()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            Assert.Greater(methodEnd, methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("MapMarkerLayout.TryGetChapterFocalPoint(MapMarkerLayout.OkinawaChapterId, out float okinawaMarkerX, out float okinawaMarkerY)", body);
            StringAssert.Contains("_japanPanZoom.AnimateViewToSourceFocalPoint(okinawaMarkerX, okinawaMarkerY, TransitionApproachZoom, ApproachDurationSeconds)", body);
        }

        [Test]
        public void JapanToOkinawa_CapturesSourceFocalPointAndZoom_AfterTheApproach()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int approachIndex = body.IndexOf("AnimateViewToSourceFocalPoint(okinawaMarkerX, okinawaMarkerY", System.StringComparison.Ordinal);
            int captureIndex = body.IndexOf("_japanPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY)", System.StringComparison.Ordinal);
            int zoomCaptureIndex = body.IndexOf("_japanPanZoom.CurrentZoom", System.StringComparison.Ordinal);

            Assert.Greater(approachIndex, -1);
            Assert.Greater(captureIndex, -1, "Expected the source focal point captured via TryGetCurrentSourceFocalPoint.");
            Assert.Greater(zoomCaptureIndex, -1, "Expected zoom captured via CurrentZoom.");
            Assert.Less(approachIndex, captureIndex, "Capture must happen AFTER the approach, reading back wherever it actually landed.");
        }

        [Test]
        public void JapanToOkinawa_AppliesTheCapturedViewToOkinawa_BeforeTheSwap()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int transferIndex = body.IndexOf("_okinawaPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"mapOkinawa\");", System.StringComparison.Ordinal);
            Assert.Greater(transferIndex, -1, "Expected the captured view transferred to Okinawa's own MapPanZoomController via SetViewToSourceFocalPoint.");
            Assert.Greater(showIndex, -1);
            Assert.Less(transferIndex, showIndex, "The view must already be reconstructed on Okinawa BEFORE the screen swap.");
        }

        [Test]
        public void JapanToOkinawa_SettlesDeeper_AfterTheSwap()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int showIndex = body.IndexOf("_navigator?.Show(\"mapOkinawa\");", System.StringComparison.Ordinal);
            int settleIndex = body.IndexOf("_okinawaPanZoom.AnimateViewToSourceFocalPoint(focusX, focusY, OkinawaSettleZoom, SettleDurationSeconds)", System.StringComparison.Ordinal);
            Assert.Greater(showIndex, -1);
            Assert.Greater(settleIndex, -1, "Expected the destination to keep animating (settle) toward a deeper zoom at the SAME focal point after the swap.");
            Assert.Less(showIndex, settleIndex, "Settle must happen after the swap, not before.");
        }

        [Test]
        public void OkinawaSettleZoom_IsDeeperThan_TransitionApproachZoom()
        {
            // "Zoomed deeper into the same physical location" requires the
            // settle target to exceed the approach/transfer zoom.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float TransitionApproachZoom = 1.6f;", source);
            StringAssert.Contains("private const float OkinawaSettleZoom = 2.0f;", source);
            Assert.Greater(2.0f, 1.6f);
        }

        // ---------- Okinawa -> Japan: camera captures current view, zooms out, settles further out at the corresponding place ----------

        [Test]
        public void OkinawaToJapan_ApproachZoomsOutInPlace_FromWhereverThePlayerCurrentlyIs()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            string body = source.Substring(methodStart);

            StringAssert.Contains("_okinawaPanZoom.TryGetCurrentSourceFocalPoint(out startFocusX, out startFocusY)", body);
            StringAssert.Contains("_okinawaPanZoom.AnimateViewToSourceFocalPoint(startFocusX, startFocusY, TransitionApproachZoom, ApproachDurationSeconds)", body, "The approach's target focal point must be the SAME point it started from (zoom-only, no pan) — reversing the 'dive in' into a 'pull back in place'.");
        }

        [Test]
        public void OkinawaToJapan_CapturesSourceFocalPointAndZoom_AfterTheApproach()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            string body = source.Substring(methodStart);

            int approachIndex = body.IndexOf("AnimateViewToSourceFocalPoint(startFocusX, startFocusY", System.StringComparison.Ordinal);
            int captureIndex = body.IndexOf("_okinawaPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY)", System.StringComparison.Ordinal);
            Assert.Greater(approachIndex, -1);
            Assert.Greater(captureIndex, -1);
            Assert.Less(approachIndex, captureIndex);
        }

        [Test]
        public void OkinawaToJapan_AppliesTheCapturedViewToJapan_BeforeTheSwap()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            string body = source.Substring(methodStart);

            int transferIndex = body.IndexOf("_japanPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"map\");", System.StringComparison.Ordinal);
            Assert.Greater(transferIndex, -1);
            Assert.Greater(showIndex, -1);
            Assert.Less(transferIndex, showIndex, "The view must already be reconstructed on Japan BEFORE the screen swap.");
        }

        [Test]
        public void OkinawaToJapan_SettlesFurtherOut_TowardJapansOwnDefaultZoom_AfterTheSwap()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            string body = source.Substring(methodStart);

            int showIndex = body.IndexOf("_navigator?.Show(\"map\");", System.StringComparison.Ordinal);
            int settleIndex = body.IndexOf("_japanPanZoom.AnimateViewToSourceFocalPoint(focusX, focusY, MapPanZoomMath.DefaultZoom, SettleDurationSeconds)", System.StringComparison.Ordinal);
            Assert.Greater(showIndex, -1);
            Assert.Greater(settleIndex, -1, "Expected Japan to keep animating (settle) toward its own DefaultZoom at the SAME focal point — never a snap, never a generic center reset.");
            Assert.Less(showIndex, settleIndex);
        }

        [Test]
        public void OkinawaToJapan_SetsMapNavigationStateToJapanWorld_BeforeTheSwap()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            string body = source.Substring(methodStart);
            int contextIndex = body.IndexOf("MapNavigationState.Current = MapContext.JapanWorld;", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"map\");", System.StringComparison.Ordinal);
            Assert.Greater(contextIndex, -1);
            Assert.Greater(showIndex, -1);
            Assert.Less(contextIndex, showIndex);
        }

        // ---------- neither direction ever resets to a generic center ----------

        [Test]
        public void NeitherDirection_EverUsesAGenericCenterReset()
        {
            // No ResetTransform() call anywhere in this controller, and the
            // transfer is always gated on "was a view successfully
            // captured" (hasView), never a fallback to center.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("ResetTransform()", source);
            StringAssert.DoesNotContain("SetPan(0f, 0f)", source);
            StringAssert.DoesNotContain("SetViewToSourceFocalPoint(0f, 0f", source);
        }

        [Test]
        public void ApproachAndSettle_UseSmoothNonLinearInterpolation_ViaMapPanZoomController()
        {
            // The actual easing (EaseInOutCubic, no bounce/overshoot) lives
            // in MapPanZoomController.AnimateViewToSourceFocalPoint /
            // MapPanZoomMath — this just proves every camera move in this
            // controller routes through that one shared method, never a
            // hand-rolled/instant alternative.
            string source = File.ReadAllText(SourcePath);
            int occurrences = CountOccurrences(source, "AnimateViewToSourceFocalPoint(");
            Assert.AreEqual(4, occurrences, "Expected exactly 4 animated camera moves: Japan's approach + Okinawa's settle (PlayJapanToOkinawa), Okinawa's approach + Japan's settle (PlayOkinawaToJapan).");
        }

        // ---------- map swap: no crossfade added (swap already reads clean without one) ----------

        [Test]
        public void NoCrossfadeCode_WasAdded_TheSwapAlreadyReadsCleanWithoutOne()
        {
            // The class remarks document, in prose, the DECISION to omit a
            // crossfade (the word may legitimately appear there) — what
            // must never exist is actual fade-implementing CODE: no new
            // VisualElement queried for a fade overlay, no opacity
            // manipulation of the map art itself.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("Q<VisualElement>(\"map-transition-overlay\")", source);
            StringAssert.DoesNotContain("Q<VisualElement>(\"okinawa-transition-overlay\")", source);
            StringAssert.DoesNotContain(".style.opacity", source);
            StringAssert.DoesNotContain("AddToClassList", source);
        }

        [Test]
        public void NoTopbarElementIsEverQueriedOrManipulated()
        {
            // Class remarks explain (in prose) that input lock doesn't reach
            // the topbar — that's expected and fine. What must never exist
            // is actual CODE querying/animating a topbar element: no
            // Q&lt;...&gt;("...topbar...") lookup anywhere in this file.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("Q<VisualElement>(\"map-topbar", source);
            StringAssert.DoesNotContain("Q<VisualElement>(\"okinawa-topbar", source);
            StringAssert.DoesNotContain("_topbar", source);
        }

        // ---------- re-entrancy: cannot start twice ----------

        [Test]
        public void PlayJapanToOkinawa_GuardsAgainstStartingTwice()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("if (IsTransitioning || !_bound)", body);
            StringAssert.Contains("yield break;", body);
        }

        [Test]
        public void PlayOkinawaToJapan_GuardsAgainstStartingTwice()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            string body = source.Substring(methodStart);
            StringAssert.Contains("if (IsTransitioning || !_bound)", body);
        }

        [Test]
        public void IsTransitioning_IsAPublicStaticFlag_OtherControllersCanCheck()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public static bool IsTransitioning { get; private set; }", source);
        }

        // ---------- input lock: locked at start, restored at end, for BOTH directions ----------

        [Test]
        public void BothDirections_LockInputAtStart_AndRestoreItAtEnd()
        {
            string source = File.ReadAllText(SourcePath);
            int japanStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int japanEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", japanStart, System.StringComparison.Ordinal);
            string japanBody = source.Substring(japanStart, japanEnd - japanStart);
            AssertLocksAndUnlocks(japanBody);

            string okinawaBody = source.Substring(japanEnd);
            AssertLocksAndUnlocks(okinawaBody);
        }

        private static void AssertLocksAndUnlocks(string methodBody)
        {
            StringAssert.Contains("SetInputLock(", methodBody);
            StringAssert.Contains(", true);", methodBody);
            StringAssert.Contains(", false);", methodBody);
        }

        [Test]
        public void SetInputLock_UsesPickingModePosition_WhenLocked_AndIgnore_AtRest()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("cloudLayer.pickingMode = locked ? PickingMode.Position : PickingMode.Ignore;", source);
        }

        [Test]
        public void InputLock_TargetsOnlyTheLayerContainer_NeverIndividualCloudSprites()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("_japanLeft1.pickingMode", source);
            StringAssert.DoesNotContain("_japanRight1.pickingMode", source);
            StringAssert.DoesNotContain("_japanLeft2.pickingMode", source);
            StringAssert.DoesNotContain("_japanBottom1.pickingMode", source);
        }

        // ---------- initial entry: rest presets apply immediately, no animation, no transition ----------

        [Test]
        public void BindWhenReady_AppliesBothRestPresetsImmediately_NoAnimationOnFirstEntry()
        {
            string source = File.ReadAllText(SourcePath);
            int bindStart = source.IndexOf("private IEnumerator BindWhenReady()", System.StringComparison.Ordinal);
            int bindEnd = source.IndexOf("private void OnJapanCanvasGeometryChanged", bindStart, System.StringComparison.Ordinal);
            Assert.Greater(bindStart, -1);
            Assert.Greater(bindEnd, -1);
            string body = source.Substring(bindStart, bindEnd - bindStart);

            StringAssert.Contains("ApplyJapanRest();", body);
            StringAssert.Contains("ApplyOkinawaRest();", body);
            StringAssert.DoesNotContain("PlayJapanToOkinawa()", body);
            StringAssert.DoesNotContain("PlayOkinawaToJapan()", body);
        }

        [Test]
        public void BindWhenReady_LeavesBothCloudLayersUnlocked()
        {
            string source = File.ReadAllText(SourcePath);
            int bindStart = source.IndexOf("private IEnumerator BindWhenReady()", System.StringComparison.Ordinal);
            int bindEnd = source.IndexOf("private void OnJapanCanvasGeometryChanged", bindStart, System.StringComparison.Ordinal);
            string body = source.Substring(bindStart, bindEnd - bindStart);
            StringAssert.Contains("SetInputLock(_japanCloudLayer, false);", body);
            StringAssert.Contains("SetInputLock(_okinawaCloudLayer, false);", body);
        }

        [Test]
        public void BindWhenReady_ResolvesBothMapPanZoomControllers_ByScreenId()
        {
            string source = File.ReadAllText(SourcePath);
            int bindStart = source.IndexOf("private IEnumerator BindWhenReady()", System.StringComparison.Ordinal);
            int bindEnd = source.IndexOf("private void OnJapanCanvasGeometryChanged", bindStart, System.StringComparison.Ordinal);
            Assert.Greater(bindStart, -1);
            Assert.Greater(bindEnd, -1);
            string body = source.Substring(bindStart, bindEnd - bindStart);

            StringAssert.Contains("GetComponents<MapPanZoomController>()", body);
            StringAssert.Contains("controller.ScreenId == \"map\"", body);
            StringAssert.Contains("controller.ScreenId == \"mapOkinawa\"", body);
        }

        // ---------- resize reprojection: resting clouds keep composition on canvas resize, but never fight a running transition ----------

        [Test]
        public void BindWhenReady_RegistersGeometryChanged_OnBothTransformedCanvases_NotOnTheCloudLayers()
        {
            string source = File.ReadAllText(SourcePath);
            int bindStart = source.IndexOf("private IEnumerator BindWhenReady()", System.StringComparison.Ordinal);
            int bindEnd = source.IndexOf("private void OnJapanCanvasGeometryChanged", bindStart, System.StringComparison.Ordinal);
            string body = source.Substring(bindStart, bindEnd - bindStart);
            StringAssert.Contains("_japanCanvas.RegisterCallback<GeometryChangedEvent>(OnJapanCanvasGeometryChanged);", body);
            StringAssert.Contains("_okinawaCanvas.RegisterCallback<GeometryChangedEvent>(OnOkinawaCanvasGeometryChanged);", body);
        }

        [Test]
        public void OnDisable_UnregistersBothCanvasGeometryHandlers()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private void OnDisable()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("private IEnumerator BindWhenReady()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("_japanCanvas?.UnregisterCallback<GeometryChangedEvent>(OnJapanCanvasGeometryChanged);", body);
            StringAssert.Contains("_okinawaCanvas?.UnregisterCallback<GeometryChangedEvent>(OnOkinawaCanvasGeometryChanged);", body);
        }

        [Test]
        public void CanvasGeometryChanged_SkipsReprojection_WhileTransitioning_SoItNeverFightsARunningCameraMove()
        {
            string source = File.ReadAllText(SourcePath);
            int japanStart = source.IndexOf("private void OnJapanCanvasGeometryChanged(GeometryChangedEvent evt)", System.StringComparison.Ordinal);
            int japanEnd = source.IndexOf("private void OnOkinawaCanvasGeometryChanged(GeometryChangedEvent evt)", japanStart, System.StringComparison.Ordinal);
            Assert.Greater(japanStart, -1);
            Assert.Greater(japanEnd, -1);
            string japanBody = source.Substring(japanStart, japanEnd - japanStart);
            StringAssert.Contains("if (IsTransitioning)", japanBody);
            StringAssert.Contains("return;", japanBody);
            StringAssert.Contains("ApplyJapanRest();", japanBody);

            int okinawaEnd = source.IndexOf("private void ApplyJapanRest()", japanEnd, System.StringComparison.Ordinal);
            Assert.Greater(okinawaEnd, -1);
            string okinawaBody = source.Substring(japanEnd, okinawaEnd - japanEnd);
            StringAssert.Contains("if (IsTransitioning)", okinawaBody);
            StringAssert.Contains("ApplyOkinawaRest();", okinawaBody);
        }

        [Test]
        public void ApplyJapanRest_And_ApplyOkinawaRest_ReadTheCanvasOwnResolvedSize()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("float width = _japanCanvas?.resolvedStyle.width ?? 0f;", source);
            StringAssert.Contains("float height = _japanCanvas?.resolvedStyle.height ?? 0f;", source);
            StringAssert.Contains("float width = _okinawaCanvas?.resolvedStyle.width ?? 0f;", source);
            StringAssert.Contains("float height = _okinawaCanvas?.resolvedStyle.height ?? 0f;", source);
        }

        // ---------- no frame polling beyond the animation coroutines themselves ----------

        [Test]
        public void NoUpdateMethod_ResizeOrTransitionIsPurelyEventOrCoroutineDriven()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("private void Update()", source);
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
    }
}
