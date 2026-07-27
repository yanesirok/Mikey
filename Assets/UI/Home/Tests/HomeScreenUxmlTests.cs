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
    /// background outside that wrapper, a progression-driven dynamic primary CTA,
    /// an explicit 4-tab dock whose Map/Techniques tabs default to locked (safe
    /// NewPlayer state) and are driven by HomeController rather than a static
    /// "go-" navigator, reusable touch targets on every interactive control, and
    /// none of the old unbound menu controls.
    /// </summary>
    public class HomeScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string NavPrefix = "go-";

        private static readonly string[] LocalControllerActions = { "home-cta" };

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

        // The CTA is a dynamic, progression-driven control (HomeController), not a
        // static "go-" navigator — its destination varies with tutorial state.
        [Test]
        public void HomeCta_Exists_IsNotAGoNavigator_AndCombineIntroScreenExists()
        {
            var root = BuildTree();
            var screen = HomeScreen(root);

            var cta = screen.Q<Button>("home-cta");
            Assert.IsNotNull(cta, "Home must expose the dynamic primary CTA named 'home-cta'.");
            Assert.IsFalse(cta.name.StartsWith(NavPrefix),
                "'home-cta' must not be a 'go-' navigator — HomeController drives its destination by progression state.");

            // combineIntro is the NewPlayer/IntroCompleted-state destination and
            // must exist as a real screen.
            var combineIntro = root.Q<VisualElement>("combineIntro");
            Assert.IsNotNull(combineIntro, "'combineIntro' screen must exist (the CTA's default-state destination).");
            Assert.IsTrue(combineIntro.ClassListContains("screen"), "'combineIntro' must be a .screen.");
        }

        [Test]
        public void HomeCta_MarkupDefault_MatchesSafeNewPlayerState()
        {
            var screen = HomeScreen(BuildTree());
            var cta = screen.Q<Button>("home-cta");
            Assert.IsNotNull(cta, "Expected the 'home-cta' CTA.");

            var label = cta.Q<Label>(className: "home-cta__text");
            Assert.IsNotNull(label, "Expected the CTA's '.home-cta__text' label.");
            Assert.AreEqual("START LVL 0", label.text,
                "The CTA's markup default text must match the safe NewPlayer state's label.");
        }

        [Test]
        public void Dock_ExposesHomeMapTechniquesProfile()
        {
            var screen = HomeScreen(BuildTree());
            // Map/Techniques are now progression-gated ('home-nav-map' /
            // 'home-nav-techniques'), driven by HomeController rather than a
            // static 'go-' navigator.
            foreach (var name in new[] { "nav-home", "home-nav-map", "home-nav-techniques", "go-profile" })
            {
                Assert.IsNotNull(screen.Q<VisualElement>(name),
                    $"Bottom dock must expose a tab named '{name}'.");
            }
        }

        // Map is progression-gated: not a static "go-" navigator (HomeController
        // decides at click time whether to navigate), but its eventual "map"
        // target screen must exist.
        [Test]
        public void MapTab_IsNotAGoNavigator_ButMapScreenExists()
        {
            var root = BuildTree();
            var screen = HomeScreen(root);

            var tab = screen.Q<VisualElement>("home-nav-map");
            Assert.IsNotNull(tab, "Home must expose the Map tab as 'home-nav-map'.");
            Assert.IsFalse(tab.name.StartsWith(NavPrefix),
                "'home-nav-map' must not be a 'go-' navigator — HomeController gates navigation by progression state.");

            var target = root.Q<VisualElement>("map");
            Assert.IsNotNull(target, "The 'map' target screen must exist.");
            Assert.IsTrue(target.ClassListContains("screen"), "'map' target must be a .screen.");
        }

        // Safe default (NewPlayer): locked/dimmed with a "COMPLETE LVL 0" badge
        // until HomeController unlocks it at Level1Unlocked.
        [Test]
        public void MapTab_IsLockedByDefault_WithCompleteLvl0Badge()
        {
            var screen = HomeScreen(BuildTree());
            var tab = screen.Q<VisualElement>("home-nav-map");
            Assert.IsNotNull(tab, "Expected the 'home-nav-map' tab.");
            Assert.IsTrue(tab.ClassListContains("home-tab--locked"),
                "The Map tab's markup default must carry 'home-tab--locked' (safe NewPlayer state).");

            var badge = tab.Q<Label>(className: "home-tab__badge");
            Assert.IsNotNull(badge, "The locked Map tab must show a badge.");
            Assert.AreEqual("COMPLETE LVL 0", badge.text, "The Map lock badge must read 'COMPLETE LVL 0'.");
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
        public void ProfileTab_IsWorkingNavigator_ToProfileScreen()
        {
            var root = BuildTree();
            var screen = HomeScreen(root);

            var tab = screen.Q<VisualElement>("go-profile");
            Assert.IsNotNull(tab, "Home must expose the Profile tab as a 'go-profile' navigator.");

            string target = tab.name.Substring(NavPrefix.Length);
            Assert.AreEqual("profile", target, "go-profile must target the 'profile' screen.");
            var targetScreen = root.Q<VisualElement>(target);
            Assert.IsNotNull(targetScreen, "The 'profile' target screen must exist.");
            Assert.IsTrue(targetScreen.ClassListContains("screen"), "'profile' target must be a .screen.");
        }

        [Test]
        public void ProfileTab_IsNotLocked()
        {
            var screen = HomeScreen(BuildTree());
            var tab = screen.Q<VisualElement>("go-profile");
            Assert.IsNotNull(tab, "Expected the 'go-profile' tab.");
            Assert.IsFalse(tab.ClassListContains("home-tab--locked"),
                "The Profile tab must never be locked — Profile is always available regardless of progression state.");
            Assert.IsNull(tab.Q<VisualElement>(className: "home-tab__badge"),
                "The Profile tab must not show a lock badge.");
        }

        // Techniques is progression-gated: not a static "go-" navigator, but its
        // eventual "techniques" target screen must exist.
        [Test]
        public void TechniquesTab_IsNotAGoNavigator_ButTechniquesScreenExists()
        {
            var root = BuildTree();
            var screen = HomeScreen(root);

            var tab = screen.Q<VisualElement>("home-nav-techniques");
            Assert.IsNotNull(tab, "Home must expose the Techniques tab as 'home-nav-techniques'.");
            Assert.IsFalse(tab.name.StartsWith(NavPrefix),
                "'home-nav-techniques' must not be a 'go-' navigator — HomeController gates navigation by progression state.");

            var target = root.Q<VisualElement>("techniques");
            Assert.IsNotNull(target, "The 'techniques' target screen must exist.");
            Assert.IsTrue(target.ClassListContains("screen"), "'techniques' target must be a .screen.");
        }

        // Safe default (NewPlayer): locked/dimmed until HomeController unlocks it
        // at Level1Unlocked. No badge requirement for Techniques (Map alone gets
        // the "COMPLETE LVL 0" badge).
        [Test]
        public void TechniquesTab_IsLockedByDefault()
        {
            var screen = HomeScreen(BuildTree());
            var tab = screen.Q<VisualElement>("home-nav-techniques");
            Assert.IsNotNull(tab, "Expected the 'home-nav-techniques' tab.");
            Assert.IsTrue(tab.ClassListContains("home-tab--locked"),
                "The Techniques tab's markup default must carry 'home-tab--locked' (safe NewPlayer state).");
        }

        [Test]
        public void HomeCta_UsesReusableTouchTargetClass()
        {
            var screen = HomeScreen(BuildTree());
            var cta = screen.Q<VisualElement>("home-cta");
            Assert.IsNotNull(cta, "Expected the CTA 'home-cta'.");
            Assert.IsTrue(cta.ClassListContains("tap-target"),
                "The CTA must use the reusable '.tap-target' (>= 48x48) class.");
        }

        [Test]
        public void Dock_UsesLargerTouchTargetClass()
        {
            var screen = HomeScreen(BuildTree());
            // Dock tabs must use the larger touch-target class (>= 56x56 logical,
            // no flex-shrink) so the dock never collapses below a tappable size
            // on phone-landscape resolutions, regardless of lock state.
            foreach (var name in new[] { "nav-home", "home-nav-map", "home-nav-techniques", "go-profile" })
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
            // size class (and the non-shrinking .home-icon base), regardless of lock state.
            foreach (var name in new[] { "nav-home", "home-nav-map", "home-nav-techniques", "go-profile" })
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
            var cta = screen.Q<VisualElement>("home-cta");
            Assert.IsNotNull(cta, "Expected the CTA 'home-cta'.");
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
            // either be a navigator (name "go-…", wired by ScreenManager), a known
            // local controller-bound action (e.g. the dynamic 'home-cta', wired by
            // HomeController), or be explicitly locked/disabled — never a
            // silently-dead button. Dev-only controls (home-dev-*) are excluded:
            // they're hidden outside the Editor/dev build and covered separately.
            foreach (var button in screen.Query<Button>().ToList())
            {
                if (!string.IsNullOrEmpty(button.name) && button.name.StartsWith("home-dev-"))
                    continue;

                bool isNavigator = !string.IsNullOrEmpty(button.name) && button.name.StartsWith(NavPrefix);
                bool isLocalAction = !string.IsNullOrEmpty(button.name) && LocalControllerActions.Contains(button.name);
                bool isLockedOrDisabled =
                    button.ClassListContains("home-tab--locked") ||
                    button.ClassListContains("locked") ||
                    button.ClassListContains("disabled") ||
                    !button.enabledSelf;

                Assert.IsTrue(isNavigator || isLocalAction || isLockedOrDisabled,
                    $"Button '{button.name}' (text '{button.text}') must have a defined action " +
                    "(go- navigator or known local action) or an explicit disabled/locked state.");
            }
        }

        // Compact, visually separate, dev-only tutorial-progression switcher
        // (HomeController hides it outside the Editor/dev build).
        [Test]
        public void DevBar_ExposesAllProgressionStateButtons()
        {
            var screen = HomeScreen(BuildTree());
            var devBar = screen.Q<VisualElement>("home-devbar");
            Assert.IsNotNull(devBar, "Expected a 'home-devbar' dev-only progression switcher.");

            foreach (var name in new[]
            {
                "home-dev-reset", "home-dev-new-player", "home-dev-combine-started",
                "home-dev-level1-unlocked", "home-dev-lesson-started", "home-dev-lesson-completed",
            })
            {
                Assert.IsNotNull(devBar.Q<Button>(name), $"Expected a dev-control Button named '{name}'.");
            }
        }
    }
}
