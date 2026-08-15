using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Structural contract for the Japan world map screen ("map") in
    /// MikeyApp.uxml: full-bleed pannable japan_map_final.jpg with real UI
    /// Toolkit chapter markers (Okinawa unlocked, Tohoku locked), no
    /// auto-selected chapter/open panel on entry, and the always-visible top
    /// quick-access bar. Retires the old flattened single-image MVP
    /// (japan_route_map_mvp@2x.png, the permanent 61.7/38.3 split, the
    /// always-open Okinawa panel) — see NoLegacyMvpMapElements_Remain.
    /// </summary>
    public class JapanMapScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string JapanMapJpgPath = "Assets/UI/Media/Images/japan_map_final.jpg";

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

        [Test]
        public void Map_ExistsAsExactlyOneScreen()
        {
            int count = BuildTree().Query<VisualElement>(className: "screen").ToList().Count(s => s.name == "map");
            Assert.AreEqual(1, count, "There must be exactly one screen named 'map'.");
        }

        [Test]
        public void JapanMapJpg_ExistsOnDisk_AndIsReferencedByMapCanvasArt()
        {
            Assert.IsTrue(File.Exists(JapanMapJpgPath), $"Expected the final Japan map art at {JapanMapJpgPath}.");
            Assert.IsTrue(File.Exists(JapanMapJpgPath + ".meta"), "Japan map JPG must have an imported .meta file.");

            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-canvas-art {");
            Assert.IsNotNull(block, "Expected a '.map-canvas-art' rule in Map.uss.");
            StringAssert.Contains("japan_map_final.jpg", block);
        }

        [Test]
        public void DefaultState_NoChapterSelected_PanelClosed()
        {
            var screen = MapScreen(BuildTree());

            var panel = screen.Q<VisualElement>("chapter-panel");
            Assert.IsNotNull(panel, "Expected a 'chapter-panel' overlay.");
            Assert.IsFalse(panel.ClassListContains("detail-panel--open"),
                "Entering Map must never auto-select a chapter or open the panel.");

            Assert.IsFalse(screen.Q<Button>("chapter-node-okinawa").ClassListContains("chapter-node--selected"));
            Assert.IsFalse(screen.Q<Button>("chapter-node-tohoku").ClassListContains("chapter-node--selected"));
        }

        [Test]
        public void OkinawaChapterMarker_Exists_UnlockedAndTappable()
        {
            var okinawa = MapScreen(BuildTree()).Q<Button>("chapter-node-okinawa");
            Assert.IsNotNull(okinawa, "Expected an unlocked 'chapter-node-okinawa' marker.");
            Assert.IsFalse(okinawa.ClassListContains("chapter-node--locked"), "Okinawa (Chapter 0) must be unlocked.");
        }

        [Test]
        public void TohokuChapterMarker_Exists_VisibleButLocked()
        {
            var tohoku = MapScreen(BuildTree()).Q<Button>("chapter-node-tohoku");
            Assert.IsNotNull(tohoku, "Expected a 'chapter-node-tohoku' marker.");
            Assert.IsTrue(tohoku.ClassListContains("chapter-node--locked"), "Tohoku (Chapter 1) must start locked.");
        }

        [Test]
        public void ChapterPanel_ExposesCtaAndVideoElements()
        {
            var screen = MapScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("chapter-panel-cta"));
            Assert.IsNotNull(screen.Q<Label>("chapter-panel-cta-text"));
            Assert.IsNotNull(screen.Q<VisualElement>("chapter-panel-video"));
            Assert.IsNotNull(screen.Q<Label>("chapter-panel-video-fallback"));
        }

        [Test]
        public void ChapterPanel_IsAnOverlay_NeverAPermanentlyReservedColumn()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".detail-panel {");
            Assert.IsNotNull(block, "Expected a shared '.detail-panel' overlay rule in Map.uss.");
            StringAssert.Contains("position: absolute", block);
            StringAssert.DoesNotContain("width: 38.3%", uss, "The old permanently-reserved 38.3% column must not return.");
        }

        [Test]
        public void MapStage_FillsRemainingSpace_NoFixedArtboard()
        {
            string uss = File.ReadAllText(UssPath);
            string stageBlock = ExtractRuleBlock(uss, ".pan-stage {");
            Assert.IsNotNull(stageBlock);
            StringAssert.Contains("flex-grow: 1", stageBlock);
            StringAssert.DoesNotContain("aspect-ratio: 1.0972", uss, "The old fixed 790:720 artboard must not return.");
        }

        [Test]
        public void TopBar_HasAWayBackToTheMainMenu()
        {
            var screen = MapScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("go-menu"), "Map must expose a 'go-menu' navigator back to the Main Menu.");
        }

        [Test]
        public void TopBar_ExposesSettingsMapTechniquesStatsAndLevelXp()
        {
            var screen = MapScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("map-topbar-settings"), "Top bar must expose a Settings action.");
            Assert.IsNotNull(screen.Q<Button>("map-topbar-map"), "Top bar must expose a Map action.");
            Assert.IsNotNull(screen.Q<Button>("map-topbar-techniques"), "Top bar must expose a Techniques action.");
            Assert.IsNotNull(screen.Q<Button>("map-topbar-stats"), "Top bar must expose a Stats action.");

            var level = screen.Q<Label>("map-topbar-level");
            var xp = screen.Q<Label>("map-topbar-xp");
            Assert.IsNotNull(level, "Top bar must expose a Level display.");
            Assert.IsNotNull(xp, "Top bar must expose an XP display.");
            Assert.IsNotEmpty(level.text);
            Assert.IsNotEmpty(xp.text);
        }

        [Test]
        public void SettingsModal_ExistsAndIsClosedByDefault()
        {
            var modal = MapScreen(BuildTree()).Q<VisualElement>("map-settings-modal");
            Assert.IsNotNull(modal, "Expected a map-local 'map-settings-modal' overlay.");
            Assert.IsFalse(modal.ClassListContains("map-settings-modal--open"));
            Assert.IsNotNull(modal.Q<Slider>("map-settings-music"));
            Assert.IsNotNull(modal.Q<Slider>("map-settings-sfx"));
            Assert.IsNotNull(modal.Q<Slider>("map-settings-trainer"));
            Assert.IsNotNull(modal.Q<Button>("map-settings-close"));
        }

        // No baked labels, the old flattened single-image composition, the
        // permanent Okinawa side panel, or the previous fixed 61.7/38.3
        // artboard composition remains on the rebuilt Japan world map.
        [Test]
        public void NoLegacyMvpMapElements_Remain()
        {
            var screen = MapScreen(BuildTree());
            Assert.IsNull(screen.Q<VisualElement>("map-art"), "The old flattened '.map-art' image must not remain.");
            Assert.IsNull(screen.Q<VisualElement>("map-node-okinawa"), "The old single transparent Okinawa hotspot must not remain.");
            Assert.IsNull(screen.Q<VisualElement>("map-detail"), "The old permanent Okinawa detail panel must not remain.");
            Assert.IsNull(screen.Q<VisualElement>("map-artboard"), "The old aspect-locked artboard must not remain.");

            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            StringAssert.DoesNotContain("japan_route_map_mvp", uss, "Map.uss must no longer reference the retired flattened MVP art.");
            StringAssert.DoesNotContain(".map-art ", uss);
            StringAssert.DoesNotContain(".map-art{", uss);
            StringAssert.DoesNotContain(".map-node-okinawa", uss);
            StringAssert.DoesNotContain(".map-artboard", uss);
            StringAssert.DoesNotContain(".map-detail {", uss, "The old permanently-reserved detail column must not remain.");
        }

        /// <summary>Body of the first USS rule whose header matches <paramref name="header"/> (e.g. ".pan-stage {"), or null.</summary>
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
    }
}
