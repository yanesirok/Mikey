using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapCloudTransitionController's phase ordering, expansion-
    /// based (not slide-based) closed layout, timing, resize reprojection,
    /// input lock, and re-entrancy guard (Map Pass 3B, corrected
    /// architecture). Read via source assertion for the same reason as the
    /// other Map controller source-text tests (MonoBehaviour coroutine
    /// internals aren't practical to exercise in EditMode).
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

            int closeIndex = body.IndexOf("AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest, japanClosed, japanWidth, japanHeight);", System.StringComparison.Ordinal);
            int coverOkinawaIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, okinawaWidth, okinawaHeight);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"mapOkinawa\");", System.StringComparison.Ordinal);
            int revealIndex = body.IndexOf("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, MapCloudLayout.OkinawaRest, okinawaWidth, okinawaHeight);", System.StringComparison.Ordinal);

            Assert.Greater(closeIndex, -1, "Expected Phase A: Japan clouds animate JapanRest -> its own computed closed layout.");
            Assert.Greater(coverOkinawaIndex, -1, "Expected Okinawa's clouds snapped to ITS OWN closed layout before the swap.");
            Assert.Greater(showIndex, -1, "Expected the screen swap.");
            Assert.Greater(revealIndex, -1, "Expected Phase C: Okinawa clouds animate its closed layout -> OkinawaRest.");

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
            StringAssert.Contains("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, MapCloudLayout.OkinawaRest, okinawaWidth, okinawaHeight);", source);
        }

        // ---------- Okinawa -> Japan: closes first, reveal ends at JapanRest ----------

        [Test]
        public void OkinawaToJapan_ClosesOkinawaCloudsFirst_FromOkinawaRestToItsOwnClosedPreset()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            string body = source.Substring(methodStart);

            int closeIndex = body.IndexOf("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest, okinawaClosed, okinawaWidth, okinawaHeight);", System.StringComparison.Ordinal);
            int coverJapanIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, japanClosed, japanWidth, japanHeight);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"map\");", System.StringComparison.Ordinal);
            int revealIndex = body.IndexOf("AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, japanClosed, MapCloudLayout.JapanRest, japanWidth, japanHeight);", System.StringComparison.Ordinal);

            Assert.Greater(closeIndex, -1, "Expected Phase A: Okinawa clouds animate OkinawaRest -> its own computed closed layout.");
            Assert.Greater(coverJapanIndex, -1, "Expected Japan's clouds snapped to ITS OWN closed layout before the swap.");
            Assert.Greater(showIndex, -1, "Expected the screen swap.");
            Assert.Greater(revealIndex, -1, "Expected Phase C: Japan clouds animate its closed layout -> JapanRest.");

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
            int occurrences = 0;
            int index = 0;
            while ((index = source.IndexOf("MapCloudLayout.CloseExpansionFactor", index, System.StringComparison.Ordinal)) != -1)
            {
                occurrences++;
                index += "MapCloudLayout.CloseExpansionFactor".Length;
            }
            Assert.AreEqual(4, occurrences, "Expected all 4 clouds (Left1, Left2, Right1, Bottom1) to share the exact same expansion factor constant.");
        }

        [Test]
        public void ClosedLayouts_AreFullyOpaque_RegardlessOfEachCloudsOwnRestOpacity()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float FullCoverOpacity = 1.0f;", source);
        }

        // ---------- timing: 0.75s close / 0.08s hold / 0.75s reveal, all 4 clouds together (no stagger) ----------

        [Test]
        public void CloudMoveDuration_IsThreeQuartersOfASecond()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float CloudMoveDurationSeconds = 0.75f;", source);
        }

        [Test]
        public void FullCoverHold_IsPointZeroEightSeconds()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const float FullCoverHoldSeconds = 0.08f;", source);
        }

        [Test]
        public void AnimateCloudSet_UsesASingleSharedProgress_ForAllFourClouds_NoPerCloudStaggerDelay()
        {
            // All 4 clouds must begin (and move) together: one "t" computed
            // once per frame and reused for every cloud, not four separately
            // staggered start times.
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("private static IEnumerator AnimateCloudSet", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("private static void SetInputLock", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int tAssignments = CountOccurrences(body, "float t = MapCloudMath.LocalProgress(");
            Assert.AreEqual(1, tAssignments, "Expected exactly one shared progress value per frame, reused by all 4 clouds.");
            StringAssert.DoesNotContain("Stagger", body);
            StringAssert.DoesNotContain("Delay", body);
        }

        // ---------- shared: AnimateCloudSet always snaps exactly to its destination ----------

        [Test]
        public void AnimateCloudSet_SnapsExactlyToDestination_AfterTheLoop()
        {
            string source = File.ReadAllText(SourcePath);
            int loopEnd = source.IndexOf("MapCloudLayout.ApplyPreset(left1, left2, right1, bottom1, to, viewportWidth, viewportHeight);", System.StringComparison.Ordinal);
            Assert.Greater(loopEnd, -1, "Expected an exact snap-to-destination after the animation loop, so no floating-point rounding ever leaves a cloud short of rest/closed.");
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
