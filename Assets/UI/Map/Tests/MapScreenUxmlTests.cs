using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Structural contract for the landscape Map / progression hub (the "map"
    /// screen) in MikeyApp.uxml: exactly one screen with one safe-area wrapper,
    /// a full-bleed background outside that wrapper, a working Home (go-menu)
    /// action, the first reference-supported node (Okinawa) exposed as a real
    /// go-techniques navigator that is visually distinct from honestly-locked
    /// later cities, a canonical 4-tab dock with the Map tab active and the
    /// Profile tab explicitly locked, and reusable touch-target / visible-icon /
    /// responsive-wrapping classes so nothing collapses or overflows on
    /// phone-landscape sizes. Mirrors TechniquesScreenUxmlTests.
    /// </summary>
    public class MapScreenUxmlTests
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

        private static VisualElement MapScreen(VisualElement root)
        {
            var screen = root.Q<VisualElement>("map");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'map'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'map' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        // 1 — 'map' exists exactly once.
        [Test]
        public void Map_ExistsAsExactlyOneScreen()
        {
            var count = BuildTree().Query<VisualElement>(className: "screen").ToList()
                .Count(s => s.name == "map");
            Assert.AreEqual(1, count, "There must be exactly one screen named 'map'.");
        }

        // 3 — exactly one safe-area wrapper on the Map screen.
        [Test]
        public void Map_HasExactlyOneSafeAreaContent()
        {
            var count = MapScreen(BuildTree()).Query<VisualElement>(className: "safe-area-content").ToList().Count;
            Assert.AreEqual(1, count, $"'map' must contain exactly one .safe-area-content (found {count}).");
        }

        // 4 — Map's full-bleed background is outside its wrapper.
        [Test]
        public void Background_IsFullBleed_OutsideSafeArea()
        {
            var bg = MapScreen(BuildTree()).Q<VisualElement>(className: "map-bg");
            Assert.IsNotNull(bg, "Expected a .map-bg full-bleed background.");
            Assert.IsNull(NearestSafeAreaAncestor(bg), ".map-bg must not be inside .safe-area-content.");
        }

        // 5 — Map foreground/navigation is inside its wrapper.
        [Test]
        public void ForegroundLayout_IsInsideSafeArea()
        {
            var screen = MapScreen(BuildTree());
            var layout = screen.Q<VisualElement>(className: "map-layout");
            Assert.IsNotNull(layout, "Expected a .map-layout foreground container.");
            Assert.IsNotNull(NearestSafeAreaAncestor(layout), ".map-layout must be inside .safe-area-content.");

            var dock = screen.Q<VisualElement>(className: "map-dock");
            Assert.IsNotNull(dock, "Expected a .map-dock bottom navigation container.");
            Assert.IsNotNull(NearestSafeAreaAncestor(dock), ".map-dock must be inside .safe-area-content.");
        }

        // 11 + 13 — Map contains go-menu, and it targets the existing 'menu' screen.
        [Test]
        public void Map_HasGoMenuHomeAction_TargetingMenu()
        {
            var root = BuildTree();
            var back = MapScreen(root).Q<Button>("go-menu");
            Assert.IsNotNull(back, "Map must expose a 'go-menu' Home action (a real Button).");

            string target = back.name.Substring(NavPrefix.Length);
            Assert.AreEqual("menu", target, "go-menu must target the 'menu' screen.");
            Assert.IsTrue(root.Q<VisualElement>(target).ClassListContains("screen"),
                "The 'menu' target screen must exist.");
        }

        // 12 + 13 + 18 — the first progression node is a real go-techniques navigator
        // to the existing techniques screen, and is the visually-distinct available node.
        [Test]
        public void FirstNode_IsGoTechniquesNavigator_ToTechniquesScreen()
        {
            var root = BuildTree();
            var node = MapScreen(root).Q<Button>(className: "map-node--available");
            Assert.IsNotNull(node, "Map must expose the first available node as a Button with '.map-node--available'.");
            Assert.AreEqual("go-techniques", node.name,
                "The first available node must be a 'go-techniques' navigator.");

            string target = node.name.Substring(NavPrefix.Length);
            Assert.AreEqual("techniques", target, "go-techniques must target the 'techniques' screen.");
            Assert.IsTrue(root.Q<VisualElement>(target).ClassListContains("screen"),
                "The 'techniques' target screen must exist.");
        }

        // 19 — locked map nodes are explicitly styled and are NOT active navigators.
        [Test]
        public void LockedNodes_AreExplicitlyLocked_AndNotNavigators()
        {
            var locked = MapScreen(BuildTree())
                .Query<VisualElement>(className: "map-node--locked").ToList();
            Assert.GreaterOrEqual(locked.Count, 3, "Expected at least three honestly-locked cities.");

            foreach (var card in locked)
            {
                Assert.IsFalse(card is Button,
                    "A locked node must not be an active Button (no silent clickability).");
                Assert.IsTrue(string.IsNullOrEmpty(card.name) || !card.name.StartsWith(NavPrefix),
                    "A locked node must not be a 'go-' navigator.");
                var badge = card.Q<VisualElement>(className: "map-node__badge");
                Assert.IsNotNull(badge, "A locked node must show an explicit coming-soon badge.");
            }
        }

        // 25 — available-node and locked-node classes are distinct.
        [Test]
        public void AvailableAndLockedNodeClasses_AreDistinct()
        {
            var screen = MapScreen(BuildTree());
            var available = screen.Query<VisualElement>(className: "map-node--available").ToList();
            var locked = screen.Query<VisualElement>(className: "map-node--locked").ToList();

            Assert.AreEqual(1, available.Count, "Expected exactly one available node.");
            Assert.GreaterOrEqual(locked.Count, 3, "Expected several locked nodes.");

            // No node may be both available and locked.
            foreach (var node in screen.Query<VisualElement>(className: "map-node").ToList())
            {
                bool isAvailable = node.ClassListContains("map-node--available");
                bool isLocked = node.ClassListContains("map-node--locked");
                Assert.IsFalse(isAvailable && isLocked,
                    "A node must not carry both the available and locked state classes.");
            }
        }

        // 13 + 14 + 15 + 16 — the dock exposes the canonical 4-tab model with real
        // actions on Home/Techniques/Profile, and the Map tab active.
        [Test]
        public void Dock_HasActiveMap_AndRealHomeTechniquesProfileActions()
        {
            var root = BuildTree();
            var dock = MapScreen(root).Q<VisualElement>(className: "map-dock");
            Assert.IsNotNull(dock, "Map must have a '.map-dock' bottom navigation dock.");

            // Map tab active (14)
            var mapTab = dock.Q<VisualElement>("nav-map");
            Assert.IsNotNull(mapTab, "Dock must contain a 'nav-map' tab.");
            Assert.IsTrue(mapTab.ClassListContains("map-tab--active"),
                "The Map tab must carry the active-state class 'map-tab--active'.");
            Assert.IsFalse(mapTab.ClassListContains("map-tab--locked"),
                "The active Map tab must not also be locked.");

            // Home + Techniques + Profile are real navigators (15)
            var home = dock.Q<VisualElement>("go-menu");
            Assert.IsNotNull(home, "Dock Home tab must be a 'go-menu' navigator.");
            Assert.IsTrue(root.Q<VisualElement>("menu").ClassListContains("screen"),
                "Dock 'go-menu' must target the existing 'menu' screen.");

            var tech = dock.Q<VisualElement>("go-techniques");
            Assert.IsNotNull(tech, "Dock Techniques tab must be a 'go-techniques' navigator.");
            Assert.IsTrue(root.Q<VisualElement>("techniques").ClassListContains("screen"),
                "Dock 'go-techniques' must target the existing 'techniques' screen.");

            var profile = dock.Q<VisualElement>("go-profile");
            Assert.IsNotNull(profile, "Dock Profile tab must be a 'go-profile' navigator.");
            Assert.IsTrue(root.Q<VisualElement>("profile").ClassListContains("screen"),
                "Dock 'go-profile' must target the existing 'profile' screen.");
            Assert.IsFalse(profile.ClassListContains("map-tab--locked"),
                "The Profile tab must no longer carry the explicit 'map-tab--locked' class.");
            Assert.IsFalse(profile.ClassListContains("map-tab--active"),
                "The Profile tab must not be active on Map.");
        }

        // 17 — no active-looking tab/control lacks an action: every Button is a
        // navigator, and every non-locked dock tab is either active or a go- navigator.
        [Test]
        public void NoActiveLookingControl_LacksAnAction()
        {
            var screen = MapScreen(BuildTree());

            foreach (var button in screen.Query<Button>().ToList())
            {
                bool isNavigator = !string.IsNullOrEmpty(button.name) && button.name.StartsWith(NavPrefix);
                bool isLockedOrDisabled =
                    button.ClassListContains("map-node--locked") ||
                    button.ClassListContains("map-tab--locked") ||
                    !button.enabledSelf;
                Assert.IsTrue(isNavigator || isLockedOrDisabled,
                    $"Button '{button.name}' (text '{button.text}') must be a go- navigator or explicitly locked/disabled.");
            }

            foreach (var tab in screen.Query<VisualElement>(className: "map-tab").ToList())
            {
                bool isNavigator = !string.IsNullOrEmpty(tab.name) && tab.name.StartsWith(NavPrefix);
                bool isActive = tab.ClassListContains("map-tab--active");
                bool isLocked = tab.ClassListContains("map-tab--locked");
                Assert.IsTrue(isNavigator || isActive || isLocked,
                    $"Dock tab '{tab.name}' must be a navigator, the active tab, or explicitly locked.");
            }
        }

        // 20 — interactive controls use the reusable minimum touch-target class.
        [Test]
        public void InteractiveControls_UseTouchTargetClass()
        {
            var screen = MapScreen(BuildTree());
            // The Home navigator and the dock tabs.
            foreach (var name in new[] { "go-menu", "nav-map", "go-profile" })
            {
                var ctrl = screen.Q<VisualElement>(name);
                Assert.IsNotNull(ctrl, $"Expected control '{name}'.");
                Assert.IsTrue(ctrl.ClassListContains("tap-target-lg"),
                    $"Control '{name}' must use the '.tap-target-lg' (>=56x56) touch-target class.");
            }

            // The available node Button (go-techniques) is the primary actionable control.
            var node = screen.Q<Button>(className: "map-node--available");
            Assert.IsNotNull(node, "Expected the available node Button.");
            Assert.IsTrue(node.ClassListContains("tap-target-lg"),
                "The available node must use the '.tap-target-lg' touch-target class.");
        }

        // 21 + 22 — visible icons use explicit reusable size classes on the non-shrinking base.
        [Test]
        public void VisibleIcons_UseExplicitSizeClasses_OnNonShrinkingBase()
        {
            var screen = MapScreen(BuildTree());

            // header hub mark
            var mark = screen.Q<VisualElement>(className: "map-headline__mark");
            Assert.IsNotNull(mark, "Expected a hub header mark icon.");
            Assert.IsTrue(mark.ClassListContains("map-icon") && mark.ClassListContains("map-icon--hub"),
                "Header mark must use '.map-icon' + '.map-icon--hub'.");

            // available node glyph: large
            var available = screen.Q<Button>(className: "map-node--available");
            var nodeGlyph = available.Q<VisualElement>(className: "map-node__glyph");
            Assert.IsNotNull(nodeGlyph, "Available node must have a glyph.");
            Assert.IsTrue(nodeGlyph.ClassListContains("map-icon") && nodeGlyph.ClassListContains("map-icon--node"),
                "Available-node glyph must use '.map-icon' + '.map-icon--node'.");

            // locked node glyphs: smaller, but still on the non-shrinking base
            var lockedGlyphs = screen.Query<VisualElement>(className: "map-node__glyph--locked").ToList();
            Assert.GreaterOrEqual(lockedGlyphs.Count, 3, "Expected glyphs on locked nodes.");
            foreach (var glyph in lockedGlyphs)
            {
                Assert.IsTrue(glyph.ClassListContains("map-icon"),
                    "Locked-node glyphs must use the non-shrinking '.map-icon' base class.");
                Assert.IsTrue(glyph.ClassListContains("map-icon--node-locked"),
                    "Locked-node glyphs must use the explicit '.map-icon--node-locked' size class.");
            }

            // dock icons — scope the lookup to the dock, because 'go-menu' and
            // 'go-techniques' legitimately appear twice on this screen (once as a
            // hub action, once as a dock tab); within the dock each name is unique.
            var dock = screen.Q<VisualElement>(className: "map-dock");
            Assert.IsNotNull(dock, "Expected a '.map-dock' container.");
            foreach (var name in new[] { "go-menu", "nav-map", "go-techniques", "go-profile" })
            {
                var tab = dock.Q<VisualElement>(name);
                Assert.IsNotNull(tab, $"Dock must contain a tab named '{name}'.");
                var glyph = tab.Q<VisualElement>(className: "map-tab__glyph");
                Assert.IsNotNull(glyph, $"Dock tab '{name}' must contain a .map-tab__glyph icon.");
                Assert.IsTrue(glyph.ClassListContains("map-icon"),
                    $"Dock icon in '{name}' must use the non-shrinking '.map-icon' base class.");
                Assert.IsTrue(glyph.ClassListContains("map-icon--nav"),
                    $"Dock icon in '{name}' must use the '.map-icon--nav' size class.");
            }
        }

        // 23 — the route/node container uses responsive layout classes.
        [Test]
        public void Layout_UsesResponsiveContainers()
        {
            var screen = MapScreen(BuildTree());
            Assert.IsNotNull(screen.Q<VisualElement>(className: "map-layout"),
                "Expected a '.map-layout' responsive (wrapping) two-band container.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "map-route"),
                "Expected a '.map-route' responsive node-path container.");
            Assert.IsNotEmpty(screen.Query<VisualElement>(className: "map-actionbar").ToList(),
                "Expected at least one '.map-actionbar' responsive (wrapping) action container.");
        }

        // 24 — sibling action buttons must NOT reintroduce the overflow-prone
        // width:100% global .btn class.
        [Test]
        public void ActionButtons_DoNotUseFullWidthGlobalBtn()
        {
            var screen = MapScreen(BuildTree());
            foreach (var btn in screen.Query<Button>().ToList())
            {
                Assert.IsFalse(btn.ClassListContains("btn"),
                    $"Action button '{btn.name}' must not use the width:100% global '.btn' class " +
                    "(use the content-sized '.map-btn' / node classes instead).");
            }
        }
    }
}
