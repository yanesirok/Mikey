using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Contract for the Main Menu readability correction: a larger logo, a
    /// soft right-side dark "gradient" (layered bands, since USS has no
    /// linear-gradient support) behind the nav rather than a flat full-screen
    /// overlay or per-item bubbles, and mobile-target typography/spacing —
    /// all presentation-only (Home.uss / MikeyApp.uxml), no controller
    /// changes, no behavior change to Play/Plans/Settings/Quit.
    /// </summary>
    public class HomeReadabilityTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string HomeUssPath = "Assets/UI/Home/Home.uss";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static VisualElement MenuScreen(VisualElement root)
        {
            var screen = root.Q<VisualElement>("menu");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'menu'.");
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

        private static float ExtractPx(string block, string property)
        {
            Assert.IsNotNull(block, "Expected a non-null rule block.");
            var match = System.Text.RegularExpressions.Regex.Match(block, property + @"\s*:\s*(-?\d+(\.\d+)?)px");
            Assert.IsTrue(match.Success, $"Expected a '{property}: <n>px' declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------- 1: Play/Plans/Settings/Quit still present ----------

        [Test]
        public void PlayPlansSettingsQuit_AllStillPresent()
        {
            var screen = MenuScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Button>("go-map"), "PLAY must still exist.");
            Assert.IsNotNull(screen.Q<Button>("menu-plans-open"), "PLANS must still exist.");
            Assert.IsNotNull(screen.Q<Button>("menu-settings-open"), "SETTINGS must still exist.");
            Assert.IsNotNull(screen.Q<Button>("menu-quit"), "QUIT must still exist.");
        }

        [Test]
        public void NoLabelTextWasRenamed_ThisPassIsPresentationOnly()
        {
            var screen = MenuScreen(BuildTree());
            Assert.AreEqual("PLAY", screen.Q<Button>("go-map").Q<Label>(className: "home-nav__label")?.text);
            Assert.AreEqual("PLANS", screen.Q<Button>("menu-plans-open").Q<Label>(className: "home-nav__label")?.text);
            Assert.AreEqual("SETTINGS", screen.Q<Button>("menu-settings-open").Q<Label>(className: "home-nav__label")?.text);
            Assert.AreEqual("QUIT", screen.Q<Button>("menu-quit").Q<Label>(className: "home-nav__label")?.text);
        }

        // ---------- 2: Settings still targets the shared implementation ----------

        [Test]
        public void Settings_StillHasNoLocalModal_TargetsTheSharedOneInstead()
        {
            var screen = MenuScreen(BuildTree());
            Assert.IsNull(screen.Q<VisualElement>("menu-settings-modal"),
                "Main Menu must not regain a local Settings overlay — it opens the one shared modal (Assets/UI/Settings).");

            var root = BuildTree();
            Assert.IsNotNull(root.Q<VisualElement>("shared-settings-modal"),
                "The one shared Settings modal must still exist.");
        }

        // ---------- 3: enlarged logo ----------

        [Test]
        public void Logo_IsEnlargedByApproximately50Percent_137To175Px()
        {
            // 104px -> 156px is exactly +50%; allow a little slack either side.
            float width = ExtractPx(ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-logo {"), "width");
            float height = ExtractPx(ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-logo {"), "height");
            Assert.AreEqual(width, height, 0.01f, "Must stay square — the source image's own aspect ratio is preserved by scale-to-fit.");
            Assert.GreaterOrEqual(width, 137f, "Expected roughly +50% over the previous 104px.");
            Assert.LessOrEqual(width, 175f);
        }

        [Test]
        public void Logo_KeepsUpperLeftPlacement_InsideSafeArea_UsingTheExistingAsset()
        {
            var screen = MenuScreen(BuildTree());
            var logo = screen.Q<VisualElement>(className: "home-logo");
            Assert.IsNotNull(logo);

            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-logo {");
            StringAssert.Contains("position: absolute", block);
            StringAssert.Contains("mikey_logo.png", block, "Must keep using the existing supplied logo asset, not a new/replacement image.");
            StringAssert.Contains("-unity-background-scale-mode: scale-to-fit", block, "Must preserve aspect ratio.");
        }

        // ---------- 4: right-side readability treatment ----------

        [Test]
        public void RightSideGradientBands_Exist()
        {
            var screen = MenuScreen(BuildTree());
            var bands = screen.Query<VisualElement>(className: "home-nav-scrim").ToList();
            Assert.AreEqual(3, bands.Count, "Expected three layered bands approximating a soft right-side gradient.");
        }

        [Test]
        public void GradientBands_AreRightAnchored_NarrowingAndDarkeningTowardTheNav()
        {
            string uss = File.ReadAllText(HomeUssPath);
            string b1 = ExtractRuleBlock(uss, "\n.home-nav-scrim--1 {");
            string b2 = ExtractRuleBlock(uss, "\n.home-nav-scrim--2 {");
            string b3 = ExtractRuleBlock(uss, "\n.home-nav-scrim--3 {");
            Assert.IsNotNull(b1);
            Assert.IsNotNull(b2);
            Assert.IsNotNull(b3);

            string baseBlock = ExtractRuleBlock(uss, "\n.home-nav-scrim {");
            StringAssert.Contains("right: 0", baseBlock, "Bands must anchor to the right edge, behind the nav.");

            float width1 = ExtractPercent(b1, "width");
            float width2 = ExtractPercent(b2, "width");
            float width3 = ExtractPercent(b3, "width");
            Assert.Greater(width1, width2, "Each band nearer the nav must be narrower — that's what reads as a soft gradient.");
            Assert.Greater(width2, width3);

            float alpha1 = ExtractAlpha(b1);
            float alpha2 = ExtractAlpha(b2);
            float alpha3 = ExtractAlpha(b3);
            Assert.Less(alpha1, alpha2, "Each narrower/closer band must be darker — darkest right behind the menu text.");
            Assert.Less(alpha2, alpha3);
            Assert.Less(alpha3, 0.30f, "Even the darkest band must stay subtle/understated — never a strong opaque rectangle.");
        }

        [Test]
        public void GradientBands_IgnorePicking_PurelyVisual()
        {
            var screen = MenuScreen(BuildTree());
            foreach (var band in screen.Query<VisualElement>(className: "home-nav-scrim").ToList())
                Assert.AreEqual(PickingMode.Ignore, band.pickingMode);
        }

        [Test]
        public void FullScreenScrim_StaysSubtle_TheGradientIsAdditionalNotAReplacementForAnOpaqueOverlay()
        {
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-scrim {");
            Assert.IsNotNull(block, "The existing full-bleed legibility scrim must remain (unchanged).");
            float alpha = ExtractAlpha(block);
            Assert.Less(alpha, 0.40f, "The whole-video scrim must stay understated — the readability fix is the right-side gradient, not a darker blanket overlay.");
        }

        // ---------- 5: no per-item bubbles ----------

        [Test]
        public void NavItems_StillHaveNoBubbleBackgroundOrPillShape()
        {
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-nav__item {");
            Assert.IsNotNull(block);
            StringAssert.Contains("background-color: transparent", block);
            StringAssert.Contains("border-radius: 0", block);
        }

        // ---------- 6: upgraded mobile typography/spacing/touch sizing ----------

        [Test]
        public void NavLabelFontSize_IsWithinMobileTarget_30To38Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-nav__label {"), "font-size");
            Assert.GreaterOrEqual(size, 30f);
            Assert.LessOrEqual(size, 38f);
        }

        [Test]
        public void NavItemSpacing_WasIncreased_ForComfortableReadability()
        {
            // Was 6px; bumped for more breathing room between choices.
            float margin = ExtractPx(ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-nav__item {"), "margin");
            Assert.GreaterOrEqual(margin, 10f);
        }

        [Test]
        public void NavItems_KeepAtLeast48pxTouchTargets()
        {
            var screen = MenuScreen(BuildTree());
            foreach (var name in new[] { "go-map", "menu-plans-open", "menu-settings-open", "menu-quit" })
            {
                var button = screen.Q<Button>(name);
                Assert.IsTrue(button.ClassListContains("tap-target-lg"), $"'{name}' must keep its >= 56px touch target class.");
            }

            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.tap-target-lg {");
            Assert.IsNotNull(block);
            float minWidth = ExtractPx(block, "min-width");
            float minHeight = ExtractPx(block, "min-height");
            Assert.GreaterOrEqual(minWidth, 48f);
            Assert.GreaterOrEqual(minHeight, 48f);
        }

        // ---------- 7: background video unchanged ----------

        [Test]
        public void BackgroundVideoElement_StillPresent_Unchanged()
        {
            var screen = MenuScreen(BuildTree());
            var media = screen.Q<VisualElement>("home-bg-media");
            Assert.IsNotNull(media, "The existing 'home-bg-media' target (bound by BackgroundMediaController to main_menu_loop.mp4) must remain.");
            Assert.IsTrue(media.ClassListContains("bg-media"));
        }

        private static float ExtractPercent(string block, string property)
        {
            var match = System.Text.RegularExpressions.Regex.Match(block, property + @"\s*:\s*(-?\d+(\.\d+)?)%");
            Assert.IsTrue(match.Success, $"Expected a '{property}: <n>%' declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static float ExtractAlpha(string block)
        {
            var match = System.Text.RegularExpressions.Regex.Match(block, @"rgba\(\s*\d+,\s*\d+,\s*\d+,\s*(\d+(\.\d+)?)\s*\)");
            Assert.IsTrue(match.Success, $"Expected an rgba(...) background-color declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
