using System.Collections.Generic;
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
    /// single source of truth. Both Intro actions (Skip + primary CTA) now route
    /// forward to the Home hub via 'go-menu'; the old loop back to Title is gone.
    /// </summary>
    public class IntroScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";

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

        // 4 — both production actions now exist as 'go-menu' navigators.
        [Test]
        public void BothProductionActions_Exist_AsGoMenu()
        {
            var actions = Intro().Query<Button>(name: "go-menu").ToList();
            Assert.AreEqual(2, actions.Count,
                "Intro must expose exactly two production actions (Skip + primary CTA), both 'go-menu'.");
        }

        // 5 + 13 — both Intro actions target the existing Home ('menu') screen.
        [Test]
        public void BothIntroActions_TargetMenuScreen()
        {
            var root = BuildTree();
            var intro = root.Q<VisualElement>("intro");
            // ScreenManager maps a 'go-<id>' navigator to the screen named <id>.
            var actions = intro.Query<Button>(name: "go-menu").ToList();
            Assert.AreEqual(2, actions.Count, "Both Intro actions must be 'go-menu' navigators.");
            var menu = root.Q<VisualElement>("menu");
            Assert.IsNotNull(menu, "'go-menu' must target an existing 'menu' (Home) screen.");
            Assert.IsTrue(menu.ClassListContains("screen"), "'menu' target must be a screen.");
        }

        // 10 — Skip uses go-menu (and is the .intro-skip action).
        [Test]
        public void IntroSkip_UsesGoMenu()
        {
            var skip = Intro().Query<Button>(name: "go-menu", className: "intro-skip").ToList();
            Assert.AreEqual(1, skip.Count, "Intro Skip must be a single 'go-menu' button with .intro-skip.");
        }

        // 11 — primary action uses go-menu (and is the .intro-primary action).
        [Test]
        public void IntroPrimary_UsesGoMenu()
        {
            var primary = Intro().Query<Button>(name: "go-menu", className: "intro-primary").ToList();
            Assert.AreEqual(1, primary.Count, "Intro primary CTA must be a single 'go-menu' button with .intro-primary.");
        }

        // 12 — Skip and primary keep distinct semantic classes.
        [Test]
        public void PrimaryAndSkip_HaveDistinctSemanticClasses()
        {
            var intro = Intro();
            var primary = intro.Query<Button>(name: "go-menu", className: "intro-primary").ToList();
            var skip = intro.Query<Button>(name: "go-menu", className: "intro-skip").ToList();
            Assert.AreEqual(1, primary.Count, "Exactly one go-menu must carry .intro-primary.");
            Assert.AreEqual(1, skip.Count, "Exactly one go-menu must carry .intro-skip.");
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
            foreach (var action in Intro().Query<Button>(name: "go-menu").ToList())
                Assert.IsTrue(action.ClassListContains("tap-target"),
                    "Each production action must carry the .tap-target minimum-touch-target class.");
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

        // 10 (suite-level) — production screen count is six after Splash removal.
        [Test]
        public void ProductionScreenCount_IsSix()
        {
            Assert.AreEqual(6, ByClass(BuildTree(), "screen").Count,
                "The application must keep exactly six production screens.");
        }

        // 11 (suite-level) — unrelated screen ids and forward routes are intact.
        [Test]
        public void NoUnrelatedScreenIdsOrRoutes_Changed()
        {
            var root = BuildTree();
            var expected = new[] { "title", "intro", "menu", "combineIntro", "camTest", "combine" };
            var ids = ByClass(root, "screen").Select(s => s.name).ToList();
            CollectionAssert.AreEquivalent(expected, ids, "Screen ids must be the six production screens.");

            // Key forward navigators must still be present (routes intact).
            foreach (var nav in new[] { "go-intro", "go-menu", "go-camTest", "go-combine" })
                Assert.IsNotEmpty(root.Query<VisualElement>(name: nav).ToList(),
                    $"Existing navigator '{nav}' must remain.");
        }
    }
}
