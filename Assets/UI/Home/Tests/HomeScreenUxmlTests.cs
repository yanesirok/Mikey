using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Structural contract for the landscape Home hub (the "menu" screen) in
    /// MikeyApp.uxml: exactly one screen with one safe-area wrapper, a full-bleed
    /// background outside that wrapper, a working Combine entry, an explicit
    /// 4-tab dock with active/locked states, reusable touch targets on every
    /// interactive control, and none of the old unbound menu controls.
    /// </summary>
    public class HomeScreenUxmlTests
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

        private static VisualElement HomeScreen(VisualElement root)
        {
            var screen = root.Q<VisualElement>("menu");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'menu'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'menu' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        [Test]
        public void Menu_ExistsAsExactlyOneScreen()
        {
            var root = BuildTree();
            int menus = root.Query<VisualElement>(className: "screen").ToList()
                .Count(s => s.name == "menu");
            Assert.AreEqual(1, menus, "There must be exactly one screen named 'menu'.");
        }

        [Test]
        public void Menu_HasExactlyOneSafeAreaContent()
        {
            var screen = HomeScreen(BuildTree());
            int count = screen.Query<VisualElement>(className: "safe-area-content").ToList().Count;
            Assert.AreEqual(1, count,
                $"'menu' must contain exactly one .safe-area-content (found {count}).");
        }

        [Test]
        public void HomeBackground_IsFullBleed_OutsideSafeAreaContent()
        {
            var screen = HomeScreen(BuildTree());
            var bg = screen.Q<VisualElement>(className: "home-bg");
            Assert.IsNotNull(bg, "Expected a .home-bg full-bleed background layer.");
            Assert.IsNull(NearestSafeAreaAncestor(bg),
                ".home-bg must not be a descendant of .safe-area-content (it must bleed full-screen).");
        }

        [Test]
        public void CombineEntry_Exists_AndTargetScreenExists()
        {
            var root = BuildTree();
            var screen = HomeScreen(root);

            var cta = screen.Q<VisualElement>("go-combineIntro");
            Assert.IsNotNull(cta, "Home must expose the Combine entry named 'go-combineIntro'.");

            // The navigator convention: name "go-<screenId>" must point at a real screen.
            string target = cta.name.Substring(NavPrefix.Length);
            var targetScreen = root.Q<VisualElement>(target);
            Assert.IsNotNull(targetScreen, $"Combine entry target screen '{target}' must exist.");
            Assert.IsTrue(targetScreen.ClassListContains("screen"),
                $"Combine entry target '{target}' must be a .screen.");
        }

        [Test]
        public void Dock_ExposesHomeMapTechniquesProfile()
        {
            var screen = HomeScreen(BuildTree());
            foreach (var name in new[] { "nav-home", "nav-map", "nav-techniques", "nav-profile" })
            {
                Assert.IsNotNull(screen.Q<VisualElement>(name),
                    $"Bottom dock must expose a tab named '{name}'.");
            }
        }

        [Test]
        public void HomeTab_HasActiveStateClass()
        {
            var screen = HomeScreen(BuildTree());
            var home = screen.Q<VisualElement>("nav-home");
            Assert.IsNotNull(home, "Expected a 'nav-home' tab.");
            Assert.IsTrue(home.ClassListContains("home-tab--active"),
                "The Home tab must carry the active-state class 'home-tab--active'.");
        }

        [Test]
        public void UnavailableTabs_HaveExplicitLockedClass()
        {
            var screen = HomeScreen(BuildTree());
            foreach (var name in new[] { "nav-map", "nav-techniques", "nav-profile" })
            {
                var tab = screen.Q<VisualElement>(name);
                Assert.IsNotNull(tab, $"Expected a '{name}' tab.");
                Assert.IsTrue(tab.ClassListContains("home-tab--locked"),
                    $"Unavailable tab '{name}' must carry the explicit 'home-tab--locked' class.");
                Assert.IsFalse(tab.ClassListContains("home-tab--active"),
                    $"Unavailable tab '{name}' must not also be active.");
            }
        }

        [Test]
        public void CombineCta_UsesReusableTouchTargetClass()
        {
            var screen = HomeScreen(BuildTree());
            var cta = screen.Q<VisualElement>("go-combineIntro");
            Assert.IsNotNull(cta, "Expected the Combine CTA 'go-combineIntro'.");
            Assert.IsTrue(cta.ClassListContains("tap-target"),
                "The Combine CTA must use the reusable '.tap-target' (>= 48x48) class.");
        }

        [Test]
        public void Dock_UsesLargerTouchTargetClass()
        {
            var screen = HomeScreen(BuildTree());
            // Dock tabs must use the larger touch-target class (>= 56x56 logical,
            // no flex-shrink) so the dock never collapses below a tappable size
            // on phone-landscape resolutions.
            foreach (var name in new[] { "nav-home", "nav-map", "nav-techniques", "nav-profile" })
            {
                var tab = screen.Q<VisualElement>(name);
                Assert.IsNotNull(tab, $"Expected a dock tab named '{name}'.");
                Assert.IsTrue(tab.ClassListContains("tap-target-lg"),
                    $"Dock tab '{name}' must use the larger '.tap-target-lg' touch-target class.");
            }
        }

        [Test]
        public void DockIcons_UseLargerReusableIconClass()
        {
            var screen = HomeScreen(BuildTree());
            // Each dock tab's visible glyph must use the larger reusable nav-icon
            // size class (and the non-shrinking .home-icon base).
            foreach (var name in new[] { "nav-home", "nav-map", "nav-techniques", "nav-profile" })
            {
                var tab = screen.Q<VisualElement>(name);
                Assert.IsNotNull(tab, $"Expected a dock tab named '{name}'.");
                var glyph = tab.Q<VisualElement>(className: "home-tab__glyph");
                Assert.IsNotNull(glyph, $"Dock tab '{name}' must contain a .home-tab__glyph icon.");
                Assert.IsTrue(glyph.ClassListContains("home-icon"),
                    $"Dock icon in '{name}' must use the non-shrinking '.home-icon' base class.");
                Assert.IsTrue(glyph.ClassListContains("home-icon--nav"),
                    $"Dock icon in '{name}' must use the larger '.home-icon--nav' size class.");
            }
        }

        [Test]
        public void RibbonIcons_UseRibbonVisibleSizeClass()
        {
            var screen = HomeScreen(BuildTree());
            var glyphs = screen.Query<VisualElement>(className: "home-chip__glyph").ToList();
            Assert.AreEqual(2, glyphs.Count, "Expected two ribbon chip icons (streak + balance).");
            foreach (var glyph in glyphs)
            {
                Assert.IsTrue(glyph.ClassListContains("home-icon"),
                    "Ribbon icons must use the non-shrinking '.home-icon' base class.");
                Assert.IsTrue(glyph.ClassListContains("home-icon--ribbon"),
                    "Ribbon icons must use the '.home-icon--ribbon' visible-size class.");
            }
        }

        [Test]
        public void CtaArrow_UsesCtaVisibleSizeClass()
        {
            var screen = HomeScreen(BuildTree());
            var cta = screen.Q<VisualElement>("go-combineIntro");
            Assert.IsNotNull(cta, "Expected the Combine CTA 'go-combineIntro'.");
            var arrow = cta.Q<VisualElement>(className: "home-icon--cta");
            Assert.IsNotNull(arrow, "The CTA must contain an arrow icon using '.home-icon--cta'.");
            Assert.IsTrue(arrow.ClassListContains("home-icon"),
                "The CTA arrow must use the non-shrinking '.home-icon' base class.");
        }

        [Test]
        public void AllVisibleIcons_UseNonShrinkingBaseClass()
        {
            var screen = HomeScreen(BuildTree());
            // Every visible icon on Home carries the .home-icon base (flex-shrink:0,
            // centered) so flex layout can never collapse the artwork.
            var icons = screen.Query<VisualElement>(className: "home-icon").ToList();
            Assert.GreaterOrEqual(icons.Count, 7,
                "Expected at least 7 .home-icon elements (4 dock + 2 ribbon + 1 CTA arrow).");
        }

        [Test]
        public void OldUnboundMenuControls_AreGone()
        {
            var screen = HomeScreen(BuildTree());

            Assert.IsEmpty(screen.Query<VisualElement>(className: "navbtn").ToList(),
                "The old .navbtn controls must be removed from the Home screen.");

            var staleTexts = new HashSet<string> { "Stats", "Collection", "Game", "Support" };
            foreach (var button in screen.Query<Button>().ToList())
            {
                Assert.IsFalse(staleTexts.Contains(button.text),
                    $"Old unbound control '{button.text}' must be removed from the Home screen.");
            }
        }

        [Test]
        public void NoActiveButton_LacksActionOrDisabledState()
        {
            var screen = HomeScreen(BuildTree());
            // Every production-looking active control (a Button) on Home must
            // either be a navigator (name "go-…", wired by ScreenManager) or be
            // explicitly locked/disabled — never a silently-dead button.
            foreach (var button in screen.Query<Button>().ToList())
            {
                bool isNavigator = !string.IsNullOrEmpty(button.name) && button.name.StartsWith(NavPrefix);
                bool isLockedOrDisabled =
                    button.ClassListContains("home-tab--locked") ||
                    button.ClassListContains("locked") ||
                    button.ClassListContains("disabled") ||
                    !button.enabledSelf;

                Assert.IsTrue(isNavigator || isLockedOrDisabled,
                    $"Button '{button.name}' (text '{button.text}') must have a defined action " +
                    "(go- navigator) or an explicit disabled/locked state.");
            }
        }
    }
}
