using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Structural contract for the rebuilt landscape "combineIntro" LVL0 briefing
    /// screen in MikeyApp.uxml: a single safe-area wrapper with the decorative
    /// background outside it and all briefing/test content inside, the preserved
    /// production route (go-menu → menu, go-camTest → camTest), explicit
    /// touch-target + visible-icon sizing classes, a responsive (non
    /// width:100%) action bar, and no regression of the surrounding ten-screen
    /// flow or the retired Combine result route.
    /// </summary>
    public class CombineIntroScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/CombineIntro/CombineIntro.uss";
        private static readonly string LegacyResultScreen = "combine" + "Results";
        private static readonly string LegacyResultNavigator = "go-" + LegacyResultScreen;

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static VisualElement CombineIntro(VisualElement root)
        {
            var screen = root.Q<VisualElement>("combineIntro");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'combineIntro'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'combineIntro' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        // 1 — combineIntro exists exactly once.
        [Test]
        public void CombineIntro_ExistsExactlyOnce()
        {
            var matches = BuildTree().Query<VisualElement>(name: "combineIntro").ToList();
            Assert.AreEqual(1, matches.Count, "There must be exactly one 'combineIntro' screen.");
        }

        // 2 — exactly one .safe-area-content wrapper.
        [Test]
        public void CombineIntro_HasExactlyOneSafeAreaContent()
        {
            var screen = CombineIntro(BuildTree());
            Assert.AreEqual(1, screen.Query<VisualElement>(className: "safe-area-content").ToList().Count,
                "'combineIntro' must contain exactly one .safe-area-content wrapper.");
        }

        // 3 — full-bleed background is OUTSIDE the safe-area wrapper.
        [Test]
        public void Background_IsFullBleed_OutsideSafeAreaContent()
        {
            var screen = CombineIntro(BuildTree());
            var bg = screen.Q<VisualElement>(className: "ci-bg");
            Assert.IsNotNull(bg, "Expected a .ci-bg full-bleed background layer.");
            Assert.IsNull(NearestSafeAreaAncestor(bg),
                ".ci-bg must not be a descendant of .safe-area-content.");
        }

        // 4 — all briefing/test content is INSIDE the safe-area wrapper.
        [Test]
        public void BriefingAndTestContent_AreInsideSafeAreaContent()
        {
            var screen = CombineIntro(BuildTree());
            foreach (var className in new[]
            {
                "ci-layout", "ci-brief", "ci-headline", "ci-lead", "ci-status",
                "ci-actionbar", "ci-tests",
            })
            {
                var matches = screen.Query<VisualElement>(className: className).ToList();
                Assert.IsNotEmpty(matches, $"Expected at least one .{className}.");
                foreach (var el in matches)
                    Assert.IsNotNull(NearestSafeAreaAncestor(el),
                        $".{className} must be inside .safe-area-content.");
            }

            // every test card lives inside the safe area too
            var tests = screen.Query<VisualElement>(className: "ci-test").ToList();
            Assert.AreEqual(4, tests.Count, "Expected exactly four .ci-test cards.");
            foreach (var t in tests)
                Assert.IsNotNull(NearestSafeAreaAncestor(t), ".ci-test must be inside .safe-area-content.");
        }

        // 5 — go-menu exists on combineIntro.
        [Test]
        public void GoMenu_ExistsOnCombineIntro()
        {
            var screen = CombineIntro(BuildTree());
            Assert.AreEqual(1, screen.Query<Button>(name: "go-menu").ToList().Count,
                "combineIntro must expose exactly one 'go-menu' (Return Home) Button.");
        }

        // 6 — go-menu targets the existing 'menu' screen (ScreenManager maps go-<id> -> <id>).
        [Test]
        public void GoMenu_TargetsExistingMenuScreen()
        {
            var root = BuildTree();
            var nav = CombineIntro(root).Q<Button>("go-menu");
            Assert.IsNotNull(nav, "Expected a 'go-menu' navigator on combineIntro.");
            var menu = root.Q<VisualElement>("menu");
            Assert.IsNotNull(menu, "'go-menu' must target an existing 'menu' screen.");
            Assert.IsTrue(menu.ClassListContains("screen"), "'menu' target must be a screen.");
        }

        // 7 — go-camTest exists on combineIntro.
        [Test]
        public void GoCamTest_ExistsOnCombineIntro()
        {
            var screen = CombineIntro(BuildTree());
            Assert.AreEqual(1, screen.Query<Button>(name: "go-camTest").ToList().Count,
                "combineIntro must expose exactly one 'go-camTest' (Begin Camera Test) Button.");
        }

        // 8 — go-camTest targets the existing 'camTest' screen.
        [Test]
        public void GoCamTest_TargetsExistingCamTestScreen()
        {
            var root = BuildTree();
            var nav = CombineIntro(root).Q<Button>("go-camTest");
            Assert.IsNotNull(nav, "Expected a 'go-camTest' navigator on combineIntro.");
            var camTest = root.Q<VisualElement>("camTest");
            Assert.IsNotNull(camTest, "'go-camTest' must target an existing 'camTest' screen.");
            Assert.IsTrue(camTest.ClassListContains("screen"), "'camTest' target must be a screen.");
        }

        // 9 — both production actions use the minimum touch-target class.
        [Test]
        public void ProductionActions_UseMinimumTouchTargetClass()
        {
            var screen = CombineIntro(BuildTree());
            foreach (var name in new[] { "go-camTest", "go-menu" })
            {
                var button = screen.Q<Button>(name);
                Assert.IsNotNull(button, $"Expected a Button named '{name}'.");
                Assert.IsTrue(button.ClassListContains("ci-tap-target-lg"),
                    $"'{name}' must carry the >=56px .ci-tap-target-lg touch-target class.");
            }
        }

        // 10 — both action icons use explicit visible-size + non-shrinking classes.
        [Test]
        public void ActionIcons_UseExplicitVisibleNonShrinkingClasses()
        {
            var screen = CombineIntro(BuildTree());
            foreach (var name in new[] { "go-camTest", "go-menu" })
            {
                var button = screen.Q<Button>(name);
                Assert.IsNotNull(button, $"Expected a Button named '{name}'.");
                var icons = button.Query<VisualElement>(className: "ci-icon--action").ToList();
                Assert.IsNotEmpty(icons, $"'{name}' must include a visible .ci-icon--action arrow.");
                foreach (var icon in icons)
                    Assert.IsTrue(icon.ClassListContains("ci-icon"),
                        $"'{name}' action icon must also carry the non-shrinking .ci-icon base class.");
            }
        }

        // 11 — test-list icons use explicit readable-size classes (all four).
        [Test]
        public void TestListIcons_UseExplicitReadableSizeClasses()
        {
            var screen = CombineIntro(BuildTree());
            var glyphs = screen.Query<VisualElement>(className: "ci-icon--test").ToList();
            Assert.AreEqual(4, glyphs.Count, "Expected four .ci-icon--test test-list glyphs.");
            foreach (var glyph in glyphs)
                Assert.IsTrue(glyph.ClassListContains("ci-icon"),
                    "Each test-list glyph must also carry the non-shrinking .ci-icon base class.");
        }

        // 12 — no visually active control lacks an action: every Button on the
        // screen is a wired 'go-' navigator (no orphan/dead controls).
        [Test]
        public void NoInteractiveControlIsDead()
        {
            var screen = CombineIntro(BuildTree());
            var buttons = screen.Query<Button>().ToList();
            Assert.IsNotEmpty(buttons, "combineIntro must expose its actions as Buttons.");
            foreach (var b in buttons)
            {
                Assert.IsFalse(string.IsNullOrEmpty(b.name),
                    "Every interactive Button must be named so it can be wired (no dead controls).");
                Assert.IsTrue(b.name.StartsWith("go-"),
                    $"Button '{b.name}' must be a wired 'go-' navigator (no inert controls on combineIntro).");
            }
        }

        // 13 — action container uses the intended responsive class.
        [Test]
        public void ActionContainer_UsesResponsiveActionbarClass()
        {
            var screen = CombineIntro(BuildTree());
            var bar = screen.Q<VisualElement>(className: "ci-actionbar");
            Assert.IsNotNull(bar, "Expected a responsive .ci-actionbar container.");
            foreach (var name in new[] { "go-camTest", "go-menu" })
                Assert.IsNotNull(bar.Q<Button>(name),
                    $"'{name}' must live inside the .ci-actionbar container.");
        }

        // 14 — no width-overflow-prone width:100% button rule where it would
        // conflict with the sibling action. The actions use the flex-sized
        // .ci-btn kit, not the theme's width:100% .btn rule, and the .ci-btn
        // rule itself declares no fixed width.
        [Test]
        public void ActionButtons_DoNotUseWidth100ButtonRule()
        {
            var screen = CombineIntro(BuildTree());
            foreach (var name in new[] { "go-camTest", "go-menu" })
            {
                var button = screen.Q<Button>(name);
                Assert.IsNotNull(button, $"Expected a Button named '{name}'.");
                Assert.IsTrue(button.ClassListContains("ci-btn"),
                    $"'{name}' must use the flex-sized .ci-btn class.");
                Assert.IsFalse(button.ClassListContains("btn"),
                    $"'{name}' must NOT use the theme .btn rule (width:100% overflows the sibling action).");
            }

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".ci-btn {");
            Assert.IsNotNull(block, "Expected a .ci-btn rule in CombineIntro.uss.");
            // The overflow-prone rule is specifically `width: 100%` (a full-width
            // button cannot share a row with a sibling). `min-width: 0` is the
            // opposite — it lets the flex child shrink — so guard the exact rule.
            StringAssert.DoesNotContain("width: 100%", block,
                "The .ci-btn rule must size via flex, not width:100% that overflows the sibling action.");
        }

        // 15 — production screen count is ten (Profile added).
        [Test]
        public void ProductionScreenCount_IsTen()
        {
            Assert.AreEqual(10, BuildTree().Query<VisualElement>(className: "screen").ToList().Count,
                "There must be exactly ten production screens.");
        }

        // 16 — Title → Intro → Home remains unchanged.
        [Test]
        public void TitleToIntroToHomeFlow_RemainsUnchanged()
        {
            var root = BuildTree();
            var title = root.Q<VisualElement>("title");
            Assert.IsNotNull(title, "Expected a 'title' screen.");
            Assert.IsNotNull(title.Q<Button>("go-intro"), "Title must keep its 'go-intro' CTA.");

            var intro = root.Q<VisualElement>("intro");
            Assert.IsNotNull(intro, "Expected an 'intro' screen.");
            Assert.IsNotEmpty(intro.Query<VisualElement>(name: "go-menu").ToList(),
                "Intro must keep a 'go-menu' route back to Home.");
        }

        // 17 — Camera Test → Combine → Home remains unchanged.
        [Test]
        public void CamTestToCombineToHomeFlow_RemainsUnchanged()
        {
            var root = BuildTree();
            var camTest = root.Q<VisualElement>("camTest");
            Assert.IsNotNull(camTest, "Expected a 'camTest' screen.");
            Assert.IsNotNull(camTest.Q<Button>("go-combine"),
                "camTest must keep its 'go-combine' route to the Combine screen.");

            var combine = root.Q<VisualElement>("combine");
            Assert.IsNotNull(combine, "Expected a 'combine' screen.");
            Assert.IsNotEmpty(combine.Query<VisualElement>(name: "go-menu").ToList(),
                "Combine must keep a 'go-menu' return-Home route.");
        }

        // 18 — no retired Combine result route returns.
        [Test]
        public void LegacyCombineResultRoute_DoesNotReturn()
        {
            var root = BuildTree();
            Assert.IsNull(root.Q<VisualElement>(LegacyResultScreen),
                "Retired Combine result screen must not exist.");
            Assert.IsNull(root.Q<VisualElement>(LegacyResultNavigator),
                "Retired Combine result navigator must not exist.");
            StringAssert.DoesNotContain(LegacyResultScreen, File.ReadAllText(UxmlPath),
                "MikeyApp.uxml must not reference the removed Combine result screen.");
        }

        /// <summary>
        /// Returns the body of the first USS rule whose header matches
        /// <paramref name="header"/> (e.g. ".ci-btn {"), or null if absent.
        /// </summary>
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
