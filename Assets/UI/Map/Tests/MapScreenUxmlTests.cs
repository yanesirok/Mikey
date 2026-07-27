using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Structural contract for the full-viewport cinematic Map screen in
    /// MikeyApp.uxml, rebuilt from the approved reference
    /// (map_okinawa_approved_reference.jpg): a map stage (left, ~61.7%) with
    /// checkpoint dots and a route line drawn directly over the map (no cards,
    /// no badges, no "SOON" capsules, no legacy territory-shape overlays), and
    /// a docked cinematic preview panel (right, ~38.3%) — one full-height video
    /// with layered dark overlays, never a separate solid content panel below
    /// it — showing Okinawa's title/description/stats/CTA laid directly on top
    /// of the video.
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

        // No legacy translucent red/green "territory shape" overlays remain over
        // the map background — the approved reference is a plain dark/fog treatment.
        [Test]
        public void NoLegacyTerritoryOverlays_RemainOnMapBackground()
        {
            var bg = MapScreen(BuildTree()).Q<VisualElement>(className: "map-bg");
            Assert.IsEmpty(bg.Query<VisualElement>(className: "map-glow").ToList(), "'.map-glow' territory shape must not remain.");
            Assert.IsEmpty(bg.Query<VisualElement>(className: "map-ink-wash").ToList(), "'.map-ink-wash' territory shape must not remain.");
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
        // inside) the left map area — the two regions of the approved split.
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
        }

        // No max-width cap and no centered container on the full-viewport Map
        // layout, and the docked panel is approximately the approved 38.3% width.
        [Test]
        public void MapLayout_HasNoMaxWidthCap_AndPanelIsApprovedWidth()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);

            string layoutBlock = ExtractRuleBlock(uss, ".map-layout {");
            Assert.IsNotNull(layoutBlock, "Expected a '.map-layout' rule in Map.uss.");
            StringAssert.DoesNotContain("max-width", layoutBlock, ".map-layout must not use a max-width cap.");
            StringAssert.DoesNotContain("align-self: center", layoutBlock, ".map-layout must not center itself as a desktop-webpage-style container.");

            string detailBlock = ExtractRuleBlock(uss, ".map-detail {");
            Assert.IsNotNull(detailBlock, "Expected a '.map-detail' rule in Map.uss.");
            StringAssert.Contains("width: 38.3%", detailBlock, "'.map-detail' must be approximately 38.3% wide, per the approved reference.");
        }

        // Map contains go-menu, and it targets the existing 'menu' screen — the one
        // minimal way back to Home for this cinematic presentation.
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

        // Okinawa is a simple dot + label — a real, clickable Button, but NOT
        // wrapped in a rectangular card, and NOT a "go-" navigator (MapLevelPreviewController
        // opens the level-detail panel instead of jumping straight to Techniques).
        [Test]
        public void OkinawaCheckpoint_IsSimpleDot_NotACard_AndClickableButNotAGoNavigator()
        {
            var root = BuildTree();
            var node = MapScreen(root).Q<Button>("map-node-okinawa");
            Assert.IsNotNull(node, "Map must expose the Okinawa checkpoint as a real Button named 'map-node-okinawa'.");
            Assert.IsTrue(node.ClassListContains("map-pin--current"), "Okinawa must carry the '.map-pin--current' (available/red) state class.");
            Assert.IsFalse(node.name.StartsWith(NavPrefix),
                "Selecting the Okinawa checkpoint must open the detail panel, not navigate directly.");

            // Exactly a marker (dot + glow) and a label -- no extra rectangular
            // "card" wrapper or background box.
            var directChildren = node.Children().ToList();
            Assert.AreEqual(2, directChildren.Count, "Okinawa's pin must contain exactly a marker and a label, no extra card wrapper.");
            Assert.IsNotNull(node.Q<VisualElement>(className: "map-pin__marker"), "Expected the dot/glow marker.");
            Assert.IsNotNull(node.Q<Label>(className: "map-pin__label"), "Expected the plain text label.");
            Assert.IsNull(node.Q<VisualElement>(className: "map-pin__glyph"), "Legacy kanji-badge glyph must not remain.");
        }

        // No legacy roadmap dashboard, city-card list, dock, or "SOON" capsule
        // remains on the approved cinematic Map layout.
        [Test]
        public void NoLegacyDashboardOrBadgeElements_RemainOnMap()
        {
            var screen = MapScreen(BuildTree());
            foreach (var legacyClass in new[]
            {
                "map-hub", "map-route", "map-dock", "map-tab", "map-chapter",
                "map-belt", "map-progress", "map-breadcrumb", "map-btn",
                "map-node--available", "map-node--locked",
                "map-pin__badge", "map-pin__glyph",
                "map-detail__preview", "map-detail__body", "map-detail__video-scrim"
            })
            {
                Assert.IsEmpty(screen.Query<VisualElement>(className: legacyClass).ToList(),
                    $"Legacy class '.{legacyClass}' must not remain on the approved Map layout.");
            }
            Assert.IsNull(screen.Q<VisualElement>("nav-map"), "The old dock's 'nav-map' active-tab indicator must not remain.");

            string text = string.Join(" ", screen.Query<Label>().ToList().Select(l => l.text));
            StringAssert.DoesNotContain("SOON", text, "No 'SOON' capsule/label text may remain in checkpoint UI.");
        }

        // Detail panel's own Start Lesson action still routes to the existing
        // 'techniques' screen; "go-techniques" remains a real navigator elsewhere
        // (Home/Techniques/Profile docks, Practice's back action), so no
        // navigation id required elsewhere is removed by dropping Map's own dock.
        [Test]
        public void MapDetailStart_TargetsExistingTechniquesScreen_AndElsewhereUnaffected()
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

        // The full-height preview video (and its fallback) live directly inside
        // map-detail — not nested inside a separate, shorter "preview" sub-container.
        [Test]
        public void PreviewVideo_IsFullHeight_DirectChildOfMapDetail()
        {
            var panel = MapScreen(BuildTree()).Q<VisualElement>("map-detail");
            Assert.IsNotNull(panel, "Map must expose a 'map-detail' cinematic panel.");

            var video = panel.Q<VisualElement>("map-detail-video");
            Assert.IsNotNull(video, "Detail panel must expose a 'map-detail-video' inline preview target.");
            Assert.AreSame(panel, video.parent, "The preview video must be a direct child of 'map-detail' (full-height, not nested in a shorter sub-container).");

            var fallback = panel.Q<VisualElement>("map-detail-video-fallback");
            Assert.IsNotNull(fallback, "Detail panel must expose a safe static fallback element for a failed preview load.");
            Assert.AreSame(panel, fallback.parent, "The fallback must be a direct child of 'map-detail', matching the full-height video it replaces.");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string videoBlock = ExtractRuleBlock(uss, ".map-detail__video {");
            Assert.IsNotNull(videoBlock, "Expected a '.map-detail__video' rule in Map.uss.");
            StringAssert.Contains("bottom: 0", videoBlock, "The preview video must be full-bleed (absolute, inset to all four edges) inside the panel, i.e. full height — not a banner.");
        }

        // There is no separate solid-black content panel below the video: the
        // text content wrapper sits directly over the video/overlays and has no
        // background color of its own.
        [Test]
        public void NoSeparateSolidContentPanel_BelowVideo()
        {
            var panel = MapScreen(BuildTree()).Q<VisualElement>("map-detail");
            var content = panel.Q<VisualElement>(className: "map-detail__content");
            Assert.IsNotNull(content, "Expected a '.map-detail__content' wrapper for the title/description/stats/CTA.");
            Assert.AreSame(panel, content.parent, "'.map-detail__content' must be a direct child of 'map-detail', overlaid on the same video (not a separate lower container).");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-detail__content {");
            Assert.IsNotNull(block, "Expected a '.map-detail__content' rule in Map.uss.");
            StringAssert.DoesNotContain("background-color", block,
                "'.map-detail__content' must not paint its own solid background — it sits directly over the video + overlays.");
        }

        // Layered dark overlays exist to keep the lower information area readable
        // while scenery stays visible behind the upper portion of the video.
        [Test]
        public void DarkOverlays_ExistOverVideo_ForLegibility()
        {
            var panel = MapScreen(BuildTree()).Q<VisualElement>("map-detail");
            var overlays = panel.Query<VisualElement>(className: "map-detail__overlay").ToList();
            Assert.GreaterOrEqual(overlays.Count, 2, "Expected at least 2 stacked dark overlay layers approximating the reference's gradient.");
            foreach (var overlay in overlays)
                Assert.AreSame(panel, overlay.parent, "Each overlay must be a direct child of 'map-detail', layered over the same full-height video.");
        }

        // The detail panel exposes title/subtitle/description/stats/CTA as real
        // UI Toolkit elements, never baked into the video.
        [Test]
        public void DetailPanel_HasExpectedCopyAndStats()
        {
            var screen = MapScreen(BuildTree());
            var panel = screen.Q<VisualElement>("map-detail");

            var title = panel.Q<Label>(className: "map-detail__title");
            Assert.IsNotNull(title, "Detail panel must expose a title label.");
            Assert.AreEqual("OKINAWA", title.text);

            var subtitle = panel.Q<Label>(className: "map-detail__subtitle");
            Assert.IsNotNull(subtitle, "Detail panel must expose a subtitle label.");
            Assert.AreEqual("Where it all began", subtitle.text);

            Assert.IsNotNull(panel.Q<Label>(className: "map-detail__desc"),
                "Detail panel must expose a description label.");

            var stats = panel.Query<VisualElement>(className: "map-detail__stat").ToList();
            Assert.AreEqual(3, stats.Count, "Expected exactly three stat rows (Lessons, Techniques, Progress).");

            var start = panel.Q<Button>("map-detail-start");
            Assert.IsNotNull(start, "Detail panel must expose a Start Lesson CTA.");
            Assert.IsFalse(start.ClassListContains("btn"),
                "Start Lesson CTA must not use the width:100% global '.btn' class.");
        }

        // The Start Lesson CTA is left-aligned and a fixed, content-appropriate
        // size (~200x60 at the 1280x720 baseline) — never full-width.
        [Test]
        public void StartLessonCta_IsLeftAligned_FixedSize_NotFullWidth()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-detail__cta {");
            Assert.IsNotNull(block, "Expected a '.map-detail__cta' rule in Map.uss.");
            StringAssert.Contains("align-self: flex-start", block,
                "Start Lesson must be left-aligned (align-self: flex-start), not stretched full-width.");
            StringAssert.DoesNotContain("width: 100%", block, "Start Lesson must not use width:100%.");
            StringAssert.Contains("width: 200px", block, "Start Lesson must use the approved ~200px baseline width.");
        }

        // The panel starts hidden (Map.uss ".map-detail" is display:none by default;
        // MapLevelPreviewController is the only thing that adds the "--open" modifier),
        // and Okinawa starts un-selected (not yet the brightened/selected glow).
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
                "Okinawa must not start as the selected (brightened-glow) checkpoint.");
        }

        // Tonokku, Kanto and the future checkpoint remain honestly non-interactive:
        // never a Button, never picked, so they structurally cannot ever open
        // Okinawa or start a lesson. Required labels exist (TONOKKU / KANTO); the
        // future checkpoint intentionally has none (cropped in the reference).
        [Test]
        public void LockedCheckpoints_CannotOpenOkinawaOrStartALesson_AndHaveExpectedLabels()
        {
            var screen = MapScreen(BuildTree());
            foreach (var className in LockedCheckpointClasses)
            {
                var nodes = screen.Query<VisualElement>(className: className).ToList();
                Assert.AreEqual(1, nodes.Count, $"Expected exactly one '.{className}' checkpoint.");
                var node = nodes[0];

                Assert.IsFalse(node is Button, $"Locked checkpoint '.{className}' must not be an active Button.");
                Assert.AreEqual(PickingMode.Ignore, node.pickingMode,
                    $"Locked checkpoint '.{className}' must not be pickable (cannot be clicked at all).");
                Assert.IsTrue(string.IsNullOrEmpty(node.name) || !node.name.StartsWith(NavPrefix),
                    $"Locked checkpoint '.{className}' must not be a 'go-' navigator.");
                Assert.IsTrue(node.ClassListContains("map-pin--locked"),
                    $"Locked checkpoint '.{className}' must carry the '.map-pin--locked' state class.");
            }

            var tonokku = screen.Q<VisualElement>(className: "map-pin--tonokku");
            Assert.IsNotNull(tonokku.Q<Label>(className: "map-pin__label"));
            Assert.AreEqual("TONOKKU", tonokku.Q<Label>(className: "map-pin__label").text);

            var kanto = screen.Q<VisualElement>(className: "map-pin--kanto");
            Assert.IsNotNull(kanto.Q<Label>(className: "map-pin__label"));
            Assert.AreEqual("KANTO", kanto.Q<Label>(className: "map-pin__label").text);

            var future = screen.Q<VisualElement>(className: "map-pin--future");
            Assert.IsNull(future.Q<Label>(className: "map-pin__label"),
                "The future/northern checkpoint intentionally has no label (cropped in the approved reference).");
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

        // Route line: several short segments approximating a curved trail
        // directly over the map, never baked into the background image.
        [Test]
        public void RouteLine_ExistsAsMultiSegmentOverlay()
        {
            var stage = MapScreen(BuildTree()).Q<VisualElement>(className: "map-stage");
            var segments = stage.Query<VisualElement>(className: "map-route-seg").ToList();
            Assert.GreaterOrEqual(segments.Count, 5, "Expected several short segments (not 3 giant diagonals) approximating a curved trail.");
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

        // Interactive controls keep a real (if invisible) minimum touch-target
        // size even where they're styled as small/unobtrusive plain-text
        // controls (no visible pill/box) — accessibility without visual bulk.
        [Test]
        public void InteractiveControls_KeepMinimumTouchTargetSize()
        {
            var screen = MapScreen(BuildTree());
            foreach (var name in new[] { "go-menu", "map-node-okinawa", "map-detail-close", "map-detail-start" })
            {
                var ctrl = screen.Q<Button>(name);
                Assert.IsNotNull(ctrl, $"Expected control '{name}'.");
                bool hasTouchTarget = ctrl.ClassListContains("tap-target") || ctrl.ClassListContains("tap-target-lg");
                Assert.IsTrue(hasTouchTarget,
                    $"Control '{name}' must keep a minimum touch-target class ('.tap-target' or '.tap-target-lg'), even if visually minimal.");
            }
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
