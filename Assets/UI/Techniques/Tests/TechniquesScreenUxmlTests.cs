using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Techniques.Tests
{
    /// <summary>
    /// Structural contract for the landscape Techniques/Lessons hub (the
    /// "techniques" screen) in MikeyApp.uxml: exactly one screen with one
    /// safe-area wrapper, a full-bleed background outside that wrapper, a
    /// clear Home/Back (go-menu) and first-lesson (go-practice) action, an
    /// available first lesson visually distinct from honestly-locked later
    /// lessons, and reusable touch-target / visible-icon / wrapping classes
    /// so nothing collapses or overflows on phone-landscape sizes.
    /// </summary>
    public class TechniquesScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string NavPrefix = "go-";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static VisualElement Techniques(VisualElement root)
        {
            var screen = root.Q<VisualElement>("techniques");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'techniques'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'techniques' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        // 1
        [Test]
        public void Techniques_ExistsAsExactlyOneScreen()
        {
            var count = BuildTree().Query<VisualElement>(className: "screen").ToList()
                .Count(s => s.name == "techniques");
            Assert.AreEqual(1, count, "There must be exactly one screen named 'techniques'.");
        }

        // 4
        [Test]
        public void Techniques_HasExactlyOneSafeAreaContent()
        {
            var count = Techniques(BuildTree()).Query<VisualElement>(className: "safe-area-content").ToList().Count;
            Assert.AreEqual(1, count, $"'techniques' must contain exactly one .safe-area-content (found {count}).");
        }

        // 5
        [Test]
        public void Background_IsFullBleed_OutsideSafeArea()
        {
            var bg = Techniques(BuildTree()).Q<VisualElement>(className: "tq-bg");
            Assert.IsNotNull(bg, "Expected a .tq-bg full-bleed background.");
            Assert.IsNull(NearestSafeAreaAncestor(bg), ".tq-bg must not be inside .safe-area-content.");
        }

        // 6
        [Test]
        public void ForegroundLayout_IsInsideSafeArea()
        {
            var layout = Techniques(BuildTree()).Q<VisualElement>(className: "tq-layout");
            Assert.IsNotNull(layout, "Expected a .tq-layout foreground container.");
            Assert.IsNotNull(NearestSafeAreaAncestor(layout), ".tq-layout must be inside .safe-area-content.");
        }

        // 11
        [Test]
        public void Techniques_HasGoMenuBackAction()
        {
            var root = BuildTree();
            var back = Techniques(root).Q<Button>("go-menu");
            Assert.IsNotNull(back, "Techniques must expose a 'go-menu' Home/Back action.");
            Assert.IsTrue(root.Q<VisualElement>("menu").ClassListContains("screen"),
                "'go-menu' must target the existing 'menu' screen.");
        }

        // 12 + 13 + 14 — the first-lesson action is a real go-practice navigator to an existing screen.
        [Test]
        public void Techniques_HasGoPracticeFirstLessonAction()
        {
            var root = BuildTree();
            var lesson = Techniques(root).Q<Button>("go-practice");
            Assert.IsNotNull(lesson, "Techniques must expose a 'go-practice' first-lesson action (a real Button).");

            string target = lesson.name.Substring(NavPrefix.Length);
            Assert.AreEqual("practice", target, "go-practice must target the 'practice' screen.");
            Assert.IsTrue(root.Q<VisualElement>(target).ClassListContains("screen"),
                "The 'practice' target screen must exist.");

            Assert.IsTrue(lesson.ClassListContains("tq-lesson--available"),
                "The first lesson card must be visually distinct (carry 'tq-lesson--available').");
        }

        // 15 — locked lessons are explicitly styled and are NOT active navigators.
        [Test]
        public void LockedLessons_AreExplicitlyLocked_AndNotNavigators()
        {
            var locked = Techniques(BuildTree())
                .Query<VisualElement>(className: "tq-lesson--locked").ToList();
            Assert.GreaterOrEqual(locked.Count, 3, "Expected at least three honestly-locked lessons.");

            foreach (var card in locked)
            {
                Assert.IsFalse(card is Button,
                    "A locked lesson must not be an active Button (no silent clickability).");
                Assert.IsTrue(string.IsNullOrEmpty(card.name) || !card.name.StartsWith(NavPrefix),
                    "A locked lesson must not be a 'go-' navigator.");
                // explicit locked state + a readable coming-soon marker
                var badge = card.Q<VisualElement>(className: "tq-lesson__badge");
                Assert.IsNotNull(badge, "A locked lesson must show an explicit coming-soon badge.");
            }
        }

        // 25 — interactive controls use the reusable minimum touch-target class.
        [Test]
        public void InteractiveControls_UseTouchTargetClass()
        {
            var screen = Techniques(BuildTree());
            foreach (var name in new[] { "go-practice", "go-menu" })
            {
                var ctrl = screen.Q<VisualElement>(name);
                Assert.IsNotNull(ctrl, $"Expected interactive control '{name}'.");
                Assert.IsTrue(ctrl.ClassListContains("tap-target-lg"),
                    $"Control '{name}' must use the '.tap-target-lg' (>=56x56) touch-target class.");
            }
        }

        // 26 + 27 — visible icons use explicit reusable size classes on the non-shrinking base.
        [Test]
        public void VisibleIcons_UseExplicitSizeClasses_OnNonShrinkingBase()
        {
            var screen = Techniques(BuildTree());
            var lessonGlyphs = screen.Query<VisualElement>(className: "tq-lesson__glyph").ToList();
            Assert.GreaterOrEqual(lessonGlyphs.Count, 4, "Expected a glyph on each lesson card.");
            foreach (var glyph in lessonGlyphs)
            {
                Assert.IsTrue(glyph.ClassListContains("tq-icon"),
                    "Lesson glyphs must use the non-shrinking '.tq-icon' base class.");
                Assert.IsTrue(glyph.ClassListContains("tq-icon--lesson"),
                    "Lesson glyphs must use the explicit '.tq-icon--lesson' size class.");
            }

            // the hub header mark uses the explicit large size class
            var mark = screen.Q<VisualElement>(className: "tq-headline__mark");
            Assert.IsNotNull(mark, "Expected a hub header mark icon.");
            Assert.IsTrue(mark.ClassListContains("tq-icon") && mark.ClassListContains("tq-icon--hub"),
                "Header mark must use '.tq-icon' + '.tq-icon--hub'.");
        }

        // 28 — action containers use the responsive wrapping class.
        [Test]
        public void ActionContainers_UseResponsiveWrappingClass()
        {
            var screen = Techniques(BuildTree());
            var bars = screen.Query<VisualElement>(className: "tq-actionbar").ToList();
            Assert.IsNotEmpty(bars, "Expected at least one '.tq-actionbar' responsive (wrapping) action container.");
        }

        // 29 — sibling action buttons must NOT reintroduce the overflow-prone width:100% global .btn.
        [Test]
        public void ActionButtons_DoNotUseFullWidthGlobalBtn()
        {
            var screen = Techniques(BuildTree());
            foreach (var btn in screen.Query<Button>().ToList())
            {
                Assert.IsFalse(btn.ClassListContains("btn"),
                    $"Action button '{btn.name}' must not use the width:100% global '.btn' class " +
                    "(use the content-sized '.tq-btn' / lesson-card classes instead).");
            }
        }
    }
}
