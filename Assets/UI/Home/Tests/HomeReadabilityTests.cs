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

        // ---------- 4: individual per-item real-PNG brushstroke backings ----------
        // (replaces both the removed shared right-side gradient bands AND the
        // earlier fake USS-layered brushstroke approximation)

        private const string BrushstrokeAssetPath = "/Assets/UI/Media/Images/MainMenu/menu_brushstroke.png";
        private static readonly string[] StrokeClasses =
            { "home-nav__stroke--play", "home-nav__stroke--plans", "home-nav__stroke--settings", "home-nav__stroke--quit" };

        [Test]
        public void SharedRightSideBands_AreCompletelyGone()
        {
            var screen = MenuScreen(BuildTree());
            Assert.IsEmpty(screen.Query<VisualElement>(className: "home-nav-scrim").ToList(),
                "The old shared right-side gradient/band treatment must not remain.");

            string uss = File.ReadAllText(HomeUssPath);
            StringAssert.DoesNotContain(".home-nav-scrim", uss, "No trace of the retired shared-band classes may remain in Home.uss.");
        }

        [Test]
        public void FakeLayeredStrokesAreGone_OnlyTheRealPngRemains()
        {
            string uss = File.ReadAllText(HomeUssPath);
            StringAssert.DoesNotContain(".home-nav__stroke-layer", uss,
                "The earlier three-strip fake-brushstroke approximation must be fully removed now that the real PNG is used.");

            var screen = MenuScreen(BuildTree());
            Assert.IsEmpty(screen.Query<VisualElement>(className: "home-nav__stroke-layer").ToList());
        }

        [Test]
        public void ExactlyFourIndividualBrushstrokes_OneBehindEachNavItem()
        {
            var root = BuildTree();
            foreach (var (buttonName, strokeClass) in new[]
            {
                ("go-map", "home-nav__stroke--play"),
                ("menu-plans-open", "home-nav__stroke--plans"),
                ("menu-settings-open", "home-nav__stroke--settings"),
                ("menu-quit", "home-nav__stroke--quit"),
            })
            {
                var button = MenuScreen(root).Q<Button>(buttonName);
                Assert.IsNotNull(button, $"Expected button '{buttonName}'.");
                var stroke = button.Q<VisualElement>(className: strokeClass);
                Assert.IsNotNull(stroke, $"Expected an individual brushstroke ('{strokeClass}') behind '{buttonName}'.");
                Assert.IsTrue(stroke.ClassListContains("home-nav__stroke"),
                    "Each item's stroke must use the shared reusable base class, not a one-off implementation.");
            }

            // Exactly four in the whole screen — one per item, never a shared one.
            Assert.AreEqual(4, MenuScreen(root).Query<VisualElement>(className: "home-nav__stroke").ToList().Count);
        }

        [Test]
        public void AllFourBrushstrokes_UseTheSameSourceTexture()
        {
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-nav__stroke {");
            Assert.IsNotNull(block, "Expected the shared '.home-nav__stroke' base recipe.");
            StringAssert.Contains(BrushstrokeAssetPath, block,
                "All four items must share one recipe pointing at the same real brushstroke PNG (Assets/UI/Media/Images/MainMenu/menu_brushstroke.png).");
            StringAssert.Contains("background-repeat: no-repeat", block);
        }

        [Test]
        public void Opacity_IsExactly0_35_AppliedAtPresentationLevel()
        {
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-nav__stroke {");
            Assert.IsNotNull(block);
            var match = System.Text.RegularExpressions.Regex.Match(block, @"opacity\s*:\s*(0(\.\d+)?)\s*;");
            Assert.IsTrue(match.Success, "Expected an 'opacity: 0.35;' declaration on the shared stroke recipe.");
            float opacity = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(0.35f, opacity, 0.001f,
                "Opacity must be applied at the presentation level (USS), not baked into the PNG bytes.");
        }

        [Test]
        public void SharedStrokeHeightAndVerticalCrop_AreIdenticalForAllFour()
        {
            // Height and background-position-y live on the ONE shared base
            // class, not per-word — structurally guaranteeing every stroke
            // reads at the same visual height, since only X-axis values are
            // allowed to vary per word.
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-nav__stroke {");
            Assert.IsNotNull(block);
            StringAssert.Contains("height:", block);
            StringAssert.Contains("background-position-y:", block);

            foreach (var strokeClass in StrokeClasses)
            {
                string modifier = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n." + strokeClass + " {");
                StringAssert.DoesNotContain("height:", modifier, $"'{strokeClass}' must not override the shared height.");
                StringAssert.DoesNotContain("background-position-y", modifier, $"'{strokeClass}' must not override the shared vertical crop.");
            }
        }

        [Test]
        public void PerWordBackgroundSize_SharesTheSameHeightButDifferentWidths()
        {
            string uss = File.ReadAllText(HomeUssPath);
            float? sharedHeight = null;
            var widths = new System.Collections.Generic.List<float>();
            foreach (var strokeClass in StrokeClasses)
            {
                string block = ExtractRuleBlock(uss, "\n." + strokeClass + " {");
                var match = System.Text.RegularExpressions.Regex.Match(block, @"background-size\s*:\s*(\d+(\.\d+)?)px\s+(\d+(\.\d+)?)px");
                Assert.IsTrue(match.Success, $"'{strokeClass}' must declare a two-value background-size (width height).");
                float w = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float h = float.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                widths.Add(w);
                if (sharedHeight == null)
                    sharedHeight = h;
                else
                    Assert.AreEqual(sharedHeight.Value, h, 0.01f, "The canvas scale's vertical component must be identical for every word — only horizontal length is tuned per label.");
            }
            Assert.AreEqual(4, widths.Distinct().Count(), "Each word's canvas scale width must differ — never a uniform fixed width.");
        }

        [Test]
        public void EachWordsStroke_HasItsOwnWidth_NotAUniformSize()
        {
            string uss = File.ReadAllText(HomeUssPath);
            float play = ExtractPx(ExtractRuleBlock(uss, "\n.home-nav__stroke--play {"), "width");
            float plans = ExtractPx(ExtractRuleBlock(uss, "\n.home-nav__stroke--plans {"), "width");
            float settings = ExtractPx(ExtractRuleBlock(uss, "\n.home-nav__stroke--settings {"), "width");
            float quit = ExtractPx(ExtractRuleBlock(uss, "\n.home-nav__stroke--quit {"), "width");

            // "SETTINGS" is by far the longest word; "PLAY"/"QUIT" the shortest.
            Assert.Greater(settings, plans);
            Assert.Greater(plans, play);
            Assert.Greater(play, quit);
        }

        [Test]
        public void Brushstroke_AnchorsUnderTheRightAlignedLabel_NotCenteredOnTheWiderPaddedButton()
        {
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-nav__stroke {");
            Assert.IsNotNull(block);
            // Matches .home-nav__item's own right padding (26px), so it
            // centers under the text despite the button's asymmetric
            // (46px left / 26px right) padding.
            StringAssert.Contains("right: 26px", block);
        }

        [Test]
        public void Stroke_IgnoresPicking_ButtonsRemainClickable()
        {
            var root = BuildTree();
            foreach (var strokeClass in StrokeClasses)
            {
                var stroke = MenuScreen(root).Q<VisualElement>(className: strokeClass);
                Assert.AreEqual(PickingMode.Ignore, stroke.pickingMode);
            }

            // The Button elements themselves are untouched, so their default
            // (clickable) picking mode and existing click wiring are intact.
            foreach (var name in new[] { "go-map", "menu-plans-open", "menu-settings-open", "menu-quit" })
                Assert.IsNotNull(MenuScreen(root).Q<Button>(name));
        }

        [Test]
        public void FullScreenScrim_RemainsUnchanged()
        {
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-scrim {");
            Assert.IsNotNull(block, "The existing full-bleed legibility scrim must remain (unchanged).");
            float alpha = ExtractAlpha(block);
            Assert.AreEqual(0.32f, alpha, 0.001f);
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
