using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Structural contract for the read-only landscape Profile dashboard.
    /// Keeps Profile honest as frontend mock data: reference-derived identity,
    /// rank, stats, badges, recent activity, and a canonical dock with real routes.
    /// </summary>
    public class ProfileScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Profile/Profile.uss";
        private const string NavPrefix = "go-";
        private static readonly string LegacyResultScreen = "combine" + "Results";
        private static readonly string LegacyResultNavigator = "go-" + LegacyResultScreen;

        private static readonly string[] ExpectedScreenIds =
            { "title", "intro", "menu", "combineIntro", "camTest", "combine", "techniques", "practice", "map", "mapOkinawa", "profile" };

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
            Assert.IsTrue(screen.ClassListContains("screen"), $"'{id}' must carry the .screen class.");
            return screen;
        }

        private static VisualElement Profile(VisualElement root) => Screen(root, "profile");

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        private static List<Label> Labels(VisualElement el) => el.Query<Label>().ToList();

        [Test]
        public void Profile_ExistsExactlyOnce_AndProductionScreenCountIsEleven()
        {
            var root = BuildTree();
            var screens = root.Query<VisualElement>(className: "screen").ToList();
            Assert.AreEqual(1, screens.Count(s => s.name == "profile"), "There must be exactly one profile screen.");
            Assert.AreEqual(11, screens.Count, "There must be exactly eleven production screens.");
            CollectionAssert.AreEquivalent(ExpectedScreenIds, screens.Select(s => s.name).ToList());
        }

        [Test]
        public void EveryProductionScreen_HasExactlyOneSafeAreaContent()
        {
            foreach (var screen in BuildTree().Query<VisualElement>(className: "screen").ToList())
            {
                int count = screen.Query<VisualElement>(className: "safe-area-content").ToList().Count;
                Assert.AreEqual(1, count, $"Screen '{screen.name}' must contain exactly one .safe-area-content.");
            }
        }

        [Test]
        public void ProfileBackground_IsFullBleed_AndForegroundIsInsideSafeArea()
        {
            var screen = Profile(BuildTree());
            var bg = screen.Q<VisualElement>(className: "profile-bg");
            Assert.IsNotNull(bg, "Expected a .profile-bg full-bleed background.");
            Assert.IsNull(NearestSafeAreaAncestor(bg), ".profile-bg must not be inside .safe-area-content.");

            foreach (var className in new[] { "profile-dashboard", "profile-identity", "profile-stats", "profile-achievements", "profile-activity", "profile-dock" })
            {
                var el = screen.Q<VisualElement>(className: className);
                Assert.IsNotNull(el, $"Expected .{className} on Profile.");
                Assert.IsNotNull(NearestSafeAreaAncestor(el), $".{className} must be inside .safe-area-content.");
            }
        }

        [Test]
        public void Profile_UsesReferenceIdentityRankStatsBadgesAndActivityContent()
        {
            var labels = Labels(Profile(BuildTree())).Select(l => l.text).ToList();

            foreach (var expected in new[]
            {
                "Your dojo",
                "1-DAY STREAK · LEVEL 1 · $10.00 BANKED",
                "White belt. Restoring the Heian base form and the foundation lifts.",
                "White Belt",
                "LADDER RUNG 1 · DOJO",
                "Japan · Karate",
                "Heian — form restored 10%",
                "Stats",
                "Your fighter's attributes",
                "The Vow",
                "Your daily commitment",
                "Plans",
                "Membership & pricing",
                "Dojo",
                "Front Stance",
                "You are here",
                "Heian — day 1",
                "A fragment restored · 3 squats: 1 no-rep, 2 clean"
            })
            {
                CollectionAssert.Contains(labels, expected, $"Profile should include reference content '{expected}'.");
            }
        }

        // Main Menu no longer links to Profile at all (the old Home dock's
        // go-profile tab is retired with the rest of the dashboard — see
        // HomeScreenUxmlTests). Profile itself is untouched and still reachable
        // from its own dock / other screens; only this one entry point is gone.
        [Test]
        public void Menu_NoLongerLinksToProfile()
        {
            var root = BuildTree();
            var menu = Screen(root, "menu");
            Assert.IsNull(menu.Q<VisualElement>("go-profile"),
                "Main Menu must not reintroduce a go-profile navigator (old Home dashboard concept).");
        }

        [Test]
        public void Map_ExistingNavigationStatesRemainIntact_AndHasNoProfileTab()
        {
            var root = BuildTree();

            // The Japan world map ("map") has no large 4-tab dock: its top bar
            // exposes a Stats action (routes to 'profile') and a minimal 'go-menu'
            // way back to the Main Menu, not a dock go-profile tab. Okinawa
            // (chapter-node-okinawa) no longer routes directly to Techniques:
            // selecting it opens the chapter detail panel, whose "ENTER CHAPTER"
            // action (chapter-panel-cta) enters the Okinawa chapter map
            // ("mapOkinawa"), where each level's own popup CTA is what finally
            // routes to Techniques/combineIntro.
            var map = Screen(root, "map");
            Assert.IsNotNull(map.Q<Button>("go-menu"));
            Assert.IsNotNull(map.Q<Button>("chapter-node-okinawa"), "Okinawa must exist as a chapter action.");
            Assert.IsNotNull(map.Q<Button>("chapter-panel-cta"), "Okinawa's chapter panel must expose an Enter Chapter action.");
            Assert.IsNotNull(map.Q<Button>("map-topbar-stats"), "Map's top bar Stats action must route to Profile.");
            Assert.IsNull(map.Q<VisualElement>("go-profile"),
                "Map must not reintroduce a dock go-profile tab.");

            var mapOkinawa = Screen(root, "mapOkinawa");
            Assert.IsNotNull(mapOkinawa.Q<Button>("go-menu"));
            Assert.IsNotNull(mapOkinawa.Q<Button>("level-node-0"), "LVL 0 must exist as a level action.");
            Assert.IsNotNull(mapOkinawa.Q<Button>("level-panel-cta"), "LVL 0's popup must expose a Begin action.");
            Assert.IsNull(mapOkinawa.Q<VisualElement>("go-profile"),
                "The Okinawa chapter map must not reintroduce a dock go-profile tab.");
        }

        [Test]
        public void ProfileDock_HasExpectedRealRoutes_AndActiveProfileHasNoNavigator()
        {
            var root = BuildTree();
            var dock = Profile(root).Q<VisualElement>(className: "profile-dock");
            Assert.IsNotNull(dock, "Profile must have a .profile-dock.");

            // "go-menu" is a plain, always-available ScreenManager navigator.
            var goMenu = dock.Q<VisualElement>("go-menu");
            Assert.IsNotNull(goMenu, "Profile dock must contain 'go-menu'.");
            Assert.IsTrue(Screen(root, "menu").ClassListContains("screen"), "'go-menu' target must exist.");
            Assert.IsFalse(goMenu.ClassListContains("profile-tab--active"), "'go-menu' must not be active on Profile.");

            // Map/Techniques are progression-gated (ProfileController), not static
            // "go-" navigators — mirrors Home's home-nav-map/home-nav-techniques so
            // Profile can no longer bypass Home's Level1Unlocked lock.
            foreach (var (name, target) in new[] { ("profile-nav-map", "map"), ("profile-nav-techniques", "techniques") })
            {
                var tab = dock.Q<VisualElement>(name);
                Assert.IsNotNull(tab, $"Profile dock must contain '{name}'.");
                Assert.IsFalse(tab.name.StartsWith(NavPrefix),
                    $"'{name}' must not be a 'go-' navigator — ProfileController gates navigation by progression state.");
                Assert.IsTrue(Screen(root, target).ClassListContains("screen"), $"'{name}' target must exist.");
                Assert.IsFalse(tab.ClassListContains("profile-tab--active"), $"'{name}' must not be active on Profile.");
            }

            var active = dock.Q<VisualElement>("nav-profile");
            Assert.IsNotNull(active, "Profile dock must contain active nav-profile.");
            Assert.IsTrue(active.ClassListContains("profile-tab--active"), "Profile tab must be active.");
            Assert.IsFalse(active.name.StartsWith(NavPrefix), "Active Profile tab must not be a redundant navigator.");
            Assert.IsNull(dock.Q<VisualElement>("go-profile"), "Profile dock must not contain go-profile on the active Profile tab.");
        }

        [Test]
        public void ProfileDock_NoActiveLookingControlLacksAction()
        {
            var dock = Profile(BuildTree()).Q<VisualElement>(className: "profile-dock");
            foreach (var tab in dock.Query<VisualElement>(className: "profile-tab").ToList())
            {
                bool isNavigator = !string.IsNullOrEmpty(tab.name) && tab.name.StartsWith(NavPrefix);
                // Map/Techniques are controller-bound (ProfileController) rather than
                // "go-" navigators, but still have a defined, gated action — see
                // ProfileDock_HasExpectedRealRoutes_AndActiveProfileHasNoNavigator.
                bool isControllerBoundAction = tab.name == "profile-nav-map" || tab.name == "profile-nav-techniques";
                bool isActive = tab.ClassListContains("profile-tab--active");
                Assert.IsTrue(isNavigator || isControllerBoundAction || isActive,
                    $"Dock tab '{tab.name}' must be a navigator, a known controller-bound action, or the active tab.");
            }

            Assert.IsEmpty(Profile(BuildTree()).Query<Button>().ToList(),
                "Profile should remain a UXML/USS-only read-only dashboard with no unbound Buttons.");
        }

        [Test]
        public void UnsupportedAccountEditAndSocialControls_AreOmittedOrExplicitlyDisabled()
        {
            var profile = Profile(BuildTree());
            string text = string.Join("\n", Labels(profile).Select(l => l.text));

            foreach (var unsupported in new[] { "Edit Profile", "Settings", "Connect account", "Share", "Friends", "Notifications", "Store", "Subscription", "Support the dojo", "DEV" })
                StringAssert.DoesNotContain(unsupported, text, $"Unsupported control '{unsupported}' should not appear as active Profile UI.");

            var disabled = profile.Query<VisualElement>(className: "profile-badge--disabled").ToList();
            Assert.AreEqual(1, disabled.Count, "Unsupported Plans content may appear only as an explicitly disabled badge.");
            Assert.IsNull(disabled[0].Q<VisualElement>(name: "go-paywall"), "Disabled Plans badge must not carry a navigator.");
            Assert.IsNotNull(disabled[0].Q<VisualElement>(className: "profile-disabled-label"),
                "Disabled Plans badge must show an explicit disabled/coming-soon label.");
        }

        [Test]
        public void TouchTargetsAndVisibleIcons_UseReusableNonShrinkingClasses()
        {
            var screen = Profile(BuildTree());

            foreach (var tab in screen.Q<VisualElement>(className: "profile-dock").Query<VisualElement>(className: "profile-tab").ToList())
                Assert.IsTrue(tab.ClassListContains("tap-target-lg"), $"Dock tab '{tab.name}' must use .tap-target-lg.");

            foreach (var icon in screen.Query<VisualElement>(className: "profile-icon").ToList())
            {
                bool hasSize =
                    icon.ClassListContains("profile-icon--avatar") ||
                    icon.ClassListContains("profile-icon--rank") ||
                    icon.ClassListContains("profile-icon--stat") ||
                    icon.ClassListContains("profile-icon--badge") ||
                    icon.ClassListContains("profile-icon--activity") ||
                    icon.ClassListContains("profile-icon--nav");
                Assert.IsTrue(hasSize, "Every Profile icon must use an explicit reusable visible-size class.");
            }

            Assert.IsNotNull(screen.Q<VisualElement>(className: "profile-icon--avatar"), "Profile needs a large identity mark.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "profile-icon--rank"), "Profile needs a rank/belt icon.");
            Assert.IsNotEmpty(screen.Query<VisualElement>(className: "profile-icon--stat").ToList(), "Profile needs stat icons.");
            Assert.IsNotEmpty(screen.Query<VisualElement>(className: "profile-icon--badge").ToList(), "Profile needs badge icons.");
        }

        [Test]
        public void ResponsiveStructuralClasses_ArePresent()
        {
            var screen = Profile(BuildTree());
            foreach (var className in new[] { "profile-dashboard", "profile-card", "profile-identity", "profile-rank", "profile-stats", "profile-achievements", "profile-activity", "profile-stat-grid", "profile-badge-row", "profile-dock" })
                Assert.IsNotNull(screen.Q<VisualElement>(className: className), $"Expected responsive structural class .{className}.");
        }

        [Test]
        public void ProfileStyles_DoNotUseOverflowProneSiblingActionWidth()
        {
            Assert.IsTrue(File.Exists(UssPath), $"Expected stylesheet at {UssPath}.");
            string uss = File.ReadAllText(UssPath);
            foreach (var header in new[] { ".profile-tab {", ".profile-stat {", ".profile-badge {", ".profile-activity-item {" })
            {
                string block = ExtractRuleBlock(uss, header);
                Assert.IsNotNull(block, $"Expected a {header} rule.");
                StringAssert.DoesNotContain("width: 100%", block,
                    $"{header} must not use width:100% on sibling controls/cards.");
            }
        }

        [Test]
        public void LockedOrDisabledControls_DoNotCarryGoNavigators()
        {
            foreach (var el in Profile(BuildTree()).Query<VisualElement>().ToList())
            {
                bool disabledOrLocked =
                    el.ClassListContains("profile-badge--disabled") ||
                    el.ClassListContains("disabled") ||
                    el.ClassListContains("locked");
                if (!disabledOrLocked)
                    continue;

                Assert.IsTrue(string.IsNullOrEmpty(el.name) || !el.name.StartsWith(NavPrefix),
                    "Disabled/locked Profile UI must not carry a go- navigator.");
            }
        }

        [Test]
        public void RegressionRoutesRemainUnchanged_AndRetiredCombineResultRouteDoesNotReturn()
        {
            var root = BuildTree();
            Assert.IsNotNull(Screen(root, "title"));
            Assert.IsNotEmpty(Screen(root, "intro").Query<VisualElement>(name: "go-menu").ToList());
            Assert.IsNotNull(Screen(root, "menu").Q<Button>("go-map"), "Main Menu's PLAY must route to Map.");
            Assert.IsNotNull(Screen(root, "combineIntro").Q<Button>("go-camTest"));
            Assert.IsNotNull(Screen(root, "camTest").Q<Button>("go-combine"));
            Assert.IsNotEmpty(Screen(root, "combine").Query<VisualElement>(name: "go-menu").ToList());
            Assert.IsNotNull(Screen(root, "techniques").Q<Button>("go-practice"));
            Assert.IsNotNull(Screen(root, "practice").Q<Button>("go-techniques"));
            // Map's Okinawa chapter routes into the Okinawa chapter map via its
            // chapter panel's Enter Chapter action (see
            // Map_ExistingNavigationStatesRemainIntact_AndHasNoProfileTab for the
            // direct check, including the chapter map's own level popup CTA).
            Assert.IsNotNull(Screen(root, "map").Q<Button>("chapter-panel-cta"));
            Assert.IsNotNull(Screen(root, "mapOkinawa").Q<Button>("go-menu"));
            Assert.IsNull(root.Q<VisualElement>(LegacyResultScreen));
            Assert.IsNull(root.Q<VisualElement>(LegacyResultNavigator));
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
    }
}
