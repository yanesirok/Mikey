using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Structural contract for the full-viewport landscape Map "selected-level"
    /// screen in MikeyApp.uxml: exactly one screen with one safe-area wrapper, a
    /// full-bleed background outside that wrapper, a map stage (left, ~62-64%)
    /// with checkpoints and a route line drawn directly over the map (never
    /// baked into the background image), a docked level-detail panel (right,
    /// ~36-38%) with no centered/max-width container, a minimal Home action
    /// (replacing the old large 4-tab dock), and the Okinawa checkpoint exposed
    /// as a local checkpoint-select action (visually distinct from the honestly
    /// locked Tonokku / Kanto / future checkpoints) that opens the panel instead
    /// of navigating directly.
    /// </summary>
    public class MapScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string NavPrefix = "go-";

        private static readonly string[] LockedCheckpointClasses =
            { "map-pin--tonokku", "map-pin--kanto", "map-pin--future" };

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

        private static string ExtractRuleBlock(string uss, string header)
        {
            int start = uss.IndexOf(header, System.StringComparison.Ordinal);
            if (start < 0)
                return null;

            int open = uss.IndexOf('{', start);
            if (open < 0)
                return null;

            int depth = 0;
            for (int i = open; i < uss.Length; i++)
            {
                if (uss[i] == '{')
                    depth++;
                else if (uss[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return uss.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }

        // 'map' exists exactly once.
        [Test]
        public void Map_ExistsAsExactlyOneScreen()
        {
            var count = BuildTree().Query<VisualElement>(className: "screen").ToList()
                .Count(s => s.name == "map");
            Assert.AreEqual(1, count, "There must be exactly one screen named 'map'.");
        }

        // Exactly one safe-area wrapper on the Map screen.
        [Test]
        public void Map_HasExactlyOneSafeAreaContent()
        {
            var count = MapScreen(BuildTree()).Query<VisualElement>(className: "safe-area-content").ToList().Count;
            Assert.AreEqual(1, count, $"'map' must contain exactly one .safe-area-content (found {count}).");
        }

        // Map's full-bleed background is outside its wrapper and stays untouched.
        [Test]
        public void Background_IsFullBleed_OutsideSafeArea()
        {
            var bg = MapScreen(BuildTree()).Q<VisualElement>(className: "map-bg");
            Assert.IsNotNull(bg, "Expected a .map-bg full-bleed background.");
            Assert.IsNull(NearestSafeAreaAncestor(bg), ".map-bg must not be inside .safe-area-content.");
        }

        // Map's foreground (stage + docked panel) lives inside the safe-area wrapper.
        [Test]
        public void ForegroundLayout_IsInsideSafeArea()
        {
            var screen = MapScreen(BuildTree());
            var layout = screen.Q<VisualElement>(className: "map-layout");
            Assert.IsNotNull(layout, "Expected a .map-layout foreground container.");
            Assert.IsNotNull(NearestSafeAreaAncestor(layout), ".map-layout must be inside .safe-area-content.");

            var stage = screen.Q<VisualElement>(className: "map-stage");
            Assert.IsNotNull(stage, "Expected a .map-stage left map area.");
            Assert.IsNotNull(NearestSafeAreaAncestor(stage), ".map-stage must be inside .safe-area-content.");

            var panel = screen.Q<VisualElement>("map-detail");
            Assert.IsNotNull(panel, "Expected the docked 'map-detail' panel.");
            Assert.IsNotNull(NearestSafeAreaAncestor(panel), "'map-detail' must be inside .safe-area-content.");
        }

        // The docked panel is structurally separate from (a sibling of, not nested
        // inside) the left map area — the two regions of the approved full-viewport
        // split.
        [Test]
        public void DetailPanel_IsStructurallySeparate_FromMapStage()
        {
            var screen = MapScreen(BuildTree());
            var layout = screen.Q<VisualElement>(className: "map-layout");
            var stage = screen.Q<VisualElement>(className: "map-stage");
            var panel = screen.Q<VisualElement>("map-detail");

            Assert.AreSame(layout, stage.parent, ".map-stage must be a direct child of .map-layout.");
            Assert.AreSame(layout, panel.parent, "'map-detail' must be a direct child of .map-layout (a sibling of .map-stage, not nested inside it).");
            Assert.IsNull(stage.Q<VisualElement>("map-detail"), ".map-stage must not contain the detail panel.");
            Assert.IsNull(panel.Q<VisualElement>(className: "map-stage"), "'map-detail' must not contain the map stage.");
        }

        // No 1500px (or any) max-width cap and no centered container on the
        // full-viewport Map layout — the approved design forbids the old
        // desktop-webpage-style centered composition.
        [Test]
        public void MapLayout_HasNoMaxWidthCap_OrCenteredContainer()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-layout {");
            Assert.IsNotNull(block, "Expected a '.map-layout' rule in Map.uss.");
            StringAssert.DoesNotContain("max-width", block, ".map-layout must not use a max-width cap.");
            StringAssert.DoesNotContain("align-self: center", block, ".map-layout must not center itself as a desktop-webpage-style container.");
        }

        // Map contains go-menu, and it targets the existing 'menu' screen — the one
        // minimal way back to Home for this selected-level presentation.
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

        // Okinawa is exposed directly over the map as a checkpoint pin: a real,
        // clickable Button, but a LOCAL checkpoint-select action (NOT a "go-"
        // navigator) — MapLevelPreviewController opens the level-detail panel
        // instead of jumping straight to Techniques.
        [Test]
        public void OkinawaCheckpoint_IsClickable_ButNotAGoNavigator()
        {
            var root = BuildTree();
            var node = MapScreen(root).Q<Button>("map-node-okinawa");
            Assert.IsNotNull(node, "Map must expose the Okinawa checkpoint as a real Button named 'map-node-okinawa'.");
            Assert.IsTrue(node.ClassListContains("map-pin--current"), "Okinawa must carry the '.map-pin--current' (available/red) state class.");
            Assert.IsFalse(node.name.StartsWith(NavPrefix),
                "Selecting the Okinawa checkpoint must open the detail panel, not navigate directly.");
        }

        // No legacy roadmap city-card list, dashboard hub, or big bottom dock
        // remain on the approved full-viewport Map layout.
        [Test]
        public void NoLegacyRoadmapDashboard_RemainsOnMap()
        {
            var screen = MapScreen(BuildTree());
            foreach (var legacyClass in new[]
            {
                "map-hub", "map-route", "map-dock", "map-tab", "map-chapter",
                "map-belt", "map-progress", "map-breadcrumb", "map-btn",
                "map-node--available", "map-node--locked"
            })
            {
                Assert.IsEmpty(screen.Query<VisualElement>(className: legacyClass).ToList(),
                    $"Legacy roadmap-dashboard class '.{legacyClass}' must not remain on the approved Map layout.");
            }
            Assert.IsNull(screen.Q<VisualElement>("nav-map"), "The old dock's 'nav-map' active-tab indicator must not remain.");
        }

        // Detail panel's own Start Lesson action still routes to the existing
        // 'techniques' screen; "go-techniques" remains a real navigator elsewhere
        // (Home/Techniques/Profile docks, Practice's back action), so no
        // navigation id required elsewhere is removed by dropping Map's own dock.
        [Test]
        public void MapDetailStart_TargetsExistingTechniquesScreen()
        {
            var root = BuildTree();
            var start = MapScreen(root).Q<Button>("map-detail-start");
            Assert.IsNotNull(start, "Map's detail panel must expose a 'map-detail-start' Start Lesson action.");
            Assert.IsTrue(root.Q<VisualElement>("techniques").ClassListContains("screen"),
                "The 'techniques' screen Start Lesson routes to must exist.");

            foreach (var elsewhere in new (string screenId, string navName)[]
            {
                ("menu", "go-techniques"),
                ("profile", "go-techniques"),
                ("practice", "go-techniques"),
            })
            {
                var screen = root.Q<VisualElement>(elsewhere.screenId);
                Assert.IsNotNull(screen, $"Expected screen '{elsewhere.screenId}'.");
                Assert.IsNotEmpty(screen.Query<VisualElement>(name: elsewhere.navName).ToList(),
                    $"'{elsewhere.navName}' must still exist on '{elsewhere.screenId}' (not removed elsewhere by the Map correction).");
            }
        }

        // The Okinawa checkpoint, the panel close action and the panel's Start
        // Lesson action are local, controller-bound actions — NOT go- navigators
        // (mirrors Practice's practice-action/practice-complete convention).
        [Test]
        public void LocalPanelActions_AreNotNavigators()
        {
            var screen = MapScreen(BuildTree());
            foreach (var name in new[] { "map-node-okinawa", "map-detail-close", "map-detail-start" })
            {
                var ctrl = screen.Q<Button>(name);
                Assert.IsNotNull(ctrl, $"Expected the local action '{name}'.");
                Assert.IsFalse(ctrl.name.StartsWith(NavPrefix),
                    $"Local state-changing action '{name}' must not use a 'go-' navigator name.");
            }
        }

        // The Okinawa level-detail panel exists, contains an inline preview video
        // target (a real UI Toolkit element, not baked into the video), a static
        // fallback, title/subtitle/description, Lessons/Techniques/Progress stats,
        // and a Start Lesson CTA — all real UI Toolkit elements.
        [Test]
        public void DetailPanel_Exists_WithPreviewAndCopyAndStats()
        {
            var screen = MapScreen(BuildTree());
            var panel = screen.Q<VisualElement>("map-detail");
            Assert.IsNotNull(panel, "Map must expose a 'map-detail' level-detail panel.");

            Assert.IsNotNull(panel.Q<VisualElement>("map-detail-video"),
                "Detail panel must expose a 'map-detail-video' inline preview target.");
            Assert.IsNotNull(panel.Q<VisualElement>("map-detail-video-fallback"),
                "Detail panel must expose a safe static fallback element for a failed preview load.");
            Assert.IsNotNull(panel.Q<Button>("map-detail-close"),
                "Detail panel must expose a close action.");

            var title = panel.Q<Label>(className: "map-detail__title");
            Assert.IsNotNull(title, "Detail panel must expose a title label.");
            Assert.AreEqual("OKINAWA", title.text);

            var subtitle = panel.Q<Label>(className: "map-detail__subtitle");
            Assert.IsNotNull(subtitle, "Detail panel must expose a subtitle label.");
            Assert.AreEqual("Where it all began", subtitle.text);

            Assert.IsNotNull(panel.Q<Label>(className: "map-detail__desc"),
                "Detail panel must expose a description label.");

            var stats = panel.Query<VisualElement>(className: "map-detail__stat").ToList();
            Assert.AreEqual(3, stats.Count, "Expected exactly three stats (Lessons, Techniques, Progress).");

            var start = panel.Q<Button>("map-detail-start");
            Assert.IsNotNull(start, "Detail panel must expose a Start Lesson CTA.");
            Assert.IsFalse(start.ClassListContains("btn"),
                "Start Lesson CTA must not use the width:100% global '.btn' class.");
        }

        // The Start Lesson CTA is left-aligned and content-width, not a full-width
        // stretched bar.
        [Test]
        public void StartLessonCta_IsLeftAligned_NotFullWidth()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-detail__cta {");
            Assert.IsNotNull(block, "Expected a '.map-detail__cta' rule in Map.uss.");
            StringAssert.Contains("align-self: flex-start", block,
                "Start Lesson must be left-aligned (align-self: flex-start), not stretched full-width.");
            StringAssert.DoesNotContain("width: 100%", block,
                "Start Lesson must not use width:100%.");
        }

        // The panel starts hidden (Map.uss ".map-detail" is display:none by default;
        // MapLevelPreviewController is the only thing that adds the "--open" modifier),
        // and Okinawa starts un-selected (not yet the active red-ring checkpoint).
        [Test]
        public void DetailPanel_StartsWithoutOpenModifier()
        {
            var screen = MapScreen(BuildTree());
            var panel = screen.Q<VisualElement>("map-detail");
            Assert.IsNotNull(panel);
            Assert.IsFalse(panel.ClassListContains("map-detail--open"),
                "Detail panel must not carry the open modifier by default.");

            var okinawa = screen.Q<Button>("map-node-okinawa");
            Assert.IsNotNull(okinawa);
            Assert.IsFalse(okinawa.ClassListContains("map-node--selected"),
                "Okinawa must not start as the selected (active) checkpoint.");
        }

        // Tonokku, Kanto and the future checkpoint remain honestly non-interactive:
        // never a Button, never picked, so they structurally cannot ever open
        // Okinawa or start a lesson.
        [Test]
        public void LockedCheckpoints_CannotOpenOkinawaOrStartALesson()
        {
            var screen = MapScreen(BuildTree());
            int found = 0;
            foreach (var className in LockedCheckpointClasses)
            {
                var nodes = screen.Query<VisualElement>(className: className).ToList();
                Assert.AreEqual(1, nodes.Count, $"Expected exactly one '.{className}' checkpoint.");
                var node = nodes[0];
                found++;

                Assert.IsFalse(node is Button, $"Locked checkpoint '.{className}' must not be an active Button.");
                Assert.AreEqual(PickingMode.Ignore, node.pickingMode,
                    $"Locked checkpoint '.{className}' must not be pickable (cannot be clicked at all).");
                Assert.IsTrue(string.IsNullOrEmpty(node.name) || !node.name.StartsWith(NavPrefix),
                    $"Locked checkpoint '.{className}' must not be a 'go-' navigator.");
                Assert.IsTrue(node.ClassListContains("map-pin--locked"),
                    $"Locked checkpoint '.{className}' must carry the '.map-pin--locked' state class.");
            }
            Assert.AreEqual(LockedCheckpointClasses.Length, found, "Expected Tonokku, Kanto and the future checkpoint.");
        }

        // Available (Okinawa) and locked (Tonokku/Kanto/future) checkpoint state
        // classes are distinct — no pin is both.
        [Test]
        public void AvailableAndLockedCheckpointClasses_AreDistinct()
        {
            var screen = MapScreen(BuildTree());
            foreach (var pin in screen.Query<VisualElement>(className: "map-pin").ToList())
            {
                bool isCurrent = pin.ClassListContains("map-pin--current");
                bool isLocked = pin.ClassListContains("map-pin--locked");
                Assert.IsFalse(isCurrent && isLocked,
                    $"Checkpoint '{pin.name}' must not carry both the current/available and locked state classes.");
            }
        }

        // Route line: a simple set of segments connecting checkpoints directly
        // over the map, never baked into the background image.
        [Test]
        public void RouteLine_ExistsAsOverlayElements()
        {
            var stage = MapScreen(BuildTree()).Q<VisualElement>(className: "map-stage");
            var segments = stage.Query<VisualElement>(className: "map-route-seg").ToList();
            Assert.GreaterOrEqual(segments.Count, 3, "Expected at least 3 route segments connecting the 4 checkpoints.");
        }

        // Checkpoint coordinates are real, per-checkpoint USS selectors (easy to
        // retune later), not hardcoded in C# and not baked into the map image.
        [Test]
        public void CheckpointPositions_AreDefinedInUss_PerCheckpoint()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            foreach (var selector in new[] { ".map-pin--okinawa {", ".map-pin--tonokku {", ".map-pin--kanto {", ".map-pin--future {" })
            {
                string block = ExtractRuleBlock(uss, selector);
                Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
                StringAssert.Contains("left:", block, $"'{selector}' must define its horizontal position.");
                StringAssert.Contains("top:", block, $"'{selector}' must define its vertical position.");
            }
        }

        // No active-looking control lacks an action: every Button on Map is either
        // a go- navigator or a known local controller-bound action.
        private static readonly string[] LocalControllerActions =
            { "map-node-okinawa", "map-detail-close", "map-detail-start" };

        [Test]
        public void NoActiveLookingControl_LacksAnAction()
        {
            var screen = MapScreen(BuildTree());
            foreach (var button in screen.Query<Button>().ToList())
            {
                bool isNavigator = !string.IsNullOrEmpty(button.name) && button.name.StartsWith(NavPrefix);
                bool isLocalAction = !string.IsNullOrEmpty(button.name) && LocalControllerActions.Contains(button.name);
                Assert.IsTrue(isNavigator || isLocalAction,
                    $"Button '{button.name}' (text '{button.text}') must be a go- navigator or a known local action.");
            }
        }

        // Interactive controls use the reusable minimum touch-target class. The
        // Home action is deliberately the smaller/unobtrusive '.tap-target'
        // (not '.tap-target-lg') per the approved design's "visually unobtrusive"
        // requirement, while the primary checkpoint and panel CTA stay large.
        [Test]
        public void InteractiveControls_UseTouchTargetClass()
        {
            var screen = MapScreen(BuildTree());

            var home = screen.Q<Button>("go-menu");
            Assert.IsNotNull(home, "Expected the Home action.");
            Assert.IsTrue(home.ClassListContains("tap-target"),
                "The Home action must use at least the '.tap-target' (>=48x48) touch-target class.");

            var node = screen.Q<Button>("map-node-okinawa");
            Assert.IsNotNull(node, "Expected the Okinawa checkpoint Button.");
            Assert.IsTrue(node.ClassListContains("tap-target-lg"),
                "The Okinawa checkpoint must use the '.tap-target-lg' touch-target class.");

            var start = screen.Q<Button>("map-detail-start");
            Assert.IsNotNull(start, "Expected the panel's Start Lesson action.");
            Assert.IsTrue(start.ClassListContains("tap-target-lg"),
                "Start Lesson must use the '.tap-target-lg' touch-target class.");

            var close = screen.Q<Button>("map-detail-close");
            Assert.IsNotNull(close, "Expected the panel's close action.");
            Assert.IsTrue(close.ClassListContains("tap-target"),
                "The panel close action must use the '.tap-target' touch-target class.");
        }

        // Sibling action buttons must NOT reintroduce the overflow-prone
        // width:100% global .btn class.
        [Test]
        public void ActionButtons_DoNotUseFullWidthGlobalBtn()
        {
            var screen = MapScreen(BuildTree());
            foreach (var btn in screen.Query<Button>().ToList())
            {
                Assert.IsFalse(btn.ClassListContains("btn"),
                    $"Action button '{btn.name}' must not use the width:100% global '.btn' class.");
            }
        }
    }
}
