using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Structural contract for the rebuilt Main Menu (the "menu" screen) in
    /// MikeyApp.uxml: the supplied cinematic video background, the upper-left
    /// Mikey logo, a right-side PLAY/PLANS/SETTINGS/QUIT navigation built from
    /// spacing and typography rather than dashboard cards, the local Plans
    /// overlay (hidden by default, driven by HomeController — SETTINGS now
    /// opens the one shared Settings modal instead, see
    /// Mikey.UI.Settings.Tests), and none of the old Home dashboard controls
    /// (CTA, ribbon, power stats, 4-tab dock, dev bar).
    /// </summary>
    public class HomeScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string HomeUssPath = "Assets/UI/Home/Home.uss";
        private const string TitleUssPath = "Assets/UI/Title/Title.uss";
        private const string LogoAssetPath = "/Assets/UI/Media/Images/mikey_logo.png";
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

        /// <summary>Body of the first USS rule whose header matches <paramref name="header"/> (e.g. ".menu-modal {"), or null.</summary>
        private static string ExtractRuleBlock(string uss, string header)
        {
            int start = uss.IndexOf(header, System.StringComparison.Ordinal);
            if (start < 0)
                return null;
            int open = start + header.Length;
            int close = uss.IndexOf('}', open);
            return close < 0 ? null : uss.Substring(open, close - open);
        }

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
        public void MikeyLogo_IsReferencedOnTitleAndMainMenu()
        {
            Assert.IsTrue(File.Exists(TitleUssPath), $"Expected stylesheet at {TitleUssPath}.");
            Assert.IsTrue(File.Exists(HomeUssPath), $"Expected stylesheet at {HomeUssPath}.");
            StringAssert.Contains(LogoAssetPath, File.ReadAllText(TitleUssPath),
                "Title.uss must reference the supplied Mikey logo asset.");
            StringAssert.Contains(LogoAssetPath, File.ReadAllText(HomeUssPath),
                "Home.uss must reference the supplied Mikey logo asset on the Main Menu.");
        }

        [Test]
        public void MainMenuLogo_ExistsInUpperLeft_InsideSafeArea()
        {
            var screen = MenuScreen(BuildTree());
            var logo = screen.Q<VisualElement>(className: "home-logo");
            Assert.IsNotNull(logo, "Main Menu must show the Mikey logo mark.");
            Assert.IsNotNull(NearestSafeAreaAncestor(logo), ".home-logo must respect the safe area.");
        }

        [Test]
        public void Play_ExistsAsGoMapNavigator_AndMapScreenExists()
        {
            var root = BuildTree();
            var screen = MenuScreen(root);

            var play = screen.Q<Button>("go-map");
            Assert.IsNotNull(play, "Main Menu must expose PLAY as a 'go-map' Button.");
            Assert.AreEqual("PLAY", play.Q<Label>(className: "home-nav__label")?.text);

            var map = root.Q<VisualElement>("map");
            Assert.IsNotNull(map, "'go-map' must target an existing 'map' screen.");
            Assert.IsTrue(map.ClassListContains("screen"), "'map' target must be a screen.");
        }

        [Test]
        public void Plans_OpensLocalOverlay_WithComingSoonContent()
        {
            var screen = MenuScreen(BuildTree());

            var plansButton = screen.Q<Button>("menu-plans-open");
            Assert.IsNotNull(plansButton, "Main Menu must expose PLANS.");
            Assert.IsFalse(plansButton.name.StartsWith(NavPrefix),
                "PLANS must not be a 'go-' navigator — it opens a local overlay, the menu itself doesn't change screens.");

            var modal = screen.Q<VisualElement>("menu-plans-modal");
            Assert.IsNotNull(modal, "Expected a 'menu-plans-modal' overlay.");
            Assert.AreEqual("PLANS", modal.Q<Label>(className: "menu-modal__title")?.text);
            Assert.AreEqual("Coming soon.", modal.Q<Label>(className: "menu-modal__body")?.text);
            Assert.IsNotNull(modal.Q<Button>("menu-plans-close"), "Plans overlay must expose a close action.");
        }

        [Test]
        public void Settings_ExistsAsButton_ButNoLongerOpensALocalOverlay()
        {
            // SETTINGS now opens the one shared Settings modal (see
            // Assets/UI/Settings — Mikey.UI.Settings.Tests covers its content,
            // sizing and behavior in full); Home no longer owns a Settings
            // modal of its own.
            var screen = MenuScreen(BuildTree());

            var settingsButton = screen.Q<Button>("menu-settings-open");
            Assert.IsNotNull(settingsButton, "Main Menu must expose SETTINGS.");
            Assert.IsFalse(settingsButton.name.StartsWith(NavPrefix),
                "SETTINGS must not be a 'go-' navigator — it opens the shared Settings modal.");

            Assert.IsNull(screen.Q<VisualElement>("menu-settings-modal"),
                "The old local Settings overlay must be gone — Settings is unified into one shared modal.");
        }

        [Test]
        public void Quit_ExistsAsLocalAction_NeverPlatformHidden()
        {
            var screen = MenuScreen(BuildTree());
            var quit = screen.Q<Button>("menu-quit");
            Assert.IsNotNull(quit, "Main Menu must expose QUIT — Mikey is mobile-first (Android) and QUIT is never platform-hidden.");
            Assert.IsFalse(quit.name.StartsWith(NavPrefix), "QUIT must not be a 'go-' navigator.");
            Assert.AreEqual("QUIT", quit.Q<Label>(className: "home-nav__label")?.text);
        }

        [Test]
        public void QuitAction_IsVisuallyLowestPriority()
        {
            var screen = MenuScreen(BuildTree());
            var quit = screen.Q<Button>("menu-quit");
            Assert.IsNotNull(quit, "Expected the 'menu-quit' action.");
            Assert.IsTrue(quit.ClassListContains("home-nav__item--quit"),
                "QUIT must carry its own lowest-priority modifier class, distinct from PLAY/PLANS/SETTINGS.");
        }

        [Test]
        public void AllFourNavActions_UseLargeTouchTargetClass_AndSameBaseTypographyKit()
        {
            var screen = MenuScreen(BuildTree());
            foreach (var name in new[] { "go-map", "menu-plans-open", "menu-settings-open", "menu-quit" })
            {
                var button = screen.Q<Button>(name);
                Assert.IsNotNull(button, $"Expected a nav action named '{name}'.");
                Assert.IsTrue(button.ClassListContains("tap-target-lg"),
                    $"Nav action '{name}' must use the >=56px .tap-target-lg touch-target class.");
                Assert.IsTrue(button.ClassListContains("home-nav__item"),
                    $"Nav action '{name}' must share the same premium typography kit (.home-nav__item).");
            }
        }

        [Test]
        public void NavActions_AreNotWrappedInCardContainers()
        {
            // Spacing/typography, not dashboard cards — the old .home-tab /
            // .home-hero__card rounded-card treatments must not return.
            var screen = MenuScreen(BuildTree());
            Assert.IsEmpty(screen.Query<VisualElement>(className: "home-tab").ToList());
            Assert.IsEmpty(screen.Query<VisualElement>(className: "home-hero__card").ToList());
        }

        [Test]
        public void Modals_AreHiddenByDefault_InStylesheet()
        {
            Assert.IsTrue(File.Exists(HomeUssPath), $"Expected stylesheet at {HomeUssPath}.");
            string block = ExtractRuleBlock(File.ReadAllText(HomeUssPath), ".menu-modal {");
            Assert.IsNotNull(block, "Expected a '.menu-modal' rule in Home.uss.");
            StringAssert.Contains("display: none", block,
                "'.menu-modal' must default to hidden (HomeController toggles it to Flex on open).");
        }

        [Test]
        public void OldHomeDashboard_IsGone()
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
                "home-power", "home-dock", "home-devbar",
            })
            {
                Assert.IsEmpty(screen.Query<VisualElement>(className: className).ToList(),
                    $"Old Home dashboard layer '.{className}' must be gone.");
            }

            var labels = screen.Query<Label>().ToList().Select(l => l.text).ToList();
            foreach (var staleText in new[]
            {
                "START LVL 0", "White Belt", "LEVEL 1", "1-day streak", "$10.00",
                "STR", "SPD", "AGI", "END", "DEV · PROGRESSION",
            })
            {
                CollectionAssert.DoesNotContain(labels, staleText,
                    $"Old Home dashboard text '{staleText}' must not appear on the rebuilt Main Menu.");
            }
        }

        [Test]
        public void NoActiveButton_LacksAnAction()
        {
            var screen = MenuScreen(BuildTree());
            // Every Button on the rebuilt Main Menu (including inside its overlays)
            // must be a wired "go-" navigator or a known local action — either
            // HomeController's own (Plans/Quit) or the shared
            // SettingsModalController's ("menu-settings-open"; its close button
            // now lives outside this screen, in the shared modal).
            var localActions = new HashSet<string>
            {
                "menu-plans-open", "menu-plans-close", "menu-settings-open", "menu-quit",
            };

            foreach (var button in screen.Query<Button>().ToList())
            {
                bool isNavigator = !string.IsNullOrEmpty(button.name) && button.name.StartsWith(NavPrefix);
                bool isLocalAction = !string.IsNullOrEmpty(button.name) && localActions.Contains(button.name);
                Assert.IsTrue(isNavigator || isLocalAction,
                    $"Button '{button.name}' (text '{button.text}') must have a defined action (go- navigator or known local HomeController action).");
            }
        }
    }
}
