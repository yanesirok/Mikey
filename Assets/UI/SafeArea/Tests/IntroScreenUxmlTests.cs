using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Structural + navigation contract for the rebuilt landscape Intro screen.
    /// Mirrors the MikeyAppUxmlTests approach (clone the real UXML, assert on the
    /// resulting tree) so the production markup is the single source of truth.
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

        // 4
        [Test]
        public void BothProductionActions_Exist()
        {
            var actions = Intro().Query<Button>(name: "go-title").ToList();
            Assert.AreEqual(2, actions.Count,
                "Intro must expose exactly two production actions (Skip + primary CTA).");
        }

        // 5
        [Test]
        public void BothIntroActions_TargetTitleScreen()
        {
            var root = BuildTree();
            var intro = root.Q<VisualElement>("intro");
            // ScreenManager maps a 'go-<id>' navigator to the screen named <id>.
            var actions = intro.Query<Button>(name: "go-title").ToList();
            Assert.AreEqual(2, actions.Count, "Both Intro actions must be 'go-title' navigators.");
            var title = root.Q<VisualElement>("title");
            Assert.IsNotNull(title, "'go-title' must target an existing 'title' screen.");
            Assert.IsTrue(title.ClassListContains("screen"), "'title' target must be a screen.");
        }

        // 6
        [Test]
        public void PrimaryAndSkip_HaveDistinctSemanticClasses()
        {
            var intro = Intro();
            var primary = intro.Query<Button>(name: "go-title", className: "intro-primary").ToList();
            var skip = intro.Query<Button>(name: "go-title", className: "intro-skip").ToList();
            Assert.AreEqual(1, primary.Count, "Exactly one go-title must carry .intro-primary.");
            Assert.AreEqual(1, skip.Count, "Exactly one go-title must carry .intro-skip.");
            Assert.AreNotSame(primary[0], skip[0], "Primary and Skip must be distinct elements.");
        }

        // 7
        [Test]
        public void InteractiveControls_UseMinimumTouchTargetClass()
        {
            foreach (var action in Intro().Query<Button>(name: "go-title").ToList())
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

        // 10
        [Test]
        public void ProductionScreenCount_RemainsSeven()
        {
            Assert.AreEqual(7, ByClass(BuildTree(), "screen").Count,
                "The application must keep exactly seven production screens.");
        }

        // 11
        [Test]
        public void NoUnrelatedScreenIdsOrRoutes_Changed()
        {
            var root = BuildTree();
            var expected = new[] { "splash", "intro", "title", "menu", "combineIntro", "camTest", "combine" };
            var ids = ByClass(root, "screen").Select(s => s.name).ToList();
            CollectionAssert.AreEquivalent(expected, ids, "Screen ids must be unchanged.");

            // Key navigators on the other screens must still be present (routes intact).
            foreach (var nav in new[] { "go-intro", "go-menu", "go-camTest", "go-combine" })
                Assert.IsNotEmpty(root.Query<VisualElement>(name: nav).ToList(),
                    $"Existing navigator '{nav}' must remain.");
        }
    }
}
