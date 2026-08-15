using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for Map's progression gates: LVL 0 (the Combine assessment) is
    /// always reachable, LVL 1 stays visible but its CTA and the top bar's
    /// TECHNIQUES tab stay locked/dimmed until <see cref="Mikey.UI.Progression.TutorialProgressState.Level1Unlocked"/>
    /// — both reuse the same <c>TutorialProgressPresenter.IsTechniquesUnlocked</c>
    /// check, so they can never disagree. This is defense-in-depth: a Map visit
    /// reached by any means (developer controls, an Editor-only direct screen
    /// jump) must never let a locked checkpoint's CTA jump straight to its
    /// target. Verified by reading the source, mirroring
    /// MapLevelPreviewControllerTests' established technique for MonoBehaviour
    /// internals not practical to drive through a live panel in EditMode.
    /// </summary>
    public class MapProgressionGatingTests
    {
        private const string SourcePath = "Assets/UI/Map/MapLevelPreviewController.cs";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        [Test]
        public void RefreshProgressionUi_LocksCheckpointsBelowTheirRequiredState()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void RefreshProgressionUi()", source);
            StringAssert.Contains("bool unlocked = state >= binding.requiredState;", source);
            StringAssert.Contains("cta.SetEnabled(unlocked);", source);
            StringAssert.Contains("node.AddToClassList(LockedNodeClass);", source);
            StringAssert.Contains("cta.AddToClassList(LockedCtaClass);", source);
        }

        [Test]
        public void TechniquesTab_UsesTheSameUnlockCheck_AsGatedCheckpoints()
        {
            string source = File.ReadAllText(SourcePath);
            // Both the top bar's Techniques tab and any checkpoint gated on
            // Level1Unlocked (LVL 1) must reuse the shared presenter helper —
            // never a second, independently-drifting condition.
            StringAssert.Contains("private void OnTechniquesTabClicked()", source);
            StringAssert.Contains("TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State)", source);
            StringAssert.Contains("bool techniquesUnlocked = TutorialProgressPresenter.IsTechniquesUnlocked(state);", source);
        }

        [Test]
        public void StartCheckpoint_NeverNavigates_WhenLocked()
        {
            string source = File.ReadAllText(SourcePath);

            int methodStart = source.IndexOf("private void StartCheckpoint(CheckpointBinding binding)", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(methodStart, 0, "Expected a StartCheckpoint(CheckpointBinding) method.");
            int methodEnd = source.IndexOf("private void OnTechniquesTabClicked()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, methodStart, "Expected OnTechniquesTabClicked() to follow StartCheckpoint() in source.");
            string methodBody = source.Substring(methodStart, methodEnd - methodStart);

            StringAssert.Contains("if (_progress != null && _progress.State < binding.requiredState)", methodBody);
            StringAssert.Contains("return;", methodBody);
            StringAssert.Contains("_navigator?.Show(binding.navigationTarget);", methodBody,
                "StartCheckpoint must still route through the existing navigator once unlocked.");
        }

        [Test]
        public void ProgressionUiRefresh_RunsOnBind_OnMapEntry_AndOnProgressChange()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_progress.Changed += RefreshProgressionUi;", source,
                "A progression change made while already on Map (e.g. a developer control) must refresh every gated affordance.");

            int occurrences = 0;
            int index = 0;
            while ((index = source.IndexOf("RefreshProgressionUi();", index, System.StringComparison.Ordinal)) >= 0)
            {
                occurrences++;
                index += 1;
            }
            Assert.GreaterOrEqual(occurrences, 2,
                "RefreshProgressionUi() must be called both after binding and on entering the Map screen.");
        }

        [Test]
        public void OnDisable_UnsubscribesFromProgressChanged_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_progress.Changed -= RefreshProgressionUi;", source);
        }

        // No Map layout/composition change — only additive, opacity-only
        // dimming rules for the locked states (mirrors .tq-lesson--locked's idiom).
        [Test]
        public void LockedStyles_AreAdditiveOpacityOnly()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            foreach (var selector in new[] { ".map-detail__cta--locked", ".map-node--locked", ".map-topbar__tab--locked" })
            {
                string block = ExtractRuleBlock(uss, selector + " {");
                Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
                StringAssert.Contains("opacity:", block, $"'{selector}' must dim via opacity only.");
            }
        }

        private static string ExtractRuleBlock(string uss, string header)
        {
            int start = uss.IndexOf(header, System.StringComparison.Ordinal);
            if (start < 0)
                return null;
            int open = start + header.Length;
            int close = uss.IndexOf('}', open);
            return close < 0 ? null : uss.Substring(open, close - open);
        }
    }
}
