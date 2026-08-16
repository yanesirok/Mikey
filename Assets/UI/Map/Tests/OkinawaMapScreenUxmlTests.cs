using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Structural contract for the Okinawa chapter map screen ("mapOkinawa")
    /// in MikeyApp.uxml: full-bleed pannable okinawa_map_final.jpg with real
    /// UI Toolkit LVL 0-5 markers, no auto-selected level/open popup on
    /// entry, and the same top quick-access bar as the Japan world map.
    /// </summary>
    public class OkinawaMapScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string OkinawaMapJpgPath = "Assets/UI/Media/Images/okinawa_map_final.jpg";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static VisualElement OkinawaScreen(VisualElement root)
        {
            var screen = root.Q<VisualElement>("mapOkinawa");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'mapOkinawa'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'mapOkinawa' must carry the .screen class.");
            return screen;
        }

        [Test]
        public void MapOkinawa_ExistsAsExactlyOneScreen()
        {
            int count = BuildTree().Query<VisualElement>(className: "screen").ToList().Count(s => s.name == "mapOkinawa");
            Assert.AreEqual(1, count, "There must be exactly one screen named 'mapOkinawa'.");
        }

        [Test]
        public void OkinawaMapJpg_ExistsOnDisk_AndIsReferencedByOkinawaCanvasArt()
        {
            Assert.IsTrue(File.Exists(OkinawaMapJpgPath), $"Expected the final Okinawa map art at {OkinawaMapJpgPath}.");
            Assert.IsTrue(File.Exists(OkinawaMapJpgPath + ".meta"), "Okinawa map JPG must have an imported .meta file.");

            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".okinawa-canvas-art {");
            Assert.IsNotNull(block, "Expected an '.okinawa-canvas-art' rule in Map.uss.");
            StringAssert.Contains("okinawa_map_final.jpg", block);
        }

        [Test]
        public void AllSixLevelMarkers_Exist()
        {
            var screen = OkinawaScreen(BuildTree());
            for (int i = 0; i < 6; i++)
                Assert.IsNotNull(screen.Q<Button>($"level-node-{i}"), $"Expected a 'level-node-{i}' marker.");
        }

        [Test]
        public void Level0Marker_IsUnlockedByDefault()
        {
            var node = OkinawaScreen(BuildTree()).Q<Button>("level-node-0");
            Assert.IsFalse(node.ClassListContains("level-node--locked"), "LVL 0 must always be unlocked.");
        }

        [Test]
        public void Levels1Through5_AreLockedByDefault()
        {
            var screen = OkinawaScreen(BuildTree());
            for (int i = 1; i <= 5; i++)
            {
                var node = screen.Q<Button>($"level-node-{i}");
                Assert.IsTrue(node.ClassListContains("level-node--locked"),
                    $"LVL {i} must start locked (LVL 1 unlocks via progression, LVL 2-5 have no gameplay yet).");
            }
        }

        [Test]
        public void DefaultState_NoLevelSelected_PopupClosed()
        {
            var screen = OkinawaScreen(BuildTree());

            var panel = screen.Q<VisualElement>("level-panel");
            Assert.IsNotNull(panel, "Expected a 'level-panel' overlay.");
            Assert.IsFalse(panel.ClassListContains("detail-panel--open"), "Entering Okinawa must never auto-select a level.");

            for (int i = 0; i < 6; i++)
                Assert.IsFalse(screen.Q<Button>($"level-node-{i}").ClassListContains("level-node--selected"));
        }

        [Test]
        public void LevelPanel_ExposesCta()
        {
            var screen = OkinawaScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("level-panel-cta"));
            Assert.IsNotNull(screen.Q<Label>("level-panel-cta-text"));
        }

        [Test]
        public void MapButton_ReturnsToJapanWorldMap()
        {
            var screen = OkinawaScreen(BuildTree());
            // Custom-wired (OkinawaMapController), not a plain "go-" navigator —
            // it must reset the session map context to Japan before navigating
            // (see MapNavigationState), so it can't rely on ScreenManager's
            // generic click wiring alone.
            var mapButton = screen.Q<Button>("okinawa-topbar-map");
            Assert.IsNotNull(mapButton, "Okinawa's top bar Map button must exist as 'okinawa-topbar-map'.");

            var root = BuildTree();
            Assert.IsTrue(root.Q<VisualElement>("map").ClassListContains("screen"),
                "The Japan world map 'okinawa-topbar-map' returns to must exist.");
        }

        [Test]
        public void TopBar_HasAWayBackToTheMainMenu()
        {
            var screen = OkinawaScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("go-menu"), "Okinawa must expose a 'go-menu' navigator back to the Main Menu.");
        }

        [Test]
        public void TopBar_ExposesSettingsTechniquesStatsAndLevelXp()
        {
            var screen = OkinawaScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("okinawa-topbar-settings"), "Top bar must expose a Settings action.");
            Assert.IsNotNull(screen.Q<Button>("okinawa-topbar-techniques"), "Top bar must expose a Techniques action.");
            Assert.IsNotNull(screen.Q<Button>("okinawa-topbar-stats"), "Top bar must expose a Stats action.");

            var level = screen.Q<Label>("okinawa-topbar-level");
            var xp = screen.Q<Label>("okinawa-topbar-xp");
            Assert.IsNotNull(level, "Top bar must expose a Level display.");
            Assert.IsNotNull(xp, "Top bar must expose an XP display.");
            Assert.IsNotEmpty(level.text);
            Assert.IsNotEmpty(xp.text);
        }

        [Test]
        public void NoLocalSettingsModal_TheSettingsButtonOpensTheOneSharedModalInstead()
        {
            // Settings is unified into a single shared modal (see
            // Assets/UI/Settings and Mikey.UI.Settings.Tests) — the Okinawa
            // screen must not have its own copy anymore, only the open
            // trigger button (already covered by
            // TopBar_ExposesSettingsTechniquesStatsAndLevelXp above).
            var screen = OkinawaScreen(BuildTree());
            Assert.IsNull(screen.Q<VisualElement>("okinawa-settings-modal"),
                "The old map-local Settings overlay must be gone.");
        }

        [Test]
        public void TransitionOverlay_StartsOpaque_ForTheIncomingInkFade()
        {
            var overlay = OkinawaScreen(BuildTree()).Q<VisualElement>("okinawa-transition-overlay");
            Assert.IsNotNull(overlay, "Expected an 'okinawa-transition-overlay' element.");
            Assert.IsTrue(overlay.ClassListContains("map-transition-overlay--visible"),
                "Okinawa's overlay must start visible so OkinawaMapController can fade it out on entry, completing the Japan screen's fade-in.");
        }

        /// <summary>Body of the first USS rule whose header matches <paramref name="header"/>, or null.</summary>
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
