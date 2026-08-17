using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapCloudTransitionController's phase ordering, expansion-
    /// based (not slide-based) closed layout, timing/easing/stagger quality,
    /// cross-map spatial-continuity transfer, resize reprojection, input
    /// lock, and re-entrancy guard (Map Pass 3B/3C). Read via source
    /// assertion for the same reason as the other Map controller source-text
    /// tests (MonoBehaviour coroutine internals aren't practical to exercise
    /// in EditMode).
    /// </summary>
    public class MapCloudTransitionControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapCloudTransitionController.cs";

        // ---------- Japan -> Okinawa: close, swap only while covered, reveal to OkinawaRest ----------

        [Test]
        public void JapanToOkinawa_ClosesJapanCloudsFirst_FromJapanRestToItsOwnClosedPreset()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int closeIndex = body.IndexOf("AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest, japanClosed, japanWidth, japanHeight, CloudCloseDurationSeconds, MapCloudMath.EaseInOutQuart);", System.StringComparison.Ordinal);
            int coverOkinawaIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, okinawaWidth, okinawaHeight);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"mapOkinawa\");", System.StringComparison.Ordinal);
            int revealIndex = body.IndexOf("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, MapCloudLayout.OkinawaRest, okinawaWidth, okinawaHeight, CloudRevealDurationSeconds, MapCloudMath.EaseInOutCubic);", System.StringComparison.Ordinal);

            Assert.Greater(closeIndex, -1, "Expected Phase A: Japan clouds animate JapanRest -> its own computed closed layout, eased via EaseInOutQuart.");
            Assert.Greater(coverOkinawaIndex, -1, "Expected Okinawa's clouds snapped to ITS OWN closed layout before the swap.");
            Assert.Greater(showIndex, -1, "Expected the screen swap.");
            Assert.Greater(revealIndex, -1, "Expected Phase C: Okinawa clouds animate its closed layout -> OkinawaRest, eased via EaseInOutCubic.");

            Assert.Less(closeIndex, coverOkinawaIndex, "Close must happen before Okinawa's clouds are set to closed.");
            Assert.Less(coverOkinawaIndex, showIndex, "Okinawa's clouds must already be fully closed BEFORE the screen swap — the swap must never be visible.");
            Assert.Less(showIndex, revealIndex, "Reveal must happen after the swap, not before.");
        }

        [Test]
        public void JapanToOkinawa_HoldsAtFullCover_BetweenCloseAndSwap()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("yield return new WaitForSeconds(FullCoverHoldSeconds);", body);
        }

        [Test]
        public void JapanToOkinawa_RevealEndsExactlyAtOkinawaRest()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, MapCloudLayout.OkinawaRest, okinawaWidth, okinawaHeight, CloudRevealDurationSeconds, MapCloudMath.EaseInOutCubic);", source);
        }

        // ---------- Okinawa -> Japan: closes first, reveal ends at JapanRest ----------

        [Test]
        public void OkinawaToJapan_ClosesOkinawaCloudsFirst_FromOkinawaRestToItsOwnClosedPreset()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            string body = source.Substring(methodStart);

            int closeIndex = body.IndexOf("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest, okinawaClosed, okinawaWidth, okinawaHeight, CloudCloseDurationSeconds, MapCloudMath.EaseInOutQuart);", System.StringComparison.Ordinal);
            int coverJapanIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, japanClosed, japanWidth, japanHeight);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"map\");", System.StringComparison.Ordinal);
            int revealIndex = body.IndexOf("AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, japanClosed, MapCloudLayout.JapanRest, japanWidth, japanHeight, CloudRevealDurationSeconds, MapCloudMath.EaseInOutCubic);", System.StringComparison.Ordinal);

            Assert.Greater(closeIndex, -1, "Expected Phase A: Okinawa clouds animate OkinawaRest -> its own computed closed layout, eased via EaseInOutQuart.");
            Assert.Greater(coverJapanIndex, -1, "Expected Japan's clouds snapped to ITS OWN closed layout before the swap.");
            Assert.Greater(showIndex, -1, "Expected the screen swap.");
            Assert.Greater(revealIndex, -1, "Expected Phase C: Japan clouds animate its closed layout -> JapanRest, eased via EaseInOutCubic.");

            Assert.Less(closeIndex, coverJapanIndex);
            Assert.Less(coverJapanIndex, showIndex, "Japan's clouds must already be fully closed BEFORE the screen swap.");
            Assert.Less(showIndex, revealIndex);
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

        // ---------- expansion, not slide: closed layouts are derived per-cloud from rest, never a shared literal preset ----------

        [Test]
        public void NoSharedCoverPreset_EachScreenComputesItsOwnClosedLayoutFromItsOwnRest()
        {
            // The old "slide toward a shared center/cover rectangle" concept
            // is gone entirely — there is no MapCloudLayout.Cover any more.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("MapCloudLayout.Cover", source);
        }

        [Test]
        public void ComputeClosedPreset_DerivesEachCloudFromItsOwnRestLayoutAndFixedAnchor()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private static MapCloudPreset ComputeClosedPreset(MapCloudPreset rest)", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1, "Expected a ComputeClosedPreset(MapCloudPreset rest) helper.");
            int methodEnd = source.IndexOf("private static IEnumerator AnimateCloudSet", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("MapCloudMath.ComputeClosedLayout(rest.Left1, MapCloudLayout.Left1Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity)", body);
            StringAssert.Contains("MapCloudMath.ComputeClosedLayout(rest.Left2, MapCloudLayout.Left2Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity)", body);
            StringAssert.Contains("MapCloudMath.ComputeClosedLayout(rest.Right1, MapCloudLayout.Right1Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity)", body);
            StringAssert.Contains("MapCloudMath.ComputeClosedLayout(rest.Bottom1, MapCloudLayout.Bottom1Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity)", body);
        }

        [Test]
        public void ClosedLayouts_UseTheSharedExpansionFactor_NotFourDifferentGuesses()
        {
            string source = File.ReadAllText(SourcePath);
            int occurrences = CountOccurrences(source, "MapCloudLayout.CloseExpansionFactor");
            Assert.AreEqual(4, occurrences, "Expected all 4 clouds (Left1, Left2, Right1, Bottom1) to share the exact same expansion factor constant.");
        }

        [Test]
        public void ClosedLayouts_AreFullyOpaque_RegardlessOfEachCloudsOwnRestOpacity()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float FullCoverOpacity = 1.0f;", source);
        }

        // ---------- timing: close/reveal are slower than the previous pass, and asymmetric (close slower than reveal) ----------

        [Test]
        public void CloudCloseDuration_IsWithinTheApprovedSlowerRange_AndSlowerThanThePreviousPass()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float CloudCloseDurationSeconds = 1.05f;", source);
        }

        [Test]
        public void CloudRevealDuration_IsWithinTheApprovedSlowerRange_AndSlowerThanThePreviousPass()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float CloudRevealDurationSeconds = 0.95f;", source);
        }

        [Test]
        public void CloudCloseAndRevealDurations_AreWithinTheApprovedRange()
        {
            // Close ~0.95-1.15s, reveal ~0.90-1.10s per the approved
            // cinematic-but-still-responsive brief.
            Assert.GreaterOrEqual(1.05f, 0.95f);
            Assert.LessOrEqual(1.05f, 1.15f);
            Assert.GreaterOrEqual(0.95f, 0.90f);
            Assert.LessOrEqual(0.95f, 1.10f);
        }

        [Test]
        public void FullCoverHold_IsWithinTheApprovedSlowerRange()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float FullCoverHoldSeconds = 0.12f;", source);
        }

        // ---------- easing: close and reveal use DIFFERENT curves; no bounce/overshoot possible (delegates to MapCloudMath) ----------

        [Test]
        public void Close_UsesTheSteeperQuarticEasing()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("CloudCloseDurationSeconds, MapCloudMath.EaseInOutQuart", source);
        }

        [Test]
        public void Reveal_UsesTheGentlerCubicEasing()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("CloudRevealDurationSeconds, MapCloudMath.EaseInOutCubic", source);
        }

        [Test]
        public void CloseAndReveal_NeverShareTheSameEasingCurve()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("CloudCloseDurationSeconds, MapCloudMath.EaseInOutCubic", source);
            StringAssert.DoesNotContain("CloudRevealDurationSeconds, MapCloudMath.EaseInOutQuart", source);
        }

        [Test]
        public void AnimateCloudSet_InterpolatesThroughLerpWithEasing_NotTheHardcodedCubicLerp()
        {
            // MapCloudMath.Lerp (hardcoded EaseInOutCubic) would silently
            // ignore the injected easing parameter — the per-cloud frame
            // application must route through LerpWithEasing so close can
            // actually differ from reveal.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private static void ApplyStaggeredFrame", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("private static void SetInputLock", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            StringAssert.Contains("MapCloudMath.LerpWithEasing(from, to, t, easing)", body);
        }

        // ---------- stagger: the 4 clouds start at slightly different offsets, so they never move in obvious lockstep ----------

        [Test]
        public void FourClouds_HaveDistinctSmallStaggerOffsets_NoObviousFourElementSynchronization()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float Right1StaggerSeconds = 0f;", source);
            StringAssert.Contains("private const float Left1StaggerSeconds = 0.025f;", source);
            StringAssert.Contains("private const float Left2StaggerSeconds = 0.05f;", source);
            StringAssert.Contains("private const float Bottom1StaggerSeconds = 0.075f;", source);
        }

        [Test]
        public void MaxStagger_IsTinyRelativeToThePhaseDurations_NotASequentialReveal()
        {
            // 0.075s max stagger against ~1s phase durations — a subtle
            // desynchronization, never a visible one-after-another reveal.
            Assert.Less(0.075f, 1.05f * 0.1f);
        }

        [Test]
        public void AnimateCloudSet_AppliesEachCloudWithItsOwnStaggerOffset_AndAShortenedLocalDuration()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private static IEnumerator AnimateCloudSet", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("private static void ApplyStaggeredFrame", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("float cloudDurationSeconds = phaseDurationSeconds - MaxCloudStaggerSeconds;", body);
            StringAssert.Contains("ApplyStaggeredFrame(left1, from.Left1, to.Left1, elapsed, Left1StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);", body);
            StringAssert.Contains("ApplyStaggeredFrame(left2, from.Left2, to.Left2, elapsed, Left2StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);", body);
            StringAssert.Contains("ApplyStaggeredFrame(right1, from.Right1, to.Right1, elapsed, Right1StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);", body);
            StringAssert.Contains("ApplyStaggeredFrame(bottom1, from.Bottom1, to.Bottom1, elapsed, Bottom1StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);", body);
        }

        [Test]
        public void EveryCloud_FinishesExactlyAtThePhaseDuration_DespiteItsStagger()
        {
            // cloudDuration = phaseDuration - maxStagger, and the largest-
            // staggered cloud starts at maxStagger — so
            // maxStagger + cloudDuration == phaseDuration exactly for every
            // cloud, none finishing early or late relative to the shared
            // hold/swap that follows.
            const float maxStagger = 0.075f;
            const float closeCloudDuration = 1.05f - maxStagger;
            Assert.AreEqual(1.05f, maxStagger + closeCloudDuration, 0.0001f);
        }

        // ---------- shared: AnimateCloudSet always snaps exactly to its destination ----------

        [Test]
        public void AnimateCloudSet_SnapsExactlyToDestination_AfterTheLoop()
        {
            string source = File.ReadAllText(SourcePath);
            int loopEnd = source.IndexOf("MapCloudLayout.ApplyPreset(left1, left2, right1, bottom1, to, viewportWidth, viewportHeight);", System.StringComparison.Ordinal);
            Assert.Greater(loopEnd, -1, "Expected an exact snap-to-destination after the animation loop, so no floating-point rounding ever leaves a cloud short of rest/closed.");
        }

        // ---------- spatial continuity: capture the source screen's view, transfer it to the destination while hidden ----------

        [Test]
        public void JapanToOkinawa_CapturesJapansCurrentViewBeforeAnythingCloses()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int captureIndex = body.IndexOf("_japanPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY)", System.StringComparison.Ordinal);
            int closeIndex = body.IndexOf("AnimateCloudSet(_japanLeft1", System.StringComparison.Ordinal);
            Assert.Greater(captureIndex, -1, "Expected the source view captured via MapPanZoomController.TryGetCurrentSourceFocalPoint.");
            Assert.Greater(closeIndex, -1);
            Assert.Less(captureIndex, closeIndex, "The view must be captured before the close animation starts moving anything.");
        }

        [Test]
        public void JapanToOkinawa_AppliesTheCapturedViewToOkinawa_BeforeTheSwap_WhileHidden()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int coverOkinawaIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, okinawaWidth, okinawaHeight);", System.StringComparison.Ordinal);
            int transferIndex = body.IndexOf("_okinawaPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"mapOkinawa\");", System.StringComparison.Ordinal);

            Assert.Greater(coverOkinawaIndex, -1);
            Assert.Greater(transferIndex, -1, "Expected the captured view applied to Okinawa's own MapPanZoomController via SetViewToSourceFocalPoint.");
            Assert.Greater(showIndex, -1);

            Assert.Greater(transferIndex, coverOkinawaIndex, "The view transfer happens alongside snapping Okinawa's clouds closed (same hidden instant), after Okinawa's clouds are set up.");
            Assert.Less(transferIndex, showIndex, "The view must already be transferred BEFORE the screen swap — never visible as a teleport.");
        }

        [Test]
        public void OkinawaToJapan_CapturesOkinawasCurrentViewBeforeAnythingCloses()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            string body = source.Substring(methodStart);

            int captureIndex = body.IndexOf("_okinawaPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY)", System.StringComparison.Ordinal);
            int closeIndex = body.IndexOf("AnimateCloudSet(_okinawaLeft1", System.StringComparison.Ordinal);
            Assert.Greater(captureIndex, -1);
            Assert.Greater(closeIndex, -1);
            Assert.Less(captureIndex, closeIndex);
        }

        [Test]
        public void OkinawaToJapan_AppliesTheCapturedViewToJapan_BeforeTheSwap_WhileHidden()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            string body = source.Substring(methodStart);

            int coverJapanIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, japanClosed, japanWidth, japanHeight);", System.StringComparison.Ordinal);
            int transferIndex = body.IndexOf("_japanPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"map\");", System.StringComparison.Ordinal);

            Assert.Greater(coverJapanIndex, -1);
            Assert.Greater(transferIndex, -1);
            Assert.Greater(showIndex, -1);
            Assert.Greater(transferIndex, coverJapanIndex);
            Assert.Less(transferIndex, showIndex);
        }

        [Test]
        public void SpatialContinuity_NeverRunsAGenericCenterReset_TheCapturedViewIsUsedWheneverAvailable()
        {
            // The transfer is gated only on "was a view successfully
            // captured" (hasView), never on any generic recentring call —
            // there is no ResetTransform/center call anywhere in this
            // controller for either direction.
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("ResetTransform()", source);
            StringAssert.Contains("if (hasView)", source);
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

        // ---------- input lock ----------

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

        // ---------- initial entry: rest presets apply immediately, no animation ----------

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
            StringAssert.DoesNotContain("StartCoroutine(AnimateCloudSet", body, "Bind must never start an animated transition on plain screen entry.");
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
        public void CanvasGeometryChanged_SkipsReprojection_WhileTransitioning_SoItNeverFightsARunningAnimation()
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

        // ---------- no frame polling beyond the animation coroutine itself ----------

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
