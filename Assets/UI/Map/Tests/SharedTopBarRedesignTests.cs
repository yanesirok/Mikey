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
    /// (restrained red text glow + a small painted brushstroke underline reusing
    /// the existing Main-Menu asset), generous nav spacing, and a Settings entry
    /// point on every screen wired into the one shared modal. Lives alongside
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

        [Test]
        public void Underline_IsRedTinted_AndReusesTheExistingBrushstrokeAsset_NoNewArt()
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, "\n.map-topbar__nav-btn-underline {");
            Assert.IsNotNull(block);
            StringAssert.Contains("menu_brushstroke.png", block, "Must reuse the existing Main-Menu brushstroke asset, not new art.");
            StringAssert.Contains("-unity-background-image-tint-color", block, "Must be red-tinted at render time (the source asset is black).");
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
