using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for the shared top-nav visual system now used by all four HUD
    /// screens (Map, Okinawa, Techniques, Profile) — one shared stylesheet
    /// (Map.uss, linked into each screen; see the per-screen class checks below),
    /// the visible label rename Stats -> Profile, per-screen active state
    /// (restrained true-red text glow + a small layered red brushstroke-style
    /// underline — see Underline_IsARedLayeredStrokeApproximation_NotTheBlockedImageTint
    /// for why this is no longer the tinted Main-Menu image), generous nav
    /// spacing, and a Settings entry point on every screen wired into the one
    /// shared modal. Lives alongside
    /// MapHudRedesignTests.cs since both read Assets/UI/Map/Map.uss as the single
    /// source of truth for this shared system. Profile-region-specific content
    /// (identity/radar/journey) is covered by Mikey.UI.Profile.Tests instead.
    /// </summary>
    public class SharedTopBarRedesignTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string SettingsControllerPath = "Assets/UI/Settings/SettingsModalController.cs";

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

        // ---------- 2-5: Menu / Map / Techniques / Profile present everywhere, "Stats" gone ----------

        [TestCase("map")]
        [TestCase("mapOkinawa")]
        [TestCase("techniques")]
        [TestCase("profile")]
        public void EveryHudScreen_HasAllFourNavLabels_AndProfileNotStats(string screenId)
        {
            var screen = Screen(BuildTree(), screenId);
            var topBar = screen.Q<VisualElement>(className: "map-topbar");
            Assert.IsNotNull(topBar, $"'{screenId}' must carry the shared top HUD.");

            var texts = topBar.Query<Label>(className: "map-topbar__nav-btn-text").ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(texts, "Menu", $"'{screenId}' HUD must show Menu.");
            CollectionAssert.Contains(texts, "Map", $"'{screenId}' HUD must show Map.");
            CollectionAssert.Contains(texts, "Techniques", $"'{screenId}' HUD must show Techniques.");
            CollectionAssert.Contains(texts, "Profile", $"'{screenId}' HUD must show Profile.");
            CollectionAssert.DoesNotContain(texts, "Stats", $"'{screenId}' HUD must not show the retired 'Stats' label.");
        }

        // ---------- 6+9-12: one shared active-state system, correct per screen ----------

        [Test]
        public void ActiveNavClass_IsOneSharedRule_NotFourUnrelatedCopies()
        {
            string uss = File.ReadAllText(UssPath);
            Assert.AreEqual(1, CountOccurrences(uss, "\n.map-topbar__nav-btn--active .map-topbar__nav-btn-text {"),
                "Expected exactly one shared active-state rule, reused by every screen.");
        }

        [TestCase("map", "Map")]
        [TestCase("mapOkinawa", "Map")]
        [TestCase("techniques", "Techniques")]
        [TestCase("profile", "Profile")]
        public void EveryHudScreen_MarksExactlyOneItemActive_MatchingItsOwnSection(string screenId, string expectedActiveLabel)
        {
            var screen = Screen(BuildTree(), screenId);
            var topBar = screen.Q<VisualElement>(className: "map-topbar");
            var activeItems = topBar.Query<VisualElement>(className: "map-topbar__nav-btn--active").ToList();
            Assert.AreEqual(1, activeItems.Count, $"'{screenId}' HUD must mark exactly one nav item active.");

            var activeText = activeItems[0].Q<Label>(className: "map-topbar__nav-btn-text").text;
            Assert.AreEqual(expectedActiveLabel, activeText, $"'{screenId}' HUD's active item must be '{expectedActiveLabel}'.");
        }

        // ---------- 7: restrained red glow on the active label ----------

        [Test]
        public void ActiveNavText_HasARestrainedRedGlow()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar__nav-btn--active .map-topbar__nav-btn-text {");
            Assert.IsNotNull(block, "Expected the active-label glow rule.");
            StringAssert.Contains("text-shadow", block, "Expected a text-shadow glow, not an extra bubble/overlay element.");

            var blur = Regex.Match(block, @"text-shadow:\s*[\d.\-]+(?:px)?\s+[\d.\-]+(?:px)?\s+(\d+(\.\d+)?)px");
            Assert.IsTrue(blur.Success, "Expected a blurred text-shadow (offset offset blur color).");
            float blurPx = float.Parse(blur.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(blurPx, 6f);
            Assert.LessOrEqual(blurPx, 24f, "Glow blur radius should stay restrained, not read as a huge neon halo.");
        }

        // ---------- 8: brushstroke underline, reusing the existing asset ----------

        [TestCase("map")]
        [TestCase("mapOkinawa")]
        [TestCase("techniques")]
        [TestCase("profile")]
        public void EveryHudScreen_ActiveItemCarriesABrushstrokeUnderline(string screenId)
        {
            var active = Screen(BuildTree(), screenId).Q<VisualElement>(className: "map-topbar__nav-btn--active");
            Assert.IsNotNull(active);
            Assert.IsNotNull(active.Q<VisualElement>(className: "map-topbar__nav-btn-underline"),
                $"'{screenId}' active nav item must carry a brushstroke underline.");
        }

        // NOT the tinted Main-Menu brushstroke image anymore: that asset's ink is
        // near-black RGB behind an alpha mask, and "-unity-background-image-tint-
        // color" is a per-pixel MULTIPLY, so multiplying black by any tint color
        // still yields black — it visually read as a black underline no matter what
        // color it was "tinted." Rebuilt as a small dedicated layered VisualElement
        // (3 solid-red, slightly rotated strokes) per the fallback the spec itself
        // allows for exactly this case.
        [Test]
        public void Underline_IsARedLayeredStrokeApproximation_NotTheBlockedImageTint()
        {
            string uss = File.ReadAllText(UssPath);
            StringAssert.DoesNotContain("menu_brushstroke.png", ExtractRuleBlock(uss, "\n.map-topbar__nav-btn-underline {") ?? string.Empty,
                "The underline wrapper must no longer reference the brushstroke image asset.");

            string strokeBlock = ExtractRuleBlock(uss, "\n.map-topbar__nav-btn-underline__stroke {");
            Assert.IsNotNull(strokeBlock, "Expected a shared '.map-topbar__nav-btn-underline__stroke' rule.");
            StringAssert.Contains("#C62828", strokeBlock, "Stroke color must be the true-red brand accent, not orange.");
            StringAssert.DoesNotContain("-unity-background-image-tint-color", strokeBlock,
                "Must be a solid color, not another attempt at tinting a black source image.");

            foreach (var suffix in new[] { "a", "b", "c" })
                Assert.IsNotNull(ExtractRuleBlock(uss, $"\n.map-topbar__nav-btn-underline__stroke--{suffix} {{"),
                    $"Expected a '.map-topbar__nav-btn-underline__stroke--{suffix}' layer for an irregular, painted look.");
        }

        [TestCase("map")]
        [TestCase("mapOkinawa")]
        [TestCase("techniques")]
        [TestCase("profile")]
        public void EveryHudScreen_ActiveUnderline_HasThreeRedStrokeLayers(string screenId)
        {
            var active = Screen(BuildTree(), screenId).Q<VisualElement>(className: "map-topbar__nav-btn--active");
            var underline = active.Q<VisualElement>(className: "map-topbar__nav-btn-underline");
            var strokes = underline.Query<VisualElement>(className: "map-topbar__nav-btn-underline__stroke").ToList();
            Assert.AreEqual(3, strokes.Count, $"'{screenId}' underline must carry all three stroke layers.");
        }

        // ---------- 13+14: LVL / XP on every screen ----------

        [TestCase("map")]
        [TestCase("mapOkinawa")]
        [TestCase("techniques")]
        [TestCase("profile")]
        public void EveryHudScreen_HasLevelAndXpDisplay(string screenId)
        {
            var topBar = Screen(BuildTree(), screenId).Q<VisualElement>(className: "map-topbar");
            Assert.IsNotNull(topBar.Q<Label>(className: "map-topbar__level"));
            Assert.IsNotNull(topBar.Q<Label>(className: "map-topbar__xp"));
            Assert.IsNotNull(topBar.Q<VisualElement>(className: "map-topbar__xp-bar"), $"'{screenId}' HUD must show a thin XP progress line.");
            Assert.IsNotNull(topBar.Q<VisualElement>(className: "map-topbar__xp-bar__fill"));
        }

        [Test]
        public void LevelText_IsOffWhite_WithARestrainedRedAccent_NotOrange()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar__level {");
            Assert.IsNotNull(block);
            StringAssert.Contains("var(--bone)", block, "LVL text itself must be off-white, not colored orange/red.");
            StringAssert.DoesNotContain("var(--seal)", block, "Must not use the old vermilion/orange-leaning accent.");
            StringAssert.DoesNotContain("var(--ember)", block, "Must not use the orange accent.");
            StringAssert.Contains("text-shadow", block, "Expected a restrained red glow as the accent, not solid-colored text.");
        }

        // ---------- 15: Settings still opens the one shared modal, everywhere ----------

        [TestCase("map", "map-topbar-settings")]
        [TestCase("mapOkinawa", "okinawa-topbar-settings")]
        [TestCase("techniques", "techniques-topbar-settings")]
        [TestCase("profile", "profile-topbar-settings")]
        public void EveryHudScreen_SettingsButtonExists_AndIsWiredIntoTheSharedModal(string screenId, string settingsButtonName)
        {
            var screen = Screen(BuildTree(), screenId);
            Assert.IsNotNull(screen.Q<Button>(settingsButtonName), $"'{screenId}' must expose '{settingsButtonName}'.");

            string controllerSource = File.ReadAllText(SettingsControllerPath);
            StringAssert.Contains($"\"{settingsButtonName}\"", controllerSource,
                $"SettingsModalController must list '{settingsButtonName}' in OpenButtonNames.");
        }

        [Test]
        public void SettingsIcon_UsesTheSuppliedAsset_NotThePlaceholderRingAndDotGlyph()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar__settings-icon {");
            Assert.IsNotNull(block);
            StringAssert.Contains("Media/Images/settings_icon.png", block, "Must use the supplied settings_icon.png asset.");

            StringAssert.DoesNotContain(".map-topbar__settings-ring", uss, "The old placeholder ring glyph rule must be removed.");
            StringAssert.DoesNotContain(".map-topbar__settings-dot", uss, "The old placeholder dot glyph rule must be removed.");

            foreach (var screenId in new[] { "map", "mapOkinawa", "techniques", "profile" })
            {
                var screen = Screen(BuildTree(), screenId);
                Assert.IsNull(screen.Q<VisualElement>(className: "map-topbar__settings-ring"),
                    $"'{screenId}' must not still render the old ring glyph markup.");
                Assert.IsNull(screen.Q<VisualElement>(className: "map-topbar__settings-dot"),
                    $"'{screenId}' must not still render the old dot glyph markup.");
            }
        }

        // ---------- premium spacing, not a compressed toolbar ----------

        [Test]
        public void NavItemSpacing_IsSubstantiallyIncreased()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar__nav-btn {");
            Assert.IsNotNull(block);
            var margin = Regex.Match(block, @"margin-right:\s*(\d+(\.\d+)?)px");
            Assert.IsTrue(margin.Success);
            float px = float.Parse(margin.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(px, 44f, "Nav items must feel like spaced-out premium navigation, not a compressed toolbar.");
        }
    }
}
