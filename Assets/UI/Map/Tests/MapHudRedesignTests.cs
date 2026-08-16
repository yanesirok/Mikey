using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for the mobile HUD redesign shared by the Japan world map and
    /// Okinawa chapter map: a translucent dark-glass top bar (never an opaque
    /// bubble-button strip) at an upgraded mobile size, upgraded chapter/level
    /// panel and CTA typography, and a substantially larger Settings modal —
    /// all pure USS/UXML, no controller changes. Structural checks use
    /// CloneTree + Query; sizing/removed-styling checks read Map.uss as raw
    /// text (mirrors this project's established source-assertion technique
    /// for values UI Toolkit doesn't expose any other way in EditMode).
    /// </summary>
    public class MapHudRedesignTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static VisualElement Screen(VisualElement root, string id)
        {
            var screen = root.Q<VisualElement>(id);
            Assert.IsNotNull(screen, $"MikeyApp.uxml must contain a screen named '{id}'.");
            return screen;
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

        // ---------- 1: Japan and Okinawa share the same top-bar system ----------

        [Test]
        public void JapanAndOkinawa_ShareTheSameTopBarClasses()
        {
            var root = BuildTree();
            var japanNavButtons = Screen(root, "map").Query<Button>(className: "map-topbar__nav-btn").ToList();
            var okinawaNavButtons = Screen(root, "mapOkinawa").Query<Button>(className: "map-topbar__nav-btn").ToList();
            Assert.AreEqual(4, japanNavButtons.Count, "Japan: Menu, Map, Techniques, Stats.");
            Assert.AreEqual(4, okinawaNavButtons.Count, "Okinawa: Menu, Map, Techniques, Stats.");

            // One shared rule in Map.uss drives both screens' bars — not a
            // duplicated per-screen stylesheet.
            string uss = File.ReadAllText(UssPath);
            Assert.AreEqual(1, CountOccurrences(uss, "\n.map-topbar {"));
        }

        // ---------- 2: Menu / Map / Techniques / Stats remain present ----------

        [TestCase("map", "go-menu")]
        [TestCase("map", "map-topbar-map")]
        [TestCase("map", "map-topbar-techniques")]
        [TestCase("map", "map-topbar-stats")]
        [TestCase("map", "map-topbar-settings")]
        [TestCase("mapOkinawa", "go-menu")]
        [TestCase("mapOkinawa", "okinawa-topbar-map")]
        [TestCase("mapOkinawa", "okinawa-topbar-techniques")]
        [TestCase("mapOkinawa", "okinawa-topbar-stats")]
        [TestCase("mapOkinawa", "okinawa-topbar-settings")]
        public void AllFiveNavActions_StillExist(string screenId, string buttonName)
        {
            var screen = Screen(BuildTree(), screenId);
            Assert.IsNotNull(screen.Q<Button>(buttonName), $"Expected '{buttonName}' on screen '{screenId}'.");
        }

        [Test]
        public void MapNavButton_CarriesTheActiveStateAccent_OnBothScreens()
        {
            var root = BuildTree();
            Assert.IsTrue(Screen(root, "map").Q<Button>("map-topbar-map").ClassListContains("map-topbar__nav-btn--active"));
            Assert.IsTrue(Screen(root, "mapOkinawa").Q<Button>("okinawa-topbar-map").ClassListContains("map-topbar__nav-btn--active"));
        }

        // ---------- 3: old individual pill/bubble styling is removed ----------

        [Test]
        public void OldBubbleButtonStyling_IsGone()
        {
            string uss = File.ReadAllText(UssPath);
            StringAssert.DoesNotContain(".map-topbar__icon-btn", uss, "The old circular icon-bubble class must not remain.");
            StringAssert.DoesNotContain(".map-topbar__icon-glyph", uss, "The old icon-bubble glyph class must not remain.");
            StringAssert.DoesNotContain(".map-topbar__left", uss, "The old left-cluster wrapper class must not remain.");

            string navBtnBlock = ExtractRuleBlock(uss, "\n.map-topbar__nav-btn {");
            Assert.IsNotNull(navBtnBlock, "Expected a '.map-topbar__nav-btn' rule.");
            StringAssert.DoesNotContain("border-radius: 24px", navBtnBlock, "Nav items must not be pill-shaped.");
            StringAssert.Contains("background-color: rgba(0, 0, 0, 0)", navBtnBlock, "Nav items must not carry an opaque/bubble background.");
        }

        [Test]
        public void TopBarBackground_IsATranslucentTint_NotAnOpaqueBlackBar()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar {");
            Assert.IsNotNull(block);
            // Darkened further for reliable readability over the brighter parts of
            // the Profile background art (0.55-0.62 -> 0.72-0.78) — still a
            // translucent glass tint, never opaque.
            StringAssert.IsMatch(@"background-color:\s*rgba\(\d+,\s*\d+,\s*\d+,\s*0\.7[2-8]\)", block,
                "Expected an rgba(...) tint with alpha roughly 0.72-0.78, not an opaque background.");
        }

        // ---------- 4: top bar height upgraded ----------

        [Test]
        public void TopBarHeight_IsWithinUpgradedMobileTarget_78To90Px()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar {");
            Assert.IsNotNull(block);
            float height = ExtractPx(block, "height");
            Assert.GreaterOrEqual(height, 78f);
            Assert.LessOrEqual(height, 90f);
        }

        // ---------- 5: main nav typography upgraded ----------

        [Test]
        public void NavTextSize_IsWithinUpgradedMobileTarget_24To32Px()
        {
            // Bumped ~10-15% for on-device readability (24-28 -> 24-32).
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar__nav-btn-text {");
            Assert.IsNotNull(block);
            float size = ExtractPx(block, "font-size");
            Assert.GreaterOrEqual(size, 24f);
            Assert.LessOrEqual(size, 32f);
        }

        [Test]
        public void LevelTextSize_IsWithinUpgradedMobileTarget_24To30Px()
        {
            // LVL now sizes independently from XP (24-30 vs XP's 20-25) since the
            // typography pass grew them by different amounts.
            float levelSize = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.map-topbar__level {"), "font-size");
            Assert.GreaterOrEqual(levelSize, 24f);
            Assert.LessOrEqual(levelSize, 30f);
        }

        [Test]
        public void XpTextSize_IsWithinUpgradedMobileTarget_20To25Px()
        {
            float xpSize = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.map-topbar__xp {"), "font-size");
            Assert.GreaterOrEqual(xpSize, 20f);
            Assert.LessOrEqual(xpSize, 25f);
        }

        // ---------- 6: Settings touch target ----------

        [Test]
        public void SettingsIcon_IsWithinUpgradedVisualSizeTarget_26To32Px()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar__settings-icon {");
            Assert.IsNotNull(block);
            float size = ExtractPx(block, "width");
            Assert.GreaterOrEqual(size, 26f);
            Assert.LessOrEqual(size, 32f);
        }

        [Test]
        public void SettingsButton_KeepsAtLeast48pxTouchTarget()
        {
            var root = BuildTree();
            var settingsButton = Screen(root, "map").Q<Button>("map-topbar-settings");
            Assert.IsTrue(settingsButton.ClassListContains("tap-target"),
                "Settings button must keep the >= 48x48 '.tap-target' touch-target class.");
        }

        // ---------- 7 + 8: chapter/level panel typography (shared .detail-panel classes) ----------

        [Test]
        public void PanelTitle_IsWithinUpgradedTarget_34To42Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.detail-panel__title {"), "font-size");
            Assert.GreaterOrEqual(size, 34f);
            Assert.LessOrEqual(size, 42f);
        }

        [Test]
        public void PanelEyebrowAndSubtitle_AreWithinUpgradedTarget_20To24Px()
        {
            string uss = File.ReadAllText(UssPath);
            float eyebrow = ExtractPx(ExtractRuleBlock(uss, "\n.detail-panel__eyebrow {"), "font-size");
            float subtitle = ExtractPx(ExtractRuleBlock(uss, "\n.detail-panel__subtitle {"), "font-size");
            Assert.GreaterOrEqual(eyebrow, 20f);
            Assert.LessOrEqual(eyebrow, 24f);
            Assert.GreaterOrEqual(subtitle, 20f);
            Assert.LessOrEqual(subtitle, 24f);
        }

        [Test]
        public void PanelDescription_IsWithinUpgradedTarget_19To22Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.detail-panel__desc {"), "font-size");
            Assert.GreaterOrEqual(size, 19f);
            Assert.LessOrEqual(size, 22f);
        }

        [Test]
        public void PanelMetadata_IsWithinUpgradedTarget_17To20Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.detail-panel__meta {"), "font-size");
            Assert.GreaterOrEqual(size, 17f);
            Assert.LessOrEqual(size, 20f);
        }

        [Test]
        public void Panel_RemainsARightSideOverlay_NotFullscreen()
        {
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), "\n.detail-panel {");
            Assert.IsNotNull(block);
            StringAssert.Contains("position: absolute", block);
            StringAssert.DoesNotContain("width: 100%", block, "The panel must never consume the entire screen.");
        }

        // ---------- 9: CTA sizing upgraded ----------

        [Test]
        public void CtaHeight_IsWithinUpgradedTarget_52To62Px()
        {
            float height = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.detail-panel__cta {"), "height");
            Assert.GreaterOrEqual(height, 52f, "Minimum approximately 52px.");
            Assert.LessOrEqual(height, 62f, "Preferably 54-62px on landscape mobile.");
        }

        [Test]
        public void CtaTextSize_IsWithinUpgradedTarget_22To26Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.detail-panel__cta-text {"), "font-size");
            Assert.GreaterOrEqual(size, 22f);
            Assert.LessOrEqual(size, 26f);
        }

        // Settings modal content/sizing tests (items 10-13 from the original
        // HUD-redesign pass) moved to Mikey.UI.Settings.Tests.SettingsModalUxmlTests
        // — Settings is no longer part of Map.uss/MikeyApp.uxml's per-screen
        // markup at all; it's the one shared modal (see
        // Assets/UI/Settings and SettingsModalController).

        private static float ExtractPx(string block, string property)
        {
            Assert.IsNotNull(block, "Expected a non-null rule block.");
            var match = System.Text.RegularExpressions.Regex.Match(block, property + @"\s*:\s*(-?\d+(\.\d+)?)px");
            Assert.IsTrue(match.Success, $"Expected a '{property}: <n>px' declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) != -1)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }
    }
}
