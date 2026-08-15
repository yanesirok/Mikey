using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Structural contract for the rebuilt mobile-first Map screen in
    /// MikeyApp.uxml: a top quick-access bar, a full-screen pan/zoom map stage
    /// (never squeezed by a permanently-reserved side column), LVL0/LVL1
    /// hotspot markers as real UI Toolkit elements (no baked route-image text),
    /// a hidden-by-default detail-panel overlay with per-checkpoint content
    /// blocks, and a Settings overlay. None of the old MVP single-Okinawa
    /// flattened-artboard design remains.
    /// </summary>
    public class MapScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string ReusedMapArtPath = "Assets/UI/Media/Images/asia_map.jpeg";
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
                if (uss[i] == '{') depth++;
                else if (uss[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return uss.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }

        [Test]
        public void Map_ExistsAsExactlyOneScreen()
        {
            var count = BuildTree().Query<VisualElement>(className: "screen").ToList().Count(s => s.name == "map");
            Assert.AreEqual(1, count, "There must be exactly one screen named 'map'.");
        }

        [Test]
        public void Map_HasExactlyOneSafeAreaContent()
        {
            var count = MapScreen(BuildTree()).Query<VisualElement>(className: "safe-area-content").ToList().Count;
            Assert.AreEqual(1, count, $"'map' must contain exactly one .safe-area-content (found {count}).");
        }

        [Test]
        public void Background_IsFullBleed_OutsideSafeArea()
        {
            var bg = MapScreen(BuildTree()).Q<VisualElement>(className: "map-bg");
            Assert.IsNotNull(bg, "Expected a .map-bg full-bleed background.");
            Assert.IsNull(NearestSafeAreaAncestor(bg), ".map-bg must not be inside .safe-area-content.");
        }

        [Test]
        public void ReusedMapArtwork_ExistsOnDisk_AndIsReferencedByCanvas()
        {
            Assert.IsTrue(File.Exists(ReusedMapArtPath), $"Expected the reused ink-wash artwork at {ReusedMapArtPath}.");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), ".map-canvas-art {");
            Assert.IsNotNull(block, "Expected a '.map-canvas-art' rule in Map.uss.");
            StringAssert.Contains("asia_map.jpeg", block, "The pannable canvas must reuse the existing asia_map.jpeg artwork.");
        }

        [Test]
        public void TopBar_ExposesHomeMapTechniquesStats_AndSettings()
        {
            var screen = MapScreen(BuildTree());
            var topbar = screen.Q<VisualElement>(className: "map-topbar");
            Assert.IsNotNull(topbar, "Expected a .map-topbar top quick-access bar.");

            Assert.IsNotNull(topbar.Q<Button>("go-menu"), "Top bar must expose a way back to Home.");
            Assert.IsNotNull(topbar.Q<Button>("go-map"), "Top bar must expose a MAP tab.");
            Assert.IsNotNull(topbar.Q<Button>("map-topbar-techniques"), "Top bar must expose a TECHNIQUES tab.");
            Assert.IsNotNull(topbar.Q<Button>("go-profile"), "Top bar must expose a STATS tab (reusing the Profile screen).");
            Assert.IsNotNull(topbar.Q<Button>("map-settings-open"), "Top bar must expose a Settings icon.");
        }

        [Test]
        public void TopBar_IsInsideSafeArea()
        {
            var topbar = MapScreen(BuildTree()).Q<VisualElement>(className: "map-topbar");
            Assert.IsNotNull(NearestSafeAreaAncestor(topbar), ".map-topbar must respect the safe area.");
        }

        [Test]
        public void MapTab_IsMarkedActive_TechniquesTab_IsNotAGoNavigator_AndLockedByDefault()
        {
            var screen = MapScreen(BuildTree());
            var mapTab = screen.Q<Button>("go-map");
            Assert.IsTrue(mapTab.ClassListContains("map-topbar__tab--active"), "MAP tab must be marked active — we're already here.");

            var techniquesTab = screen.Q<Button>("map-topbar-techniques");
            Assert.IsFalse(techniquesTab.name.StartsWith(NavPrefix),
                "TECHNIQUES must not be a 'go-' navigator — MapLevelPreviewController gates navigation by progression state.");
            Assert.IsTrue(techniquesTab.ClassListContains("map-topbar__tab--locked"),
                "TECHNIQUES tab's markup default must be locked (safe NewPlayer state).");

            var root = BuildTree();
            Assert.IsTrue(root.Q<VisualElement>("techniques").ClassListContains("screen"), "'techniques' target must exist.");
            Assert.IsTrue(root.Q<VisualElement>("profile").ClassListContains("screen"), "'profile' target must exist.");
        }

        [Test]
        public void LevelAndXpReadout_ExistsInTopBar_WithSafeDefaultLevel()
        {
            var topbar = MapScreen(BuildTree()).Q<VisualElement>(className: "map-topbar");
            var level = topbar.Q<Label>("map-topbar-level");
            Assert.IsNotNull(level, "Expected a Level readout in the top bar.");
            Assert.AreEqual("LEVEL 0", level.text, "Markup default must match the safe NewPlayer state (LEVEL 0).");

            var xp = topbar.Q<VisualElement>(className: "map-topbar__xp");
            Assert.IsNotNull(xp, "Expected an XP readout in the top bar (mock data is acceptable).");
        }

        [Test]
        public void MapStage_FillsRemainingSpace_WithNoMaxWidthCap()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string stageBlock = ExtractRuleBlock(uss, ".map-stage {");
            Assert.IsNotNull(stageBlock, "Expected a '.map-stage' rule in Map.uss.");
            StringAssert.Contains("flex-grow: 1", stageBlock, ".map-stage must grow to fill the remaining space — the map is the main focus.");
            StringAssert.DoesNotContain("max-width", stageBlock, ".map-stage must not cap its width.");
        }

        [Test]
        public void MapStage_IsInsideSafeArea()
        {
            var stage = MapScreen(BuildTree()).Q<VisualElement>(className: "map-stage");
            Assert.IsNotNull(stage, "Expected a .map-stage map viewport.");
            Assert.IsNotNull(NearestSafeAreaAncestor(stage), ".map-stage must be inside .safe-area-content.");
        }

        [Test]
        public void CanvasAndBothHotspots_AreChildrenOfTheSamePannableCanvas()
        {
            var screen = MapScreen(BuildTree());
            var canvas = screen.Q<VisualElement>(className: "map-canvas");
            Assert.IsNotNull(canvas, "Expected a '.map-canvas' pannable/zoomable content layer.");

            var art = canvas.Q<VisualElement>(className: "map-canvas-art");
            Assert.IsNotNull(art, "Expected the map artwork inside '.map-canvas'.");
            Assert.AreSame(canvas, art.parent, "The map artwork must be a direct child of '.map-canvas'.");

            var lvl0 = canvas.Q<Button>("map-node-lvl0");
            var lvl1 = canvas.Q<Button>("map-node-lvl1");
            Assert.IsNotNull(lvl0, "Expected the LVL 0 hotspot.");
            Assert.IsNotNull(lvl1, "Expected the LVL 1 hotspot.");
            Assert.AreSame(canvas, lvl0.parent, "LVL 0 hotspot must be a direct child of '.map-canvas' so it pans/zooms with the map.");
            Assert.AreSame(canvas, lvl1.parent, "LVL 1 hotspot must be a direct child of '.map-canvas' so it pans/zooms with the map.");
        }

        [Test]
        public void Hotspots_AreRealButtons_WithVisibleGlyphAndLabel_NotGoNavigators()
        {
            var screen = MapScreen(BuildTree());
            foreach (var name in new[] { "map-node-lvl0", "map-node-lvl1" })
            {
                var hotspot = screen.Q<Button>(name);
                Assert.IsNotNull(hotspot, $"Expected hotspot '{name}'.");
                Assert.IsFalse(hotspot.name.StartsWith(NavPrefix),
                    $"'{name}' must not be a 'go-' navigator — selecting it opens the detail panel instead.");
                Assert.IsNotNull(hotspot.Q<Label>(className: "map-node__glyph"), $"'{name}' must show a clean icon-marker glyph.");
                Assert.IsNotNull(hotspot.Q<Label>(className: "map-node__label"), $"'{name}' must show a visible 'LVL n' label.");
            }

            var lvl0Label = screen.Q<Button>("map-node-lvl0").Q<Label>(className: "map-node__label");
            Assert.AreEqual("LVL 0", lvl0Label.text);
            var lvl1Label = screen.Q<Button>("map-node-lvl1").Q<Label>(className: "map-node__label");
            Assert.AreEqual("LVL 1", lvl1Label.text);
        }

        [Test]
        public void Lvl0Hotspot_IsNotLockedByDefault_Lvl1HotspotIs()
        {
            var screen = MapScreen(BuildTree());
            var lvl0 = screen.Q<Button>("map-node-lvl0");
            var lvl1 = screen.Q<Button>("map-node-lvl1");

            Assert.IsFalse(lvl0.ClassListContains("map-node--locked"), "LVL 0 (the assessment) must never be locked.");
            Assert.IsTrue(lvl1.ClassListContains("map-node--locked"),
                "LVL 1's markup default must be locked (safe NewPlayer state) — still visible and tappable, just dimmed.");
        }

        [Test]
        public void DetailPanel_IsHiddenByDefault_AsAnOverlay_NotALayoutSibling()
        {
            var screen = MapScreen(BuildTree());
            var panel = screen.Q<VisualElement>("map-detail");
            Assert.IsNotNull(panel, "Expected the 'map-detail' panel.");
            Assert.IsFalse(panel.ClassListContains("map-detail--open"),
                "The panel's markup default must not carry the open modifier — hidden until a hotspot is tapped.");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-detail {");
            Assert.IsNotNull(block, "Expected a '.map-detail' rule in Map.uss.");
            StringAssert.Contains("display: none", block, "'.map-detail' must default to hidden.");
            StringAssert.Contains("position: absolute", block,
                "'.map-detail' must be an absolutely-positioned overlay, not a layout-affecting flex sibling that squeezes the map.");

            string openBlock = ExtractRuleBlock(uss, ".map-detail--open {");
            Assert.IsNotNull(openBlock, "Expected a '.map-detail--open' rule toggling the panel visible.");
            StringAssert.Contains("display: flex", openBlock);
        }

        [Test]
        public void DetailPanel_HasTwoContentBlocks_OneForEachCheckpoint()
        {
            var panel = MapScreen(BuildTree()).Q<VisualElement>("map-detail");

            var lvl0Content = panel.Q<VisualElement>("map-detail-content-lvl0");
            var lvl1Content = panel.Q<VisualElement>("map-detail-content-lvl1");
            Assert.IsNotNull(lvl0Content, "Expected an LVL 0 content block.");
            Assert.IsNotNull(lvl1Content, "Expected an LVL 1 content block.");

            Assert.AreEqual("LVL 0", lvl0Content.Q<Label>(className: "map-detail__title").text);
            Assert.AreEqual("LVL 1", lvl1Content.Q<Label>(className: "map-detail__title").text);

            var lvl0Cta = lvl0Content.Q<Button>("map-detail-start-lvl0");
            var lvl1Cta = lvl1Content.Q<Button>("map-detail-start-lvl1");
            Assert.IsNotNull(lvl0Cta, "Expected the LVL 0 Start CTA.");
            Assert.IsNotNull(lvl1Cta, "Expected the LVL 1 Start CTA.");
            Assert.IsFalse(lvl0Cta.name.StartsWith(NavPrefix));
            Assert.IsFalse(lvl1Cta.name.StartsWith(NavPrefix));
        }

        [Test]
        public void Lvl0Cta_IsNotLockedByDefault_Lvl1CtaIs()
        {
            var panel = MapScreen(BuildTree()).Q<VisualElement>("map-detail");
            var lvl0Cta = panel.Q<Button>("map-detail-start-lvl0");
            var lvl1Cta = panel.Q<Button>("map-detail-start-lvl1");

            Assert.IsFalse(lvl0Cta.ClassListContains("map-detail__cta--locked"), "LVL 0's CTA must never be locked.");
            Assert.IsTrue(lvl1Cta.ClassListContains("map-detail__cta--locked"),
                "LVL 1's CTA markup default must be locked (safe NewPlayer state).");
        }

        [Test]
        public void PreviewVideoAndFallback_LiveDirectlyInsideMapDetail()
        {
            var panel = MapScreen(BuildTree()).Q<VisualElement>("map-detail");
            var video = panel.Q<VisualElement>("map-detail-video");
            var fallback = panel.Q<VisualElement>("map-detail-video-fallback");
            Assert.IsNotNull(video, "Detail panel must expose a 'map-detail-video' inline preview target.");
            Assert.IsNotNull(fallback, "Detail panel must expose a safe static fallback element.");
            Assert.AreSame(panel, video.parent);
            Assert.AreSame(panel, fallback.parent);
        }

        [Test]
        public void SettingsOverlay_HasThreeVolumeSlidersAndClose_HiddenByDefault()
        {
            var screen = MapScreen(BuildTree());
            var modal = screen.Q<VisualElement>("map-settings-modal");
            Assert.IsNotNull(modal, "Expected a 'map-settings-modal' overlay.");

            var music = modal.Q<Slider>("map-settings-music");
            var sfx = modal.Q<Slider>("map-settings-sfx");
            var trainer = modal.Q<Slider>("map-settings-trainer");
            Assert.IsNotNull(music);
            Assert.IsNotNull(sfx);
            Assert.IsNotNull(trainer);
            foreach (var slider in new[] { music, sfx, trainer })
            {
                Assert.AreEqual(0f, slider.lowValue);
                Assert.AreEqual(1f, slider.highValue);
            }
            Assert.AreEqual(0.70f, music.value, 0.001f);
            Assert.AreEqual(1.00f, sfx.value, 0.001f);
            Assert.AreEqual(1.00f, trainer.value, 0.001f);
            Assert.IsNotNull(modal.Q<Button>("map-settings-close"));

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), ".map-modal {");
            Assert.IsNotNull(block, "Expected a '.map-modal' rule in Map.uss.");
            StringAssert.Contains("display: none", block, "Settings overlay must default to hidden.");
        }

        [Test]
        public void NoLegacyMvpMapElements_Remain()
        {
            var screen = MapScreen(BuildTree());
            foreach (var legacyClass in new[]
            {
                "map-artboard", "map-art", "map-node-okinawa", "map-home", "map-home__icon", "map-scrim",
                "map-hub", "map-route", "map-dock", "map-tab", "map-chapter", "map-belt", "map-progress",
                "map-breadcrumb", "map-btn", "map-node--available",
                "map-pin", "map-pin__marker", "map-pin__dot", "map-pin__glow", "map-pin__label", "map-pin__badge", "map-pin__glyph",
                "map-route-seg", "map-stage__art", "map-detail__preview", "map-detail__body", "map-detail__video-scrim",
                "map-detail__close", "map-detail__close-icon",
            })
            {
                Assert.IsEmpty(screen.Query<VisualElement>(className: legacyClass).ToList(),
                    $"Legacy class '.{legacyClass}' must not remain on the rebuilt Map screen.");
            }
            Assert.IsNull(screen.Q<VisualElement>("map-bg-media"), "The retired BackgroundMediaController target must be gone — Map no longer uses it.");
            Assert.IsNull(screen.Q<Button>("map-detail-start"), "The old single shared Start Lesson CTA must be gone (replaced by per-checkpoint CTAs).");
        }

        [Test]
        public void NoActiveLookingControl_LacksAnAction()
        {
            var screen = MapScreen(BuildTree());
            var localActions = new System.Collections.Generic.HashSet<string>
            {
                "map-node-lvl0", "map-node-lvl1", "map-detail-start-lvl0", "map-detail-start-lvl1",
                "map-topbar-techniques", "map-settings-open", "map-settings-close",
            };

            foreach (var button in screen.Query<Button>().ToList())
            {
                bool isNavigator = !string.IsNullOrEmpty(button.name) && button.name.StartsWith(NavPrefix);
                bool isLocalAction = !string.IsNullOrEmpty(button.name) && localActions.Contains(button.name);
                Assert.IsTrue(isNavigator || isLocalAction,
                    $"Button '{button.name}' (text '{button.text}') must be a go- navigator or a known local action.");
            }
        }

        [Test]
        public void InteractiveControls_KeepMinimumTouchTargetSize()
        {
            var screen = MapScreen(BuildTree());
            foreach (var name in new[]
            {
                "go-menu", "go-map", "map-topbar-techniques", "go-profile", "map-settings-open",
                "map-node-lvl0", "map-node-lvl1", "map-detail-start-lvl0", "map-detail-start-lvl1",
            })
            {
                var ctrl = screen.Q<Button>(name);
                Assert.IsNotNull(ctrl, $"Expected control '{name}'.");
                bool hasTouchTarget = ctrl.ClassListContains("tap-target") || ctrl.ClassListContains("tap-target-lg");
                Assert.IsTrue(hasTouchTarget, $"Control '{name}' must keep a minimum touch-target class.");
            }
        }

        [Test]
        public void ProductionScreenCount_IsStillTen()
        {
            Assert.AreEqual(10, BuildTree().Query<VisualElement>(className: "screen").ToList().Count,
                "The Map rebuild must not add or remove production screens.");
        }
    }
}
