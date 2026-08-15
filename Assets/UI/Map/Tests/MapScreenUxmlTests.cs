using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Structural contract for the full-viewport cinematic Map screen in
    /// MikeyApp.uxml — MVP composition: Okinawa is the only playable
    /// destination, so its docked cinematic panel (right, ~38.3%) is a
    /// structural constant (never hidden/collapsed) and MapLevelPreviewController
    /// auto-selects it the moment the screen is entered — the user never sees a
    /// bare full-width Map state first. The left map (~61.7%) shows the approved
    /// reference's route/checkpoint art (map_okinawa_approved_reference.jpg,
    /// cropped to Assets/UI/Media/Images/japan_route_map_mvp.png) as ONE
    /// flattened static image — the legacy full-screen asia_map background is
    /// disabled for this screen so there is never a second map behind it — held
    /// inside an aspect-ratio-locked ".map-artboard" (790:720) so it never
    /// stretches or re-crops, with a single transparent Button hotspot over
    /// Okinawa's position INSIDE that same artboard (kept for re-selection, not
    /// required) that stays aligned with the visible dot even when the artboard
    /// pillarboxes within a wider stage (e.g. ultrawide 2400x1080). There is no
    /// panel close action for this MVP — Home/back is the only way to leave Map.
    /// </summary>
    public class MapScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string MapArtPath = "Assets/UI/Media/Images/japan_route_map_mvp.png";
        private const string MapArt2xPath = "Assets/UI/Media/Images/japan_route_map_mvp@2x.png";
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

        // Both the 1x (source-of-truth crop) and 2x (actually displayed,
        // higher-quality) MVP map art assets exist on disk.
        [Test]
        public void MvpMapArtAssets_ExistOnDisk()
        {
            Assert.IsTrue(File.Exists(MapArtPath), $"Expected the cropped MVP map artboard at {MapArtPath}.");
            Assert.IsTrue(File.Exists(MapArt2xPath), $"Expected the high-resolution MVP map artboard at {MapArt2xPath}.");
        }

        // The runtime map art rule (".map-art") references the high-resolution
        // 2x asset (not the 1x file) and uses scale-to-fit, never scale-and-crop.
        [Test]
        public void MapArt_ReferencesHighResolutionAsset_WithScaleToFit()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-art {");
            Assert.IsNotNull(block, "Expected a '.map-art' rule in Map.uss.");
            StringAssert.Contains("japan_route_map_mvp@2x.png", block,
                "'.map-art' must reference the high-resolution japan_route_map_mvp@2x.png asset.");
            StringAssert.DoesNotContain("scale-and-crop", block,
                "The flattened map artwork must use scale-to-fit, not scale-and-crop (never re-cropped).");
            StringAssert.Contains("scale-to-fit", block,
                "The flattened map artwork must use scale-to-fit to preserve its exact aspect ratio.");
        }

        // .map-artboard preserves the approved reference's exact 790:720 aspect
        // ratio and is sized to the maximum that fits inside .map-stage
        // (definite height + aspect-ratio, centered by .map-stage), never
        // stretched.
        [Test]
        public void MapArtboard_PreservesApproved790x720AspectRatio()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);

            string artboardBlock = ExtractRuleBlock(uss, ".map-artboard {");
            Assert.IsNotNull(artboardBlock, "Expected a '.map-artboard' rule in Map.uss.");
            StringAssert.Contains("aspect-ratio:", artboardBlock, "'.map-artboard' must declare an explicit aspect-ratio (790:720).");
            StringAssert.Contains("height: 100%", artboardBlock, "'.map-artboard' must fill the stage height so aspect-ratio computes a fitted (non-stretched) width.");
            StringAssert.DoesNotContain("width:", artboardBlock, "'.map-artboard' must not hardcode a width — it must be computed from height + aspect-ratio.");

            string stageBlock = ExtractRuleBlock(uss, ".map-stage {");
            Assert.IsNotNull(stageBlock, "Expected a '.map-stage' rule in Map.uss.");
            StringAssert.Contains("justify-content: center", stageBlock, "'.map-stage' must horizontally center the (possibly narrower) artboard.");
        }

        // The map stage's foreground (artboard + hotspot) lives inside the
        // safe-area wrapper, alongside the docked panel.
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

        // The docked panel is structurally separate from (a sibling of, not
        // nested inside) the left map stage.
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

        // Map contains go-menu, and it targets the existing 'menu' screen — the
        // one minimal way back to Home.
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

        // Structural fix for the hotspot-misalignment bug: the map art and the
        // Okinawa hotspot must share the SAME .map-artboard parent (not the
        // wider .map-stage), so the hotspot's percentage position always
        // matches the actual displayed (possibly pillarboxed) artwork.
        [Test]
        public void MapArtAndOkinawaHotspot_ShareTheSameArtboardParent()
        {
            var screen = MapScreen(BuildTree());
            var artboard = screen.Q<VisualElement>(className: "map-artboard");
            Assert.IsNotNull(artboard, "Expected a '.map-artboard' container.");

            var art = screen.Q<VisualElement>(className: "map-art");
            Assert.IsNotNull(art, "Expected a '.map-art' element for the flattened map artwork.");
            Assert.AreSame(artboard, art.parent, "'.map-art' must be a direct child of '.map-artboard'.");

            var hotspot = screen.Q<Button>("map-node-okinawa");
            Assert.IsNotNull(hotspot, "Expected the Okinawa hotspot.");
            Assert.AreSame(artboard, hotspot.parent,
                "The Okinawa hotspot must be a direct child of '.map-artboard' (the SAME parent as '.map-art'), not '.map-stage' directly — this is what keeps it aligned with the visible artwork when the artboard pillarboxes.");

            var stage = screen.Q<VisualElement>(className: "map-stage");
            Assert.AreSame(stage, artboard.parent, "'.map-artboard' must be a direct child of '.map-stage'.");
        }

        // The Okinawa hotspot exists as a real, clickable Button, positioned
        // over the flattened artboard, but is NOT a "go-" navigator
        // (MapLevelPreviewController opens the level-detail panel instead of
        // jumping straight to Techniques), and has no visible button/card
        // styling of its own — no background, no border, no label.
        [Test]
        public void OkinawaHotspot_IsTransparentAndClickable_ButNotAGoNavigator()
        {
            var root = BuildTree();
            var hotspot = MapScreen(root).Q<Button>("map-node-okinawa");
            Assert.IsNotNull(hotspot, "Map must expose the Okinawa hotspot as a real Button named 'map-node-okinawa'.");
            Assert.IsFalse(hotspot.name.StartsWith(NavPrefix),
                "Selecting the Okinawa hotspot must open the detail panel, not navigate directly.");

            // No visible button chrome: no child label/glyph, no card wrapper.
            Assert.AreEqual(0, hotspot.childCount, "The Okinawa hotspot must have no child elements (no Unity-generated label, no card/glyph).");
            Assert.IsFalse(hotspot.ClassListContains("btn"), "The hotspot must not use the global '.btn' styled-button class.");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-node-okinawa {");
            Assert.IsNotNull(block, "Expected a '.map-node-okinawa' rule in Map.uss.");
            StringAssert.Contains("border-width: 0", block, "The hotspot must have no visible border.");
            StringAssert.IsMatch(@"background-color:\s*rgba\(\s*0,\s*0,\s*0,\s*0\s*\)", block,
                "The hotspot must have a fully transparent background (no visible button/card).");
        }

        // The Okinawa hotspot's clickable area is intentionally larger than a
        // typical visible dot, for usability, and positioned within the
        // artboard's coordinate space (not hardcoded pixel values that would
        // drift at other resolutions).
        [Test]
        public void OkinawaHotspot_HasGenerousTapArea_PositionedByPercentage()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-node-okinawa {");
            Assert.IsNotNull(block);
            StringAssert.Contains("left:", block);
            StringAssert.Contains("top:", block);
            StringAssert.Contains("%", block, "Hotspot position must be percentage-based so it stays aligned when the stage scales.");
        }

        // No legacy roadmap dashboard, city-card list, dock, "SOON" capsule, or
        // the previous dynamic dot/route-segment rendering remains on the
        // approved MVP Map layout — the whole composition is now the flattened
        // artboard plus the transparent hotspot.
        [Test]
        public void NoLegacyDynamicMapElements_Remain()
        {
            var screen = MapScreen(BuildTree());
            foreach (var legacyClass in new[]
            {
                "map-hub", "map-route", "map-dock", "map-tab", "map-chapter",
                "map-belt", "map-progress", "map-breadcrumb", "map-btn",
                "map-node--available", "map-node--locked",
                "map-pin", "map-pin__marker", "map-pin__dot", "map-pin__glow", "map-pin__label", "map-pin__badge", "map-pin__glyph",
                "map-route-seg", "map-stage__art",
                "map-detail__preview", "map-detail__body", "map-detail__video-scrim",
                "map-detail__close", "map-detail__close-icon"
            })
            {
                Assert.IsEmpty(screen.Query<VisualElement>(className: legacyClass).ToList(),
                    $"Legacy class '.{legacyClass}' must not remain on the approved MVP Map layout.");
            }
            Assert.IsNull(screen.Q<VisualElement>("nav-map"), "The old dock's 'nav-map' active-tab indicator must not remain.");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            foreach (var legacySelector in new[] { ".map-pin ", ".map-pin{", ".map-route-seg" })
                StringAssert.DoesNotContain(legacySelector, uss, $"Map.uss must not retain dead '{legacySelector}' rules.");
        }

        // Detail panel's own Start Lesson action still routes to the existing
        // 'techniques' screen; "go-techniques" remains a real navigator
        // elsewhere (Practice's back action), so no navigation regression was
        // introduced by the MVP map correction.
        [Test]
        public void MapDetailStart_TargetsExistingTechniquesScreen_AndElsewhereUnaffected()
        {
            var root = BuildTree();
            var start = MapScreen(root).Q<Button>("map-detail-start");
            Assert.IsNotNull(start, "Map's detail panel must expose a 'map-detail-start' Start Lesson action.");
            Assert.IsTrue(root.Q<VisualElement>("techniques").ClassListContains("screen"),
                "The 'techniques' screen Start Lesson routes to must exist.");

            // "menu" is intentionally excluded here: Home's Techniques tab is now
            // progression-gated as 'home-nav-techniques' (see HomeController /
            // HomeScreenUxmlTests), not a static 'go-techniques' navigator. Profile's
            // dock is likewise gated (progression-bound 'profile-nav-techniques' via
            // ProfileController, not a static navigator) so it can no longer bypass
            // the Level1Unlocked lock — see ProfileScreenUxmlTests /
            // ProfileProgressionTests. Practice's back action is unaffected, still a
            // real 'go-techniques' navigator (Practice is only reachable once Level 1
            // is already unlocked).
            foreach (var elsewhere in new (string screenId, string navName)[]
            {
                ("practice", "go-techniques"),
            })
            {
                var screen = root.Q<VisualElement>(elsewhere.screenId);
                Assert.IsNotNull(screen, $"Expected screen '{elsewhere.screenId}'.");
                Assert.IsNotEmpty(screen.Query<VisualElement>(name: elsewhere.navName).ToList(),
                    $"'{elsewhere.navName}' must still exist on '{elsewhere.screenId}' (not removed elsewhere by the Map correction).");
            }
        }

        // The Okinawa hotspot and the panel's Start Lesson action are local,
        // controller-bound actions — NOT go- navigators (mirrors Practice's
        // practice-action/practice-complete convention).
        [Test]
        public void LocalPanelActions_AreNotNavigators()
        {
            var screen = MapScreen(BuildTree());
            foreach (var name in new[] { "map-node-okinawa", "map-detail-start" })
            {
                var ctrl = screen.Q<Button>(name);
                Assert.IsNotNull(ctrl, $"Expected the local action '{name}'.");
                Assert.IsFalse(ctrl.name.StartsWith(NavPrefix),
                    $"Local state-changing action '{name}' must not use a 'go-' navigator name.");
            }
        }

        // For this MVP, the panel has no close action: Home/back is the only
        // way to leave Map, and the panel must never collapse back into the
        // broken full-width stage state.
        [Test]
        public void PanelCloseAction_IsAbsent()
        {
            var screen = MapScreen(BuildTree());
            Assert.IsNull(screen.Q<Button>("map-detail-close"), "The panel close action must be removed for this MVP.");
            Assert.IsNull(screen.Q<VisualElement>(className: "map-detail__close"), "No close-button element/styling may remain.");
        }

        // The full-height preview video (and its fallback) live directly inside
        // map-detail — the video lifecycle target names MapLevelPreviewController
        // queries are unchanged by the MVP map correction.
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

        // Layered dark overlays exist to keep the lower information area
        // readable while scenery stays visible behind the upper portion of the
        // video.
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

        // MVP: Okinawa is the only playable destination, so the panel is a
        // structural constant — Map.uss must never hide ".map-detail" (no
        // display:none / conditional "--open" gate), so the Map screen can
        // never collapse into the broken full-width-stage state regardless of
        // controller/selection timing.
        [Test]
        public void DetailPanel_IsStructurallyAlwaysVisible_NeverCollapsesStage()
        {
            var screen = MapScreen(BuildTree());
            Assert.IsNotNull(screen.Q<VisualElement>("map-detail"));

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string detailBlock = ExtractRuleBlock(uss, ".map-detail {");
            Assert.IsNotNull(detailBlock, "Expected a '.map-detail' rule in Map.uss.");
            StringAssert.DoesNotContain("display: none", detailBlock,
                "'.map-detail' must never be display:none — the panel must remain visible for the whole Map screen visit.");
        }

        // Entering the Map screen automatically selects Okinawa (opens the
        // panel, starts its preview) — the user must never see a bare,
        // full-width Map state first. Verified at the controller level in
        // MapLevelPreviewControllerTests (EnteringMapScreen_AutoSelectsDefaultCheckpoint)
        // and here structurally: the scene's single checkpoint binding is
        // Okinawa, so it is unambiguously "the default".
        [Test]
        public void OkinawaIsTheOnlyCheckpointBinding_SoItIsUnambiguouslyDefault()
        {
            var screen = MapScreen(BuildTree());
            var hotspots = screen.Query<Button>(className: "tap-target").ToList()
                .Where(b => b.name == "map-node-okinawa").ToList();
            Assert.AreEqual(1, hotspots.Count, "Expected exactly one Okinawa hotspot to serve as the default checkpoint.");
        }

        // The legacy full-screen asia_map image must not be visible behind the
        // flattened MVP map artwork: Map.uss disables map-bg-media (the
        // BackgroundMediaController target element) specifically for this
        // screen, without touching the shared background-media wiring itself.
        [Test]
        public void LegacyAsiaMapBackground_IsDisabled_ForMapScreenOnly()
        {
            var screen = MapScreen(BuildTree());
            // The element (and BackgroundMediaController's binding) must still
            // exist — only its visibility is turned off — so Title/Home/Combine's
            // shared wiring is provably untouched.
            Assert.IsNotNull(screen.Q<VisualElement>("map-bg-media"),
                "'map-bg-media' element must still exist (BackgroundMediaController wiring untouched); only its visibility is disabled.");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "#map-bg-media {");
            Assert.IsNotNull(block, "Expected a '#map-bg-media' rule in Map.uss disabling the legacy background.");
            StringAssert.Contains("display: none", block, "'#map-bg-media' must be display:none so no second map renders behind the flattened artwork.");
        }

        // Only the flattened MVP artwork supplies the visible left map: no
        // other background-image rule targets the map stage/background layer.
        [Test]
        public void OnlyMvpArtwork_SuppliesTheVisibleLeftMap()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string mapBgBlock = ExtractRuleBlock(uss, ".map-bg {");
            Assert.IsNotNull(mapBgBlock, "Expected a '.map-bg' rule in Map.uss.");
            StringAssert.DoesNotContain("background-image", mapBgBlock,
                "'.map-bg' must not itself carry a background-image — the only visible map image is '.map-art' (japan_route_map_mvp@2x.png).");
        }

        // No active-looking control lacks an action: every Button on Map is
        // either a go- navigator or a known local controller-bound action.
        private static readonly string[] LocalControllerActions =
            { "map-node-okinawa", "map-detail-start" };

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
        // size even where they're styled as small/unobtrusive or fully
        // transparent controls — accessibility without visual bulk.
        [Test]
        public void InteractiveControls_KeepMinimumTouchTargetSize()
        {
            var screen = MapScreen(BuildTree());
            foreach (var name in new[] { "go-menu", "map-node-okinawa", "map-detail-start" })
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
