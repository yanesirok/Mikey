using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Verifies the MikeyApp.uxml structural contract: exactly twelve production
    /// screens (the six post-consolidation entry/Combine screens plus the
    /// Techniques hub, Practice slice, the two-tier Map flow, and Profile
    /// Details), one dedicated
    /// ".safe-area-content" per screen, full-bleed elements outside the wrappers,
    /// the mapped foreground elements inside them, Logo Intro's button-free
    /// contract (TitleController drives navigation itself), and the untouched
    /// combineIntro → camTest → combine flow.
    /// </summary>
    public class MikeyAppUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private static readonly string LegacyResultScreen = "combine" + "Results";
        private static readonly string LegacyResultNavigator = "go-" + LegacyResultScreen;

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static List<VisualElement> ByClass(VisualElement el, string className) =>
            el.Query<VisualElement>(className: className).ToList();

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        // The sixteen production screens: the six post-consolidation entry/Combine
        // screens, the four Level 0 placeholder test screens (combinePushups/
        // Squats/Wallsit/Yokogeri — no real assessment built yet), the Techniques
        // lesson hub, the Practice training slice, the two-tier Map flow (the
        // Japan world map plus the Okinawa chapter map), and Profile Details
        // (Display Name/Gender/Age/Weight/Height).
        private static readonly string[] ExpectedScreenIds =
        {
            "title", "intro", "menu", "combineIntro", "camTest", "combine",
            "combinePushups", "combineSquats", "combineWallsit", "combineYokogeri",
            "techniques", "practice", "map", "mapOkinawa", "profile", "profileDetails",
        };

        // 1
        [Test]
        public void HasExactlySixteenScreens()
        {
            Assert.AreEqual(16, ByClass(BuildTree(), "screen").Count);
        }

        // 2
        [Test]
        public void ScreenIds_AreExactlyTheSixteenProductionScreens()
        {
            var ids = ByClass(BuildTree(), "screen").Select(s => s.name).ToList();
            CollectionAssert.AreEquivalent(ExpectedScreenIds, ids);
        }

        // 3
        [Test]
        public void LegacySplashScreen_DoesNotExist()
        {
            Assert.IsNull(BuildTree().Q<VisualElement>("splash"),
                "Legacy 'splash' screen must be removed.");
        }

        // 4 — Title has no CTA button anymore: TitleController advances to Intro
        // itself (auto-advance timer + tap-anywhere), so no "go-intro" navigator
        // exists anywhere in production.
        [Test]
        public void GoIntroNavigator_NoLongerExists_TitleDrivesNavigationItself()
        {
            var root = BuildTree();
            Assert.IsEmpty(root.Query<VisualElement>(name: "go-intro").ToList(),
                "Title has no CTA — TitleController advances to Intro itself, not a 'go-' navigator.");

            var intro = root.Q<VisualElement>("intro");
            Assert.IsNotNull(intro, "The 'intro' screen TitleController advances to must still exist.");
            Assert.IsTrue(intro.ClassListContains("screen"), "'intro' target must be a screen.");
        }

        // 8 + 9 — Logo Intro is minimal by design: no buttons, just the full-bleed
        // logo video (see Assets/UI/Title/Tests for TitleController's own video
        // playback/tap-skip contract).
        [Test]
        public void TitleScreen_HasNoButtons_OnlyTheLogoVideo()
        {
            var title = BuildTree().Q<VisualElement>("title");
            Assert.IsNotNull(title, "Expected a 'title' screen.");
            Assert.IsEmpty(title.Query<Button>().ToList(),
                "Logo Intro must have no buttons — video completion + tap-anywhere only.");
            Assert.IsNotNull(title.Q<VisualElement>("title-video"),
                "Title must have a video target for the final logo animation.");
            Assert.IsNull(title.Q<VisualElement>(className: "title-logo"),
                "The retired static logo mark must be gone — the video is the logo now.");
            Assert.IsNull(title.Q<VisualElement>("title-logo-hold"),
                "There must be no separate static final-logo hold element — TitleController freezes on the video's own final frame instead.");
        }

        [Test]
        public void LegacyCombineResultScreen_DoesNotExist()
        {
            Assert.IsNull(BuildTree().Q<VisualElement>(LegacyResultScreen),
                "Retired Combine result screen must be removed.");
        }

        [Test]
        public void LegacyGoCombineResultNavigator_DoesNotExist()
        {
            Assert.IsNull(BuildTree().Q<VisualElement>(LegacyResultNavigator),
                "Retired Combine result navigator must be removed.");
        }

        // 17 — combineIntro → camTest → combine → menu remains structurally
        // unchanged, even though Main Menu's PLAY no longer routes through it (PLAY
        // goes straight to Map now — see HomeScreenUxmlTests). These screens and
        // their internal routes are untouched, just no longer reachable from the
        // rebuilt Main Menu.
        [Test]
        public void CombineIntroToCamTestToCombineFlow_RemainsUnchanged()
        {
            var root = BuildTree();

            Assert.IsNotNull(root.Q<VisualElement>("menu"), "Expected a 'menu' (Main Menu) screen.");

            // combineIntro → camTest
            var combineIntro = root.Q<VisualElement>("combineIntro");
            Assert.IsNotNull(combineIntro, "Expected a 'combineIntro' screen.");
            Assert.IsNotNull(combineIntro.Q<Button>("go-camTest"),
                "combineIntro must keep its 'go-camTest' CTA.");

            // camTest → combine
            var camTest = root.Q<VisualElement>("camTest");
            Assert.IsNotNull(camTest, "Expected a 'camTest' screen.");
            Assert.IsNotNull(camTest.Q<Button>("camera-test-complete"),
                "camTest must route to the Combine checklist via a 'camera-test-complete' button (controller-bound, not a bare 'go-' navigator, since it also marks Level 0's Camera Test complete).");

            // combine → menu (return Home)
            var combine = root.Q<VisualElement>("combine");
            Assert.IsNotNull(combine, "Expected the modern 'combine' screen.");
            Assert.IsTrue(combine.ClassListContains("screen"), "'combine' must carry the .screen class.");
            Assert.IsNotEmpty(combine.Query<VisualElement>(name: "go-menu").ToList(),
                "Combine must keep a 'go-menu' return-Home route.");
        }

        [Test]
        public void GoMenuNavigator_TargetsAnExistingMenuScreen()
        {
            var root = BuildTree();
            // ScreenManager maps a 'go-<id>' navigator to the screen named <id>.
            Assert.IsNotEmpty(root.Query<VisualElement>(name: "go-menu").ToList(),
                "Expected at least one 'go-menu' navigator.");
            var menu = root.Q<VisualElement>("menu");
            Assert.IsNotNull(menu, "'go-menu' must target an existing 'menu' screen.");
            Assert.IsTrue(menu.ClassListContains("screen"), "'menu' target must be a screen.");
        }

        // 16 — no production route still references 'splash'.
        [Test]
        public void NoProductionRoute_ReferencesSplash()
        {
            var root = BuildTree();
            Assert.IsEmpty(root.Query<VisualElement>(name: "splash").ToList(),
                "No element may be named 'splash'.");
            Assert.IsEmpty(root.Query<VisualElement>(name: "go-splash").ToList(),
                "No 'go-splash' navigator may target the removed Splash screen.");

            string text = File.ReadAllText(UxmlPath);
            StringAssert.DoesNotContain("splash", text,
                "MikeyApp.uxml must not reference the removed 'splash' screen or its styles.");
        }

        [Test]
        public void RemovedLegacySelectors_AreNotReferencedByUxml()
        {
            string text = File.ReadAllText(UxmlPath);
            foreach (var selector in new[] { LegacyResultScreen, LegacyResultNavigator, "class=\"bar", "class=\"fill" })
            {
                StringAssert.DoesNotContain(selector, text,
                    $"MikeyApp.uxml must not reference the removed legacy selector '{selector}'.");
            }
        }

        // 6 + 15 — exactly one safe-area wrapper per screen (incl. Title).
        [Test]
        public void EveryScreenHasExactlyOneSafeAreaContent()
        {
            foreach (var screen in ByClass(BuildTree(), "screen"))
            {
                int count = ByClass(screen, "safe-area-content").Count;
                Assert.AreEqual(1, count,
                    $"Screen '{screen.name}' must contain exactly one .safe-area-content (found {count}).");
            }
        }

        // 7 — full-bleed decorative layers (incl. Title's) live outside the wrapper.
        [Test]
        public void FullBleedElementsAreNotInsideSafeAreaContent()
        {
            var root = BuildTree();
            foreach (var className in new[] { "title-bg", "title-video", "cam-feed", "combine-bg", "intro-bg", "tq-bg", "pr-feed", "map-bg", "profile-bg" })
            {
                var matches = ByClass(root, className);
                Assert.IsNotEmpty(matches, $"Expected at least one .{className}.");
                foreach (var el in matches)
                {
                    Assert.IsNull(NearestSafeAreaAncestor(el),
                        $".{className} must not be a descendant of .safe-area-content.");
                }
            }
        }

        [Test]
        public void MappedForegroundElementsAreInsideSafeAreaContent()
        {
            var root = BuildTree();
            foreach (var className in new[] { "content", "cam-actionbar", "cam-live", "skip", "combine-layout",
                "tq-layout", "tq-lessons", "tq-actionbar", "pr-hud", "pr-actionbar", "pr-stage",
                "map-root", "pan-stage", "detail-panel",
                "profile-layout", "profile-column--identity", "profile-column--radar", "profile-column--journey" })
            {
                var matches = ByClass(root, className);
                Assert.IsNotEmpty(matches, $"Expected at least one .{className}.");
                foreach (var el in matches)
                {
                    Assert.IsNotNull(NearestSafeAreaAncestor(el),
                        $".{className} must be a descendant of .safe-area-content.");
                }
            }
        }

        // 30 — Intro → Home route remains unchanged (Title's own route into Intro
        // is driven by TitleController, not a "go-" navigator — see
        // GoIntroNavigator_NoLongerExists_TitleDrivesNavigationItself). Intro's
        // own exit is likewise not a "go-" navigator: 'lore-skip'/'lore-continue'
        // are driven by LoreExitController's cinematic transition instead (see
        // LoreExitControllerTests), not ScreenManager's auto-wiring.
        [Test]
        public void IntroToHomeRoute_RemainsUnchanged()
        {
            var root = BuildTree();

            Assert.IsNotNull(root.Q<VisualElement>("title"), "Expected a 'title' screen.");
            Assert.IsNotNull(root.Q<VisualElement>("intro"), "Expected an 'intro' screen.");

            // intro → menu (Continue / Skip both exit to Home via LoreExitController)
            var intro = root.Q<VisualElement>("intro");
            Assert.IsNotNull(intro.Q<VisualElement>("lore-skip"), "Intro must keep a 'lore-skip' route to Home.");
            Assert.IsNotNull(intro.Q<VisualElement>("lore-continue"), "Intro must keep a 'lore-continue' route to Home.");
            var menu = root.Q<VisualElement>("menu");
            Assert.IsNotNull(menu, "Lore's exit must target an existing 'menu' (Home) screen.");
            Assert.IsTrue(menu.ClassListContains("screen"), "'menu' target must be a screen.");
        }

        // 31 — the shared launch transition overlay: exists once, outside every
        // screen (never toggled by ScreenManager), declared last so it paints
        // above whichever screen is active, and starts fully transparent and
        // click-through.
        [Test]
        public void TransitionOverlay_ExistsOnce_OutsideEveryScreen_DeclaredLast()
        {
            var root = BuildTree();

            var overlays = root.Query<VisualElement>(name: "transition-overlay").ToList();
            Assert.AreEqual(1, overlays.Count, "Expected exactly one 'transition-overlay' element.");
            Assert.IsFalse(overlays[0].ClassListContains("screen"),
                "The transition overlay must not carry the .screen class — ScreenManager must never toggle it.");

            var appChildren = root.Q<VisualElement>("app")?.Children().ToList() ?? root.Children().ToList();
            Assert.AreEqual("transition-overlay", appChildren[appChildren.Count - 1].name,
                "The transition overlay must be declared last so it always paints above every screen and the shared Settings modal.");
        }

        [Test]
        public void TransitionOverlay_StartsTransparentAndClickThrough()
        {
            var root = BuildTree();
            var overlay = root.Q<VisualElement>("transition-overlay");
            Assert.IsNotNull(overlay, "Expected a 'transition-overlay' element.");
            Assert.AreEqual(PickingMode.Ignore, overlay.pickingMode,
                "The overlay must start click-through — TransitionOverlayController only flips it to Position while actively covering the screen.");

            string uss = File.ReadAllText("Assets/UI/theme.uss");
            int start = uss.IndexOf("\n.transition-overlay {", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "Expected a '.transition-overlay' rule in theme.uss.");
            int close = uss.IndexOf('}', start);
            string block = uss.Substring(start, close - start);
            StringAssert.Contains("opacity: 0", block, "The overlay must start fully transparent.");
            StringAssert.Contains("background-color: #000000", block, "The overlay must be pure black.");
        }

        // 32 — Settings and Vow are local overlays, not ScreenManager screens:
        // opening/closing them can never raise ScreenChanged, so AudioController's
        // hub soundtrack (which only reacts to ScreenChanged) is structurally
        // unaffected by them — see AudioControllerHubMusicTests.
        [Test]
        public void SharedSettingsModalAndVowModal_AreNotScreens_SoTheyCannotInterruptHubMusic()
        {
            var root = BuildTree();
            var settings = root.Q<VisualElement>("shared-settings-modal");
            var vow = root.Q<VisualElement>("menu-vow-modal");
            Assert.IsNotNull(settings, "Expected the shared Settings modal.");
            Assert.IsNotNull(vow, "Expected the Vow modal.");
            Assert.IsFalse(settings.ClassListContains("screen"),
                "The shared Settings modal must not carry the .screen class — ScreenManager (and anything keyed to ScreenChanged, like hub music) must never react to it opening/closing.");
            Assert.IsFalse(vow.ClassListContains("screen"),
                "The Vow modal must not carry the .screen class — ScreenManager (and anything keyed to ScreenChanged, like hub music) must never react to it opening/closing.");
        }

        // 33 — theme.uss ".mikey-app" remains the ONE place the global Mikey font
        // is declared. Every descendant TextElement (including labels the Profile
        // radar creates dynamically in C#) inherits it purely through the USS
        // cascade; a local "-unity-font-definition" override anywhere else would
        // both defeat that inheritance for its subtree and violate theme.uss's
        // own "screens should not redeclare font-family locally" contract.
        [Test]
        public void GlobalMikeyFont_IsDeclaredExactlyOnce_InThemeUss()
        {
            string uiRoot = Path.Combine(UnityEngine.Application.dataPath, "UI");
            Assert.IsTrue(Directory.Exists(uiRoot), $"Expected {uiRoot} to exist.");

            int totalDeclarations = 0;
            foreach (string path in Directory.GetFiles(uiRoot, "*.uss", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);
                int count = CountOccurrences(source, "-unity-font-definition");
                if (count == 0)
                    continue;

                totalDeclarations += count;
                Assert.AreEqual("theme.uss", Path.GetFileName(path),
                    $"'{Path.GetFileName(path)}' must not redeclare the font locally — theme.uss's '.mikey-app' is the sole authoritative source.");
            }

            Assert.AreEqual(1, totalDeclarations, "Expected exactly one '-unity-font-definition' declaration across all of Assets/UI (in theme.uss).");

            string themeUss = File.ReadAllText("Assets/UI/theme.uss");
            StringAssert.Contains("mikey_ui.otf", themeUss);
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
