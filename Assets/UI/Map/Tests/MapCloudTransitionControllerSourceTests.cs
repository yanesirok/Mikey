using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for MapCloudTransitionController's phase ordering, input
    /// lock, and re-entrancy guard. Read via source assertion for the same
    /// reason as the other Map controller source-text tests (MonoBehaviour
    /// coroutine internals aren't practical to exercise in EditMode).
    /// </summary>
    public class MapCloudTransitionControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapCloudTransitionController.cs";

        // ---------- Japan -> Okinawa: close, swap only while covered, reveal to OkinawaRest ----------

        [Test]
        public void JapanToOkinawa_ClosesJapanCloudsFirst_FromJapanRestToCover()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            int methodEnd = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, -1);
            string body = source.Substring(methodStart, methodEnd - methodStart);

            int closeIndex = body.IndexOf("AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest, MapCloudLayout.Cover);", System.StringComparison.Ordinal);
            int coverOkinawaIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.Cover);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"mapOkinawa\");", System.StringComparison.Ordinal);
            int revealIndex = body.IndexOf("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.Cover, MapCloudLayout.OkinawaRest);", System.StringComparison.Ordinal);

            Assert.Greater(closeIndex, -1, "Expected Phase A: Japan clouds animate JapanRest -> Cover.");
            Assert.Greater(coverOkinawaIndex, -1, "Expected Okinawa's clouds snapped to Cover before the swap.");
            Assert.Greater(showIndex, -1, "Expected the screen swap.");
            Assert.Greater(revealIndex, -1, "Expected Phase C: Okinawa clouds animate Cover -> OkinawaRest.");

            Assert.Less(closeIndex, coverOkinawaIndex, "Close must happen before Okinawa's clouds are set to Cover.");
            Assert.Less(coverOkinawaIndex, showIndex, "Okinawa's clouds must already be at Cover BEFORE the screen swap — the swap must never be visible.");
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
            // AnimateCloudSet snaps to its "to" preset exactly at the end
            // (see the shared snap test below) — this proves the reveal's
            // destination preset is literally OkinawaRest, not an approximation.
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.Cover, MapCloudLayout.OkinawaRest);", source);
        }

        // ---------- Okinawa -> Japan: closes first, reveal ends at JapanRest ----------

        [Test]
        public void OkinawaToJapan_ClosesOkinawaCloudsFirst_FromOkinawaRestToCover()
        {
            string source = File.ReadAllText(SourcePath);
            int methodStart = source.IndexOf("public IEnumerator PlayOkinawaToJapan()", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1);
            string body = source.Substring(methodStart);

            int closeIndex = body.IndexOf("AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest, MapCloudLayout.Cover);", System.StringComparison.Ordinal);
            int coverJapanIndex = body.IndexOf("MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.Cover);", System.StringComparison.Ordinal);
            int showIndex = body.IndexOf("_navigator?.Show(\"map\");", System.StringComparison.Ordinal);
            int revealIndex = body.IndexOf("AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.Cover, MapCloudLayout.JapanRest);", System.StringComparison.Ordinal);

            Assert.Greater(closeIndex, -1, "Expected Phase A: Okinawa clouds animate OkinawaRest -> Cover.");
            Assert.Greater(coverJapanIndex, -1, "Expected Japan's clouds snapped to Cover before the swap.");
            Assert.Greater(showIndex, -1, "Expected the screen swap.");
            Assert.Greater(revealIndex, -1, "Expected Phase C: Japan clouds animate Cover -> JapanRest.");

            Assert.Less(closeIndex, coverJapanIndex);
            Assert.Less(coverJapanIndex, showIndex, "Japan's clouds must already be at Cover BEFORE the screen swap.");
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

        // ---------- shared: AnimateCloudSet always snaps exactly to its destination ----------

        [Test]
        public void AnimateCloudSet_SnapsExactlyToDestination_AfterTheLoop()
        {
            string source = File.ReadAllText(SourcePath);
            int loopEnd = source.IndexOf("MapCloudLayout.ApplyPreset(left1, left2, right1, bottom1, to);", System.StringComparison.Ordinal);
            Assert.Greater(loopEnd, -1, "Expected an exact snap-to-destination after the animation loop, so no floating-point rounding ever leaves a cloud short of rest/cover.");
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
            // The four decorative sprites must never be the picking target —
            // only the shared layer container (see MapCloudAssetsTests for
            // the corresponding UXML-side proof that sprites default to Ignore).
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
            int bindEnd = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", bindStart, System.StringComparison.Ordinal);
            Assert.Greater(bindStart, -1);
            Assert.Greater(bindEnd, -1);
            string body = source.Substring(bindStart, bindEnd - bindStart);

            StringAssert.Contains("MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest);", body);
            StringAssert.Contains("MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest);", body);
            StringAssert.DoesNotContain("StartCoroutine(AnimateCloudSet", body, "Bind must never start an animated transition on plain screen entry.");
            StringAssert.DoesNotContain("PlayJapanToOkinawa()", body);
            StringAssert.DoesNotContain("PlayOkinawaToJapan()", body);
        }

        [Test]
        public void BindWhenReady_LeavesBothCloudLayersUnlocked()
        {
            string source = File.ReadAllText(SourcePath);
            int bindStart = source.IndexOf("private IEnumerator BindWhenReady()", System.StringComparison.Ordinal);
            int bindEnd = source.IndexOf("public IEnumerator PlayJapanToOkinawa()", bindStart, System.StringComparison.Ordinal);
            string body = source.Substring(bindStart, bindEnd - bindStart);
            StringAssert.Contains("SetInputLock(_japanCloudLayer, false);", body);
            StringAssert.Contains("SetInputLock(_okinawaCloudLayer, false);", body);
        }

        // ---------- no frame polling beyond the animation coroutine itself ----------

        [Test]
        public void NoUpdateMethod_ResizeOrTransitionIsPurelyEventOrCoroutineDriven()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("private void Update()", source);
        }
    }
}
