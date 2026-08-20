using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Structural contract for the redesigned Profile screen: the shared top HUD
    /// (relocated dock element names, see ProfileController/ProfileProgressionTests
    /// for the untouched Map/Techniques gating those names still drive) plus the
    /// three-region identity / capability-radar / current-journey composition.
    /// Cross-screen HUD checks shared by Map/Okinawa/Techniques/Profile live in
    /// Mikey.UI.Map.Tests.SharedTopBarRedesignTests instead of being duplicated here.
    /// </summary>
    public class ProfileScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string NavPrefix = "go-";
        private static readonly string LegacyResultScreen = "combine" + "Results";
        private static readonly string LegacyResultNavigator = "go-" + LegacyResultScreen;

        private static readonly string[] ExpectedScreenIds =
        {
            "title", "intro", "menu", "combineIntro", "camTest", "combine",
            "combinePushups", "combineSquats", "combineWallsit", "combineYokogeri",
            "techniques", "practice", "map", "mapOkinawa", "profile", "profileDetails",
        };

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
        public void Profile_ExistsExactlyOnce_AndProductionScreenCountIsSixteen()
        {
            var root = BuildTree();
            var screens = root.Query<VisualElement>(className: "screen").ToList();
            Assert.AreEqual(1, screens.Count(s => s.name == "profile"), "There must be exactly one profile screen.");
            Assert.AreEqual(16, screens.Count, "There must be exactly sixteen production screens (the four Level 0 placeholder test screens added).");
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

            foreach (var className in new[]
            {
                "map-topbar", "profile-layout", "profile-column--identity", "profile-column--radar", "profile-column--journey"
            })
            {
                var el = screen.Q<VisualElement>(className: className);
                Assert.IsNotNull(el, $"Expected .{className} on Profile.");
                Assert.IsNotNull(NearestSafeAreaAncestor(el), $".{className} must be inside .safe-area-content.");
            }
        }

        // Main Menu no longer links to Profile at all (the old Home dock's
        // go-profile tab is retired with the rest of the dashboard — see
        // HomeScreenUxmlTests). Profile itself is untouched and still reachable
        // from the shared top HUD / other screens; only this one entry point is gone.
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

            var map = Screen(root, "map");
            Assert.IsNotNull(map.Q<Button>("go-menu"));
            Assert.IsNotNull(map.Q<Button>("chapter-node-okinawa"), "Okinawa must exist as a chapter action.");
            Assert.IsNotNull(map.Q<Button>("chapter-panel-cta"), "Okinawa's chapter panel must expose an Enter Chapter action.");
            Assert.IsNotNull(map.Q<Button>("map-topbar-stats"), "Map's top bar Profile action must route to Profile.");
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
        public void TopBar_RelocatedDockElements_StillDriveTheSameGating_AndProfileIsActive()
        {
            var root = BuildTree();
            var topBar = Profile(root).Q<VisualElement>(className: "map-topbar");
            Assert.IsNotNull(topBar, "Profile must carry the shared top HUD.");
            Assert.IsNull(Profile(root).Q<VisualElement>(className: "profile-dock"), "The old bottom dock must be gone.");

            var goMenu = topBar.Q<VisualElement>("go-menu");
            Assert.IsNotNull(goMenu, "Profile's HUD must contain 'go-menu'.");

            // Map/Techniques are progression-gated (ProfileController), not static
            // "go-" navigators — see ProfileProgressionTests, unchanged by this redesign.
            foreach (var (name, target) in new[] { ("profile-nav-map", "map"), ("profile-nav-techniques", "techniques") })
            {
                var item = topBar.Q<VisualElement>(name);
                Assert.IsNotNull(item, $"Profile's HUD must contain '{name}'.");
                Assert.IsFalse(item.name.StartsWith(NavPrefix),
                    $"'{name}' must not be a 'go-' navigator — ProfileController gates navigation by progression state.");
                Assert.IsTrue(Screen(root, target).ClassListContains("screen"), $"'{name}' target must exist.");
                Assert.IsFalse(item.ClassListContains("map-topbar__nav-btn--active"), $"'{name}' must not be active on Profile.");
            }

            var active = topBar.Q<VisualElement>("nav-profile");
            Assert.IsNotNull(active, "Profile's HUD must contain the active 'nav-profile' item.");
            Assert.IsTrue(active.ClassListContains("map-topbar__nav-btn--active"), "Profile item must be marked active.");
            Assert.IsFalse(active.name.StartsWith(NavPrefix), "Active Profile item must not be a redundant navigator.");
        }

        [Test]
        public void TopBar_HasLevelXpAndSettings()
        {
            var topBar = Profile(BuildTree()).Q<VisualElement>(className: "map-topbar");
            Assert.IsNotNull(topBar.Q<Label>("profile-topbar-level"));
            Assert.IsNotNull(topBar.Q<Label>("profile-topbar-xp"));
            Assert.IsNotNull(topBar.Q<Button>("profile-topbar-settings"), "Profile's HUD must expose a Settings entry point.");
        }

        [Test]
        public void Identity_ShowsKickerNameRoleLevelAndXpBar()
        {
            var labels = Labels(Profile(BuildTree())).Select(l => l.text).ToList();
            foreach (var expected in new[] { "PROFILE", "Mikey", "Disciple", "3 Day Streak", "Chapter 0 · Okinawa", "1 Technique Learned" })
                CollectionAssert.Contains(labels, expected, $"Profile identity should include '{expected}'.");

            var screen = Profile(BuildTree());
            Assert.IsNotNull(screen.Q<VisualElement>(className: "profile-xp-bar"), "Expected an identity XP bar.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "profile-xp-bar__fill"), "Expected the XP bar's fill element.");
        }

        [Test]
        public void Radar_MountPointExistsInTheCenterColumn()
        {
            var screen = Profile(BuildTree());
            var radarColumn = screen.Q<VisualElement>(className: "profile-column--radar");
            Assert.IsNotNull(radarColumn, "Expected the center radar column.");
            Assert.IsNotNull(radarColumn.Q<VisualElement>("profile-radar-mount"),
                "Expected 'profile-radar-mount' — ProfileController mounts ProfileRadarChart into it at runtime.");
        }

        [Test]
        public void Journey_ShowsCurrentJourneyTrainingAndRecentMilestone()
        {
            var labels = Labels(Profile(BuildTree())).Select(l => l.text).ToList();
            foreach (var expected in new[]
            {
                "Current Journey", "Chapter 0", "Okinawa", "Level 1 of 6",
                "Training", "1 Assessment Completed",
                "Recent Milestone", "First Assessment Complete"
            })
            {
                CollectionAssert.Contains(labels, expected, $"Profile's journey column should include '{expected}'.");
            }
        }

        [Test]
        public void Background_UsesSuppliedFinalArt_AndOldDecorativeShapesAreGone()
        {
            // The shared dark background was tried on Profile and reverted — it
            // read poorly against Profile's own content, so Profile keeps its own
            // supplied art while Lore keeps the shared asset (see
            // SharedBackgroundAssetTests for the cross-screen contract).
            const string ussPath = "Assets/UI/Profile/Profile.uss";
            Assert.IsTrue(File.Exists(ussPath), $"Expected stylesheet at {ussPath}.");
            string uss = File.ReadAllText(ussPath);
            StringAssert.Contains("Media/Images/Profile/profile_background.jpg", uss,
                "Profile.uss must reference the supplied final background art.");
            StringAssert.Contains("scale-and-crop", uss, "Background must cover-fit, never stretch.");

            var screen = Profile(BuildTree());
            Assert.IsNull(screen.Q<VisualElement>(className: "profile-glow"), "The generated decorative glow circle must be removed.");
            Assert.IsNull(screen.Q<VisualElement>(className: "profile-enso"), "The generated decorative enso ring must be removed.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "profile-scrim"), "Expected a subtle readability scrim over the real art.");
        }

        [Test]
        public void PermanentAttributeGuide_IsRemoved()
        {
            var screen = Profile(BuildTree());
            Assert.IsNull(screen.Q<VisualElement>(className: "profile-attribute-guide"),
                "The always-visible Attribute Guide must be gone — it fought the background art for readability; " +
                "the radar now opens an Attribute Details popup instead.");
        }

        [Test]
        public void RadarTap_OpensAttributeDetailsPopup_WithAllFiveDetailedExplanations()
        {
            var root = BuildTree();
            var screen = Profile(root);

            var popup = screen.Q<VisualElement>("profile-attribute-popup");
            Assert.IsNotNull(popup, "Expected a Profile-local 'profile-attribute-popup' overlay.");
            Assert.IsNull(root.Q<VisualElement>("attributeDetails"), "Must not be a new screen id — Profile-local overlay only.");

            var labels = Labels(popup).Select(l => l.text).ToList();
            CollectionAssert.Contains(labels, "Attributes");

            foreach (var (name, description) in new[]
            {
                ("Strength", "Measures power production and striking force. Higher Strength reflects your ability to generate force efficiently through movement."),
                ("Speed", "Measures how quickly you move and react. Higher Speed reflects faster execution, transitions, and response time."),
                ("Form", "Measures technical precision and correctness. Higher Form reflects cleaner positioning, mechanics, and execution of techniques."),
                ("Stamina", "Measures endurance during repeated effort. Higher Stamina reflects your ability to maintain performance as a session becomes longer or more demanding."),
                ("Control", "Measures balance, stability, and body control. Higher Control reflects your ability to stay composed, coordinated, and accurate throughout movement."),
            })
            {
                CollectionAssert.Contains(labels, name, $"Attribute popup must name '{name}'.");
                CollectionAssert.Contains(labels, description, $"Attribute popup must give '{name}'s detailed explanation.");
            }

            Assert.AreEqual(5, popup.Query<VisualElement>(className: "profile-attribute-popup__entry").ToList().Count,
                "Expected one compact grid, not five separate cards.");
            Assert.IsNotNull(popup.Q<VisualElement>("profile-attribute-popup-scrim"), "Expected an outside-tap-to-close scrim.");
            Assert.IsNotNull(popup.Q<Button>("profile-attribute-popup-close"), "Expected an explicit Close control.");
        }

        [Test]
        public void EditIcon_ExistsBesideDisplayName_AndNavigatesToProfileDetails()
        {
            var root = BuildTree();
            var screen = Profile(root);

            var nameLabel = screen.Q<Label>("profile-display-name");
            Assert.IsNotNull(nameLabel, "Expected the display name Label.");
            Assert.AreEqual("Mikey", nameLabel.text, "Default display name must be 'Mikey'.");

            // Plain "go-profileDetails" navigator — no gating, so ScreenManager's
            // generic wiring handles the click with zero controller code.
            var editButton = screen.Q<Button>("go-profileDetails");
            Assert.IsNotNull(editButton, "Expected a 'go-profileDetails' edit button beside the display name.");
            Assert.IsTrue(Screen(root, "profileDetails").ClassListContains("screen"), "'go-profileDetails' target must exist.");

            string uss = File.ReadAllText("Assets/UI/Profile/Profile.uss");
            StringAssert.Contains("Media/Images/edit_icon.png", uss, "Must use the supplied edit_icon.png asset.");
        }

        [Test]
        public void OldUsernameOnlyPopup_IsFullyRemoved()
        {
            var screen = Profile(BuildTree());
            Assert.IsNull(screen.Q<VisualElement>("profile-username-edit-popup"),
                "The old username-only popup must be gone — Profile Details is now the one place identity is edited.");
            Assert.IsNull(screen.Q<Button>("profile-name-edit-open"),
                "The old controller-bound edit button must be gone, replaced by the 'go-profileDetails' navigator.");
        }

        [Test]
        public void NoOrangeAccentsRemainInProfileStyles()
        {
            string uss = File.ReadAllText("Assets/UI/Profile/Profile.uss");
            StringAssert.DoesNotContain("var(--ember)", uss, "Profile must not use the orange accent anywhere.");
            StringAssert.DoesNotContain("var(--seal)", uss, "Profile must use the true-red --crimson accent, not the orange-leaning --seal.");
            StringAssert.DoesNotContain("255, 90, 31", uss, "No leftover orange (--ember) literal rgba values.");
            StringAssert.Contains("var(--crimson)", uss);
        }

        [Test]
        public void RadarLabelStyle_HasNoTextShadow_AndIsNotBold()
        {
            string uss = File.ReadAllText("Assets/UI/Profile/Profile.uss");
            string block = ExtractRuleBlock(uss, ".profile-radar-label {");
            Assert.IsNotNull(block, "Expected a '.profile-radar-label' rule.");
            StringAssert.DoesNotContain("text-shadow", block, "Radar axis/value labels must be clean flat text, no glow.");
            StringAssert.Contains("-unity-font-style: normal", block, "Radar labels must not be bold (reads as heavier/glowier).");
        }

        [Test]
        public void RadarPolygonColors_AreTrueRed_NotOrange()
        {
            string source = File.ReadAllText("Assets/UI/Profile/ProfileRadarChart.cs");
            StringAssert.DoesNotContain("0.90f, 0.30f, 0.20f", source, "Old orange-shifted stroke color must be gone.");
            StringAssert.Contains("0.7765f, 0.1569f, 0.1569f", source, "Polygon fill/stroke/glow must use the true-red (#C62828) value.");
        }

        [Test]
        public void RadarValues_AreNoticeablyLarger_CleanOffWhite_StillGlowFree()
        {
            string uss = File.ReadAllText("Assets/UI/Profile/Profile.uss");
            string block = ExtractRuleBlock(uss, ".profile-radar-label--value {");
            Assert.IsNotNull(block, "Expected a '.profile-radar-label--value' rule.");
            var size = System.Text.RegularExpressions.Regex.Match(block, @"font-size:\s*(\d+(\.\d+)?)px");
            Assert.IsTrue(size.Success);
            float px = float.Parse(size.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(px, 23f);
            Assert.LessOrEqual(px, 25f, "Target ~24px (23-25px acceptable).");
            StringAssert.Contains("var(--bone)", block, "Values must read as clean off-white.");
            StringAssert.DoesNotContain("text-shadow", block, "Values must stay glow-free — only the polygon glows.");
        }

        [Test]
        public void AttributePopup_IsMuchDarker_CardAndScrimBothStrongOpacity()
        {
            string uss = File.ReadAllText("Assets/UI/Profile/Profile.uss");
            string cardBlock = ExtractRuleBlock(uss, ".profile-popup__card {");
            string scrimBlock = ExtractRuleBlock(uss, ".profile-popup__scrim {");
            Assert.IsNotNull(cardBlock);
            Assert.IsNotNull(scrimBlock);

            var cardAlpha = System.Text.RegularExpressions.Regex.Match(cardBlock, @"rgba\(\d+,\s*\d+,\s*\d+,\s*(\d+(\.\d+)?)\)");
            Assert.IsTrue(cardAlpha.Success);
            float cardOpacity = float.Parse(cardAlpha.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(cardOpacity, 0.96f);
            Assert.LessOrEqual(cardOpacity, 0.98f);

            var scrimAlpha = System.Text.RegularExpressions.Regex.Match(scrimBlock, @"rgba\(\d+,\s*\d+,\s*\d+,\s*(\d+(\.\d+)?)\)");
            Assert.IsTrue(scrimAlpha.Success);
            float scrimOpacity = float.Parse(scrimAlpha.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(scrimOpacity, 0.82f);
            Assert.LessOrEqual(scrimOpacity, 0.88f);
        }

        [Test]
        public void EditIcon_UsesTheReplacedAssetContents_Untinted()
        {
            // Path unchanged, contents manually replaced (now a white outline +
            // crimson pencil that's already legible on the dark HUD) — still
            // referenced by path only, no tint override, so Unity's own reimport
            // of the changed PNG bytes is all that's needed for the new art to
            // show up; nothing in this stylesheet needed to change.
            string uss = File.ReadAllText("Assets/UI/Profile/Profile.uss");
            string block = ExtractRuleBlock(uss, ".profile-name-edit-icon {");
            Assert.IsNotNull(block, "Expected a '.profile-name-edit-icon' rule.");
            StringAssert.Contains("Media/Images/edit_icon.png", block);
            StringAssert.DoesNotContain("-unity-background-image-tint-color", block,
                "Left untinted so the new asset's own colors show through unmodified.");
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

        [Test]
        public void OldDashboardAndDockStructure_IsFullyRemoved()
        {
            var screen = Profile(BuildTree());
            foreach (var oldClass in new[]
            {
                "profile-dashboard", "profile-card", "profile-identity", "profile-rank", "profile-meter",
                "profile-stats", "profile-stat-grid", "profile-achievements", "profile-badge-row",
                "profile-activity", "profile-activity-list", "profile-dock", "profile-tab"
            })
            {
                Assert.IsNull(screen.Q<VisualElement>(className: oldClass),
                    $"Old dashboard/dock class '.{oldClass}' must not remain after the redesign.");
            }
        }

        [Test]
        public void UnsupportedAccountEditAndSocialControls_AreOmittedOrExplicitlyDisabled()
        {
            var profile = Profile(BuildTree());
            string text = string.Join("\n", Labels(profile).Select(l => l.text));

            foreach (var unsupported in new[] { "Edit Profile", "Connect account", "Share", "Friends", "Notifications", "Store", "Subscription", "Support the dojo", "DEV" })
                StringAssert.DoesNotContain(unsupported, text, $"Unsupported control '{unsupported}' should not appear as active Profile UI.");
        }

        [Test]
        public void TouchTargetsUseTheSharedHudClass()
        {
            var topBar = Profile(BuildTree()).Q<VisualElement>(className: "map-topbar");
            foreach (var name in new[] { "go-menu", "profile-nav-map", "profile-nav-techniques", "nav-profile", "profile-topbar-settings" })
            {
                var item = topBar.Q<VisualElement>(name);
                Assert.IsNotNull(item, $"Expected HUD item '{name}'.");
                Assert.IsTrue(item.ClassListContains("tap-target"), $"HUD item '{name}' must use the shared '.tap-target' class.");
            }
        }

        [Test]
        public void LockedOrDisabledControls_DoNotCarryGoNavigators()
        {
            foreach (var el in Profile(BuildTree()).Query<VisualElement>().ToList())
            {
                bool disabledOrLocked = el.ClassListContains("disabled") || el.ClassListContains("locked");
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
            // Intro's exit is 'lore-skip'/'lore-continue' (LoreExitController's
            // cinematic transition), not a 'go-menu' navigator.
            Assert.IsNotNull(Screen(root, "intro").Q<VisualElement>("lore-skip"));
            Assert.IsNotNull(Screen(root, "intro").Q<VisualElement>("lore-continue"));
            Assert.IsNotNull(Screen(root, "menu").Q<Button>("go-map"), "Main Menu's PLAY must route to Map.");
            Assert.IsNotNull(Screen(root, "combineIntro").Q<Button>("go-combine"),
                "combineIntro must route to the Combine checklist, not directly to camTest.");
            Assert.IsNotNull(Screen(root, "camTest").Q<Button>("camera-test-complete"));
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
    }
}
