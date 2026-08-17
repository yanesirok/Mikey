using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Structural + navigation contract for the landscape Intro screen after the
    /// entry-flow consolidation. Mirrors the MikeyAppUxmlTests approach (clone the
    /// real UXML, assert on the resulting tree) so the production markup is the
    /// single source of truth. Both Intro actions (Skip + primary CTA) route
    /// forward to the Home hub as 'lore-skip'/'lore-continue' — deliberately not
    /// 'go-menu' navigators, so LoreExitController (not ScreenManager's auto-
    /// wiring) drives their cinematic fade-to-black-then-navigate exit; see
    /// LoreExitControllerTests. The old loop back to Title is gone. Since the
    /// minimal-placeholder pass, also covers the pure-black background and the
    /// retired atmospheric glow/multi-beat copy sequence.
    /// </summary>
    public class IntroScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string IntroUssPath = "Assets/UI/Intro/Intro.uss";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static List<VisualElement> ByClass(VisualElement el, string className) =>
            el.Query<VisualElement>(className: className).ToList();

        private static VisualElement Intro() => BuildTree().Q<VisualElement>("intro");

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        // 1
        [Test]
        public void IntroScreen_ExistsExactlyOnce()
        {
            var intros = BuildTree().Query<VisualElement>(name: "intro").ToList();
            Assert.AreEqual(1, intros.Count, "Exactly one screen named 'intro' must exist.");
            Assert.IsTrue(intros[0].ClassListContains("screen"), "'intro' must carry the .screen class.");
        }

        // 2
        [Test]
        public void IntroScreen_HasExactlyOneSafeAreaContent()
        {
            Assert.AreEqual(1, ByClass(Intro(), "safe-area-content").Count,
                "Intro must contain exactly one .safe-area-content wrapper.");
        }

        // 3
        [Test]
        public void IntroBackground_IsFullBleed_OutsideSafeArea()
        {
            var intro = Intro();
            var bg = ByClass(intro, "intro-bg");
            Assert.IsNotEmpty(bg, "Intro must have a full-bleed .intro-bg background.");
            foreach (var el in bg)
                Assert.IsNull(NearestSafeAreaAncestor(el),
                    ".intro-bg must live OUTSIDE .safe-area-content (full-bleed).");
        }

        // 4 — both production actions exist, named for LoreExitController (not 'go-menu').
        [Test]
        public void BothProductionActions_Exist_AsLoreSkipAndLoreContinue()
        {
            Assert.IsNotNull(Intro().Q<Button>("lore-skip"), "Intro must expose a 'lore-skip' action.");
            Assert.IsNotNull(Intro().Q<Button>("lore-continue"), "Intro must expose a 'lore-continue' action.");
            Assert.IsEmpty(Intro().Query<Button>(name: "go-menu").ToList(),
                "Intro's exit actions must not be 'go-menu' navigators — LoreExitController drives their cinematic exit instead.");
        }

        // 5 + 13 — both Intro actions ultimately reach the existing Home ('menu')
        // screen — via LoreExitController's transition, not ScreenManager's
        // 'go-<id>' auto-wiring convention (see LoreExitControllerTests for the
        // NextScreenId = "menu" contract).
        [Test]
        public void BothIntroActions_TargetMenuScreen()
        {
            var root = BuildTree();
            var intro = root.Q<VisualElement>("intro");
            Assert.IsNotNull(intro.Q<Button>("lore-skip"), "Expected a 'lore-skip' action.");
            Assert.IsNotNull(intro.Q<Button>("lore-continue"), "Expected a 'lore-continue' action.");
            var menu = root.Q<VisualElement>("menu");
            Assert.IsNotNull(menu, "Lore's exit must target an existing 'menu' (Home) screen.");
            Assert.IsTrue(menu.ClassListContains("screen"), "'menu' target must be a screen.");
        }

        // 10 — Skip is 'lore-skip' (and is the .intro-skip action).
        [Test]
        public void IntroSkip_IsLoreSkip()
        {
            var skip = Intro().Query<Button>(name: "lore-skip", className: "intro-skip").ToList();
            Assert.AreEqual(1, skip.Count, "Intro Skip must be a single 'lore-skip' button with .intro-skip.");
        }

        // 11 — primary action is 'lore-continue' (and is the .intro-primary action).
        [Test]
        public void IntroPrimary_IsLoreContinue()
        {
            var primary = Intro().Query<Button>(name: "lore-continue", className: "intro-primary").ToList();
            Assert.AreEqual(1, primary.Count, "Intro primary CTA must be a single 'lore-continue' button with .intro-primary.");
        }

        // 12 — Skip and primary keep distinct semantic classes.
        [Test]
        public void PrimaryAndSkip_HaveDistinctSemanticClasses()
        {
            var intro = Intro();
            var primary = intro.Query<Button>(name: "lore-continue", className: "intro-primary").ToList();
            var skip = intro.Query<Button>(name: "lore-skip", className: "intro-skip").ToList();
            Assert.AreEqual(1, primary.Count, "Exactly one 'lore-continue' must carry .intro-primary.");
            Assert.AreEqual(1, skip.Count, "Exactly one 'lore-skip' must carry .intro-skip.");
            Assert.AreNotSame(primary[0], skip[0], "Primary and Skip must be distinct elements.");
        }

        // No leftover loop back to Title.
        [Test]
        public void Intro_NoLongerRoutesBackToTitle()
        {
            Assert.IsEmpty(Intro().Query<VisualElement>(name: "go-title").ToList(),
                "Intro must not retain any 'go-title' loop-back navigator.");
        }

        // 7
        [Test]
        public void InteractiveControls_UseMinimumTouchTargetClass()
        {
            foreach (var name in new[] { "lore-skip", "lore-continue" })
            {
                var action = Intro().Q<Button>(name);
                Assert.IsNotNull(action, $"Expected a '{name}' action.");
                Assert.IsTrue(action.ClassListContains("tap-target"),
                    "Each production action must carry the .tap-target minimum-touch-target class.");
            }
        }

        // 8
        [Test]
        public void VisibleActionIcons_UseExplicitNonShrinkingSizingClass()
        {
            // Each action carries a separate visible arrow icon with the sizing class.
            var icons = ByClass(Intro(), "intro-icon");
            Assert.GreaterOrEqual(icons.Count, 2,
                "Skip and primary CTA must each have a visible .intro-icon arrow.");
        }

        // 9
        [Test]
        public void OldStaticPlaceholderStructure_IsRemoved()
        {
            var intro = Intro();
            Assert.IsEmpty(ByClass(intro, "videobox"),
                "Legacy .videobox placeholder must be removed from the Intro screen.");
            Assert.IsEmpty(ByClass(intro, "videobox-label"),
                "Legacy .videobox-label placeholder must be removed from the Intro screen.");
        }

        // Lore rebuild: the comic/silhouette "cast stepping into frame" placeholder
        // is retired in favor of a minimal temporary text-only cinematic.
        [Test]
        public void OldSilhouetteCastPlaceholder_IsRemoved()
        {
            var intro = Intro();
            Assert.IsEmpty(ByClass(intro, "intro-cast"),
                "The old placeholder silhouette cast must be removed from Lore.");
            Assert.IsEmpty(ByClass(intro, "intro-figure"),
                "The old placeholder silhouette figures must be removed from Lore.");
        }

        // Minimal placeholder pass: one honest "not ready yet" statement rather
        // than a scripted multi-beat sequence, ending on Continue (lore-continue).
        [Test]
        public void PlaceholderCopy_Exists_PrimaryAndSecondary()
        {
            var lines = Intro().Query<Label>(className: "intro-line").ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(lines, "The story is still being written.");
            CollectionAssert.Contains(lines, "Every path begins before the first step.");
        }

        [Test]
        public void PlaceholderCopy_HasComingSoonNote_DistinctFromTheMainLines()
        {
            var notes = Intro().Query<Label>(className: "intro-note").ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(notes, "Lore cinematic coming soon.");
        }

        [Test]
        public void ContinueCta_ExistsAndReadsContinue()
        {
            var continueCta = Intro().Query<Button>(name: "lore-continue", className: "intro-primary").ToList();
            Assert.AreEqual(1, continueCta.Count, "Expected a single Continue (lore-continue/.intro-primary) action.");
            Assert.AreEqual("Continue", continueCta[0].Q<Label>(className: "intro-cta__text")?.text);
        }

        [Test]
        public void NoExtraCopy_OnlyTheFourSpecifiedLines()
        {
            var intro = Intro();
            var allText = intro.Query<Label>().ToList().Select(l => l.text ?? string.Empty).Where(t => t.Length > 0).ToList();
            var expected = new[]
            {
                "The story is still being written.",
                "Every path begins before the first step.",
                "Lore cinematic coming soon.",
                "Continue",
                "Skip",
                "▸",
                "›",
            };
            CollectionAssert.AreEquivalent(expected, allText,
                "The placeholder Lore screen must not carry any extra copy beyond the spec'd lines, note, Continue and Skip.");
        }

        [Test]
        public void Background_IsPureOrNearPureBlack()
        {
            string uss = File.ReadAllText(IntroUssPath);
            int start = uss.IndexOf("\n.intro-bg {", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "Expected an '.intro-bg' rule in Intro.uss.");
            int close = uss.IndexOf('}', start);
            string block = uss.Substring(start, close - start);
            StringAssert.Contains("background-color: #000000", block,
                "The Lore placeholder background must be pure (or extremely close to pure) black.");
        }

        [Test]
        public void OldGlowDecoration_IsRemoved()
        {
            Assert.IsEmpty(ByClass(Intro(), "intro-glow"),
                "The old atmospheric glow decoration must be gone — the screen should feel almost empty.");
            string uss = File.ReadAllText(IntroUssPath);
            StringAssert.DoesNotContain(".intro-glow", uss);
        }

        [Test]
        public void NoCardOrPanelWrapsThePlaceholderCopy()
        {
            // The placeholder must read as an near-empty full-bleed screen, never
            // a card/popup/modal-style container.
            var intro = Intro();
            foreach (var className in new[] { "menu-modal", "vow-modal", "settings-modal", "card", "panel" })
                Assert.IsEmpty(ByClass(intro, className), $"Lore must not wrap its copy in a '.{className}'-style container.");
        }

        // 10 (suite-level) — production screen count is eleven (Profile, Okinawa chapter map added).
        [Test]
        public void ProductionScreenCount_IsTwelve()
        {
            Assert.AreEqual(12, ByClass(BuildTree(), "screen").Count,
                "The application must keep exactly twelve production screens.");
        }

        // 11 (suite-level) — unrelated screen ids and forward routes are intact.
        [Test]
        public void NoUnrelatedScreenIdsOrRoutes_Changed()
        {
            var root = BuildTree();
            var expected = new[] { "title", "intro", "menu", "combineIntro", "camTest", "combine", "techniques", "practice", "map", "mapOkinawa", "profile", "profileDetails" };
            var ids = ByClass(root, "screen").Select(s => s.name).ToList();
            CollectionAssert.AreEquivalent(expected, ids, "Screen ids must be the twelve production screens.");

            // Key forward navigators must still be present (routes intact). Title's
            // own route into Intro is no longer "go-intro" — TitleController drives
            // it directly (see MikeyAppUxmlTests).
            foreach (var nav in new[] { "go-menu", "go-camTest", "go-combine" })
                Assert.IsNotEmpty(root.Query<VisualElement>(name: nav).ToList(),
                    $"Existing navigator '{nav}' must remain.");
        }
    }
}
