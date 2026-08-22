using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Structural contract for the Sign In screen (still the "menu" screen id, so
    /// BackgroundMediaController / AudioController / IShellPreloader keep working
    /// untouched) in MikeyApp.uxml: the supplied cinematic video background, the
    /// upper-left Mikey logo, a bottom-centered row of exactly two round,
    /// icon-only account actions (Google + guest), one inline status line, and
    /// none of the retired PLAY / VOW / SETTINGS / QUIT menu.
    /// </summary>
    public class HomeScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string HomeUssPath = "Assets/UI/Home/Home.uss";
        private const string TitleUssPath = "Assets/UI/Title/Title.uss";
        private const string LogoAssetPath = "/Assets/UI/Media/Images/mikey_logo.png";
        private const string GoogleIconPath = "/Assets/UI/Media/Images/MainMenu/google_icon.png";
        private const string GuestIconPath = "/Assets/UI/Media/Images/MainMenu/guest_icon.png";
        private const string NavPrefix = "go-";

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
            Assert.IsTrue(screen.ClassListContains("screen"), "'menu' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        /// <summary>Body of the first USS rule whose header matches <paramref name="header"/> (e.g. "\n.home-auth__btn {"), or null.</summary>
        private static string ExtractRuleBlock(string uss, string header)
        {
            int start = uss.IndexOf(header, System.StringComparison.Ordinal);
            if (start < 0)
                return null;
            int open = start + header.Length;
            int close = uss.IndexOf('}', open);
            return close < 0 ? null : uss.Substring(open, close - open);
        }

        private static float ExtractPx(string block, string property)
        {
            var match = System.Text.RegularExpressions.Regex.Match(block, property + @"\s*:\s*(-?\d+(\.\d+)?)px");
            Assert.IsTrue(match.Success, $"Expected a '{property}: <n>px' declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static float ExtractAlpha(string block)
        {
            var match = System.Text.RegularExpressions.Regex.Match(block, @"rgba\(\s*\d+,\s*\d+,\s*\d+,\s*(\d+(\.\d+)?)\s*\)");
            Assert.IsTrue(match.Success, $"Expected an rgba(...) background-color declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------- screen shell (unchanged by the Sign In rebuild) ----------

        [Test]
        public void Menu_ExistsAsExactlyOneScreen()
        {
            var root = BuildTree();
            int menus = root.Query<VisualElement>(className: "screen").ToList().Count(s => s.name == "menu");
            Assert.AreEqual(1, menus, "There must be exactly one screen named 'menu'.");
        }

        [Test]
        public void Menu_HasExactlyOneSafeAreaContent()
        {
            var screen = MenuScreen(BuildTree());
            int count = screen.Query<VisualElement>(className: "safe-area-content").ToList().Count;
            Assert.AreEqual(1, count, $"'menu' must contain exactly one .safe-area-content (found {count}).");
        }

        [Test]
        public void MenuBackground_IsFullBleed_OutsideSafeAreaContent()
        {
            var screen = MenuScreen(BuildTree());
            var bg = screen.Q<VisualElement>(className: "home-bg");
            Assert.IsNotNull(bg, "Expected a .home-bg full-bleed background layer.");
            Assert.IsNull(NearestSafeAreaAncestor(bg),
                ".home-bg must not be a descendant of .safe-area-content (it must bleed full-screen).");
        }

        [Test]
        public void BackgroundVideoElement_StillPresent_Unchanged()
        {
            var screen = MenuScreen(BuildTree());
            var media = screen.Q<VisualElement>("home-bg-media");
            Assert.IsNotNull(media, "The existing 'home-bg-media' target (bound by BackgroundMediaController to main_menu_loop.mp4) must remain.");
            Assert.IsTrue(media.ClassListContains("bg-media"));
        }

        [Test]
        public void FullScreenScrim_RemainsUnchanged()
        {
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-scrim {");
            Assert.IsNotNull(block, "The existing full-bleed legibility scrim must remain (unchanged).");
            Assert.AreEqual(0.32f, ExtractAlpha(block), 0.001f);
        }

        [Test]
        public void MikeyLogo_IsReferencedOnSignIn_ButNoLongerOnTitle()
        {
            Assert.IsTrue(File.Exists(TitleUssPath), $"Expected stylesheet at {TitleUssPath}.");
            Assert.IsTrue(File.Exists(HomeUssPath), $"Expected stylesheet at {HomeUssPath}.");
            StringAssert.Contains(LogoAssetPath, File.ReadAllText(HomeUssPath),
                "Home.uss must reference the supplied Mikey logo asset on the Sign In screen.");
            // Title.uss must not reference the static image at all: the video
            // itself is the logo during playback, and TitleController freezes on
            // that SAME video's own final frame while waiting on the shell (see
            // TitleControllerSourceTests).
            StringAssert.DoesNotContain(LogoAssetPath, File.ReadAllText(TitleUssPath),
                "Title.uss must no longer reference the static Mikey logo image — the final logo_intro.mp4 animation (including its own final frame) replaces it.");
        }

        [Test]
        public void SignInLogo_ExistsInUpperLeft_InsideSafeArea_UsingTheExistingAsset()
        {
            var screen = MenuScreen(BuildTree());
            var logo = screen.Q<VisualElement>(className: "home-logo");
            Assert.IsNotNull(logo, "Sign In must show the Mikey logo mark.");
            Assert.IsNotNull(NearestSafeAreaAncestor(logo), ".home-logo must respect the safe area.");

            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-logo {");
            StringAssert.Contains("position: absolute", block);
            StringAssert.Contains("mikey_logo.png", block, "Must keep using the existing supplied logo asset, not a new/replacement image.");
            StringAssert.Contains("-unity-background-scale-mode: scale-to-fit", block, "Must preserve aspect ratio.");
            Assert.AreEqual(ExtractPx(block, "width"), ExtractPx(block, "height"), 0.01f,
                "Must stay square — the source image's own aspect ratio is preserved by scale-to-fit.");
        }

        // ---------- the account row ----------

        [Test]
        public void AccountRow_HasExactlyTwoActions_GoogleAndGuest()
        {
            var screen = MenuScreen(BuildTree());
            var row = screen.Q<VisualElement>(className: "home-auth__row");
            Assert.IsNotNull(row, "Sign In must expose a .home-auth__row account row.");

            var buttons = row.Query<Button>().ToList();
            Assert.AreEqual(2, buttons.Count, "The account row carries exactly two actions: Google and guest.");
            Assert.IsNotNull(row.Q<Button>("menu-google-signin"), "Expected the Google sign-in action.");
            Assert.IsNotNull(row.Q<Button>("menu-guest-continue"), "Expected the guest continue action.");
            Assert.IsNotNull(NearestSafeAreaAncestor(row), "The account row must respect the safe area.");
        }

        [Test]
        public void BothActions_AreIconOnly_NeverWorded()
        {
            var screen = MenuScreen(BuildTree());
            foreach (var name in new[] { "menu-google-signin", "menu-guest-continue" })
            {
                var button = screen.Q<Button>(name);
                Assert.IsNotNull(button, $"Expected the '{name}' action.");
                Assert.IsEmpty(button.Query<Label>().ToList(),
                    $"'{name}' must be icon-only — the mark carries the meaning, not a worded label.");
                Assert.IsTrue(string.IsNullOrEmpty(button.text),
                    $"'{name}' must not carry button text either.");
                Assert.IsNotNull(button.Q<VisualElement>(className: "home-auth__icon"),
                    $"'{name}' must contain a .home-auth__icon.");
            }
        }

        [Test]
        public void BothActions_AreLocalActions_NotScreenNavigators()
        {
            // Where they lead depends on progression state, so HomeController owns
            // them — ScreenManager's 'go-<id>' auto-wiring cannot express that.
            var screen = MenuScreen(BuildTree());
            foreach (var name in new[] { "menu-google-signin", "menu-guest-continue" })
                Assert.IsFalse(name.StartsWith(NavPrefix), $"'{name}' must not be a 'go-' navigator.");

            Assert.IsEmpty(screen.Query<VisualElement>().ToList()
                    .Where(e => !string.IsNullOrEmpty(e.name) && e.name.StartsWith(NavPrefix)).ToList(),
                "Sign In must contain no 'go-' navigators at all.");
        }

        [Test]
        public void BothActions_UseLargeTouchTargets_AndTheSameRoundButtonKit()
        {
            var screen = MenuScreen(BuildTree());
            foreach (var name in new[] { "menu-google-signin", "menu-guest-continue" })
            {
                var button = screen.Q<Button>(name);
                Assert.IsTrue(button.ClassListContains("tap-target-lg"),
                    $"Action '{name}' must use the >=56px .tap-target-lg touch-target class.");
                Assert.IsTrue(button.ClassListContains("home-auth__btn"),
                    $"Action '{name}' must share the same round button kit (.home-auth__btn).");
            }

            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), "\n.home-auth__btn {");
            Assert.IsNotNull(block, "Expected a '.home-auth__btn' rule in Home.uss.");
            float width = ExtractPx(block, "width");
            Assert.AreEqual(width, ExtractPx(block, "height"), 0.01f, "The account buttons must be round, so square-sized.");
            Assert.GreaterOrEqual(width, 56f, "Both actions must clear the 56px touch target on their own, not only via the shared class.");
            Assert.AreEqual(width / 2f, ExtractPx(block, "border-radius"), 0.01f, "border-radius must be half the size — a circle, not a squircle.");
        }

        [Test]
        public void GuestIsQuieterButNeverSmallerOrHidden()
        {
            var screen = MenuScreen(BuildTree());
            var guest = screen.Q<Button>("menu-guest-continue");
            Assert.IsTrue(guest.ClassListContains("home-auth__btn--guest"),
                "Guest must carry its own lowest-priority modifier class.");

            string uss = File.ReadAllText(HomeUssPath);
            string block = ExtractRuleBlock(uss, "\n.home-auth__btn--guest {");
            Assert.IsNotNull(block, "Expected a '.home-auth__btn--guest' rule in Home.uss.");
            StringAssert.DoesNotContain("display: none", block, "Guest must never be hidden.");
            StringAssert.DoesNotContain("width", block, "Guest must never be smaller — it differs only in ring emphasis.");
        }

        [Test]
        public void IconsUseTheirOwnSuppliedAssets()
        {
            string uss = File.ReadAllText(HomeUssPath);
            StringAssert.Contains(GoogleIconPath, uss, "The Google action must use the Google mark asset.");
            StringAssert.Contains(GuestIconPath, uss, "The guest action must use the guest glyph asset.");
            Assert.IsTrue(File.Exists("Assets" + GoogleIconPath.Substring("/Assets".Length)), $"Expected the Google mark at {GoogleIconPath}.");
            Assert.IsTrue(File.Exists("Assets" + GuestIconPath.Substring("/Assets".Length)), $"Expected the guest glyph at {GuestIconPath}.");
        }

        [Test]
        public void StatusLine_ExistsAndIsEmptyByDefault()
        {
            var screen = MenuScreen(BuildTree());
            var status = screen.Q<Label>("menu-auth-status");
            Assert.IsNotNull(status, "Sign In must expose a single inline status line.");
            Assert.IsTrue(string.IsNullOrEmpty(status.text),
                "The status line must start empty — nothing is claimed until there is something honest to say.");
        }

        [Test]
        public void SignIn_ShowsNoTextAtAllUntilTheStatusLineSpeaks()
        {
            var screen = MenuScreen(BuildTree());
            foreach (var label in screen.Query<Label>().ToList())
                Assert.IsTrue(string.IsNullOrEmpty(label.text),
                    $"Sign In must carry no worded UI — found the label '{label.text}'.");
        }

        // ---------- the retired Main Menu ----------

        [Test]
        public void RetiredMainMenu_IsGone()
        {
            var screen = MenuScreen(BuildTree());

            foreach (var name in new[] { "menu-vow-open", "menu-vow-close", "menu-vow-modal", "menu-settings-open", "menu-quit", "go-map" })
                Assert.IsNull(screen.Q<VisualElement>(name), $"Retired Main Menu element '{name}' must be gone.");

            foreach (var className in new[] { "home-nav", "home-nav__item", "home-nav__label", "home-nav__stroke", "vow-modal" })
                Assert.IsEmpty(screen.Query<VisualElement>(className: className).ToList(),
                    $"Retired Main Menu layer '.{className}' must be gone.");
        }

        [Test]
        public void RetiredMainMenuStyles_AreGoneFromTheStylesheet()
        {
            string uss = File.ReadAllText(HomeUssPath);
            foreach (var rule in new[] { ".home-nav", ".home-nav__item", ".home-nav__label", ".home-nav__stroke", ".vow-modal", ".vow-option" })
                StringAssert.DoesNotContain(rule + " {", uss,
                    $"'{rule}' styles the retired Main Menu and must not linger in Home.uss.");
        }

        [Test]
        public void OldHomeDashboard_IsStillGone()
        {
            var screen = MenuScreen(BuildTree());

            foreach (var name in new[]
            {
                "home-cta", "home-nav-map", "home-nav-techniques", "home-devbar", "nav-home",
                "home-dev-reset", "home-dev-new-player", "home-dev-combine-started",
                "home-dev-level1-unlocked", "home-dev-lesson-started", "home-dev-lesson-completed",
            })
            {
                Assert.IsNull(screen.Q<VisualElement>(name), $"Old Home dashboard element '{name}' must be gone.");
            }

            foreach (var className in new[]
            {
                "home-ribbon", "home-belt", "home-stats", "home-chip", "home-hero", "home-ring",
                "home-power", "home-dock", "home-devbar", "home-tab", "home-hero__card",
            })
            {
                Assert.IsEmpty(screen.Query<VisualElement>(className: className).ToList(),
                    $"Old Home dashboard layer '.{className}' must be gone.");
            }
        }
    }
}
