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
    /// UI Toolkit LVL 0-8 markers (Okinawa's mission set — see
    /// MapMarkerLayout.Missions), no auto-selected level/open popup on
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
        public void AllNineLevelMarkers_Exist()
        {
            var screen = OkinawaScreen(BuildTree());
            for (int i = 0; i < 9; i++)
                Assert.IsNotNull(screen.Q<Button>($"level-node-{i}"), $"Expected a 'level-node-{i}' marker.");
        }

        [Test]
        public void Level0Marker_IsUnlockedByDefault()
        {
            var node = OkinawaScreen(BuildTree()).Q<Button>("level-node-0");
            Assert.IsFalse(node.ClassListContains("level-node--locked"), "LVL 0 must always be unlocked.");
        }

        [Test]
        public void Levels1Through8_AreLockedByDefault()
        {
            var screen = OkinawaScreen(BuildTree());
            for (int i = 1; i <= 8; i++)
            {
                var node = screen.Q<Button>($"level-node-{i}");
                Assert.IsTrue(node.ClassListContains("level-node--locked"),
                    $"LVL {i} must start locked (LVL 1 unlocks via progression, LVL 2-8 have no gameplay yet).");
            }
        }

        [Test]
        public void DefaultState_NoLevelSelected_PopupClosed()
        {
            var screen = OkinawaScreen(BuildTree());

            var panel = screen.Q<VisualElement>("level-panel");
            Assert.IsNotNull(panel, "Expected a 'level-panel' overlay.");
            Assert.IsFalse(panel.ClassListContains("scroll-panel--open"), "Entering Okinawa must never auto-select a level.");
            Assert.IsFalse(panel.ClassListContains("scroll-panel--locked"), "The locked look is applied per level when the scroll opens, never baked into the markup.");

            for (int i = 0; i < 9; i++)
                Assert.IsFalse(screen.Q<Button>($"level-node-{i}").ClassListContains("level-node--selected"));
        }

        [Test]
        public void LevelPanel_ExposesCta()
        {
            var screen = OkinawaScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("level-panel-cta"));
            Assert.IsNotNull(screen.Q<Label>("level-panel-cta-text"));
        }

        /// <summary>
        /// The popup is a scroll now, not the shared right-side
        /// ".detail-panel" slide. Sharing that class was the whole hazard the
        /// redesign had to remove: restyling it here would have silently
        /// restyled the Japan chapter panel, which still uses it.
        /// </summary>
        [Test]
        public void LevelPanel_NoLongerSharesTheJapanChapterPanelStyle()
        {
            var panel = OkinawaScreen(BuildTree()).Q<VisualElement>("level-panel");

            Assert.IsTrue(panel.ClassListContains("scroll-panel"));
            foreach (string shared in panel.GetClasses())
            {
                Assert.IsFalse(shared.StartsWith("detail-panel"),
                    $"'level-panel' still carries \"{shared}\" -- the Japan chapter panel's style must not reach the Okinawa scroll.");
            }
        }

        /// <summary>
        /// Locked levels get a cord across the paper instead of a call to
        /// action, and unlocked ones a brushstroke CTA. Both live in the
        /// markup and are switched by ONE class in USS, so the controller
        /// never touches style.display -- these ids/classes existing is what
        /// makes that possible.
        /// </summary>
        [Test]
        public void LevelPanel_CarriesBothLockedAndUnlockedFurniture()
        {
            var screen = OkinawaScreen(BuildTree());

            Assert.IsNotNull(screen.Q<VisualElement>("level-panel-backdrop"), "Expected the dim backdrop -- it is also the tap-outside close surface.");
            Assert.IsNotNull(screen.Q<VisualElement>("level-panel-paper"), "Expected the paper -- it is the element whose height unrolls.");
            Assert.IsNotNull(screen.Q<Label>("level-panel-requirement"), "Expected the locked requirement line.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "scroll-panel__cord"), "Expected the locked cord.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "scroll-panel__roll--top"), "Expected the top roll.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "scroll-panel__roll--bottom"), "Expected the bottom roll.");
        }

        /// <summary>
        /// The scroll covers the whole screen, so a tap on the paper that fell
        /// through to the backdrop behind it would close the popup the player
        /// just opened. The paper must therefore stop picking itself.
        /// </summary>
        [Test]
        public void LevelPanel_PaperStopsTapsFromReachingTheBackdrop()
        {
            var paper = OkinawaScreen(BuildTree()).Q<VisualElement>("level-panel-paper");
            Assert.AreEqual(PickingMode.Position, paper.pickingMode);
        }

        /// <summary>
        /// Paint order is the whole reason the dim works. UI Toolkit paints —
        /// and hit-tests in reverse — document order, so the scroll must be
        /// declared AFTER the HUD: otherwise the bar stays at full brightness
        /// on top of a 0.9 backdrop and, worse, stays tappable, letting the
        /// player navigate away through a bar the modal is supposed to be
        /// covering. Map.uss's ".map-topbar" note carries the rule (HUD above
        /// panels, below modals); this test is what stops the block drifting
        /// back above it, which would look like nothing more than a
        /// reordered chunk of markup in review.
        /// </summary>
        [Test]
        public void LevelPanel_IsDeclaredAfterTheHud_SoTheDimCoversIt()
        {
            var screen = OkinawaScreen(BuildTree());
            var panel = screen.Q<VisualElement>("level-panel");
            var topbar = screen.Query<VisualElement>(className: "map-topbar").First();
            Assert.IsNotNull(topbar, "Expected the Okinawa screen's shared top HUD.");

            var siblings = panel.parent.Children().ToList();
            int panelIndex = siblings.IndexOf(panel);
            int topbarIndex = siblings.IndexOf(topbar);

            Assert.Greater(topbarIndex, -1, "The HUD must be a sibling of 'level-panel' for their paint order to be comparable.");
            Assert.Greater(panelIndex, topbarIndex,
                "'level-panel' must be declared after the '.map-topbar' block so the scroll's dim covers the HUD and blocks taps on it while the popup is open.");
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
        public void TopBar_HasNoMainMenuEntry_TheMapIsTheHub()
        {
            var screen = OkinawaScreen(BuildTree());
            Assert.IsNull(screen.Q<Button>("go-menu"),
                "The Main Menu is retired - 'menu' is the Sign In gate now, and nothing navigates back to it.");
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
