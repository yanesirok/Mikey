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

        // The twelve production screens: the six post-consolidation entry/Combine
        // screens plus the Techniques lesson hub, the Practice training slice, the
        // two-tier Map flow (the Japan world map plus the Okinawa chapter map),
        // and Profile Details (Display Name/Gender/Age/Weight/Height).
        private static readonly string[] ExpectedScreenIds =
            { "title", "intro", "menu", "combineIntro", "camTest", "combine", "techniques", "practice", "map", "mapOkinawa", "profile", "profileDetails" };

        // 1
        [Test]
        public void HasExactlyTwelveScreens()
        {
            Assert.AreEqual(12, ByClass(BuildTree(), "screen").Count);
        }

        // 2
        [Test]
        public void ScreenIds_AreExactlyTheTwelveProductionScreens()
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

        // 17 — combineIntro → camTest → combine → map remains structurally
        // unchanged, even though Main Menu's PLAY no longer routes through it (PLAY
        // goes straight to Map now — see HomeScreenUxmlTests). These screens and
        // their internal routes are untouched, just no longer reachable from the
        // rebuilt Main Menu.
        [Test]
        public void CombineIntroToCamTestToCombineFlow_RemainsUnchanged()
        {
            var root = BuildTree();

            Assert.IsNotNull(root.Q<VisualElement>("menu"), "Expected a 'menu' (Sign In) screen.");

            // combineIntro → camTest
            var combineIntro = root.Q<VisualElement>("combineIntro");
            Assert.IsNotNull(combineIntro, "Expected a 'combineIntro' screen.");
            Assert.IsNotNull(combineIntro.Q<Button>("go-camTest"),
                "combineIntro must keep its 'go-camTest' CTA.");

            // camTest → combine
            var camTest = root.Q<VisualElement>("camTest");
            Assert.IsNotNull(camTest, "Expected a 'camTest' screen.");
            Assert.IsNotNull(camTest.Q<Button>("go-combine"),
                "camTest must route to the modern Combine screen via a 'go-combine' button.");

            // combine → map (return to the Map hub)
            var combine = root.Q<VisualElement>("combine");
            Assert.IsNotNull(combine, "Expected the modern 'combine' screen.");
            Assert.IsTrue(combine.ClassListContains("screen"), "'combine' must carry the .screen class.");
            Assert.IsNotEmpty(combine.Query<VisualElement>(name: "go-map").ToList(),
                "Combine must keep a 'go-map' return-to-Map route.");
        }

        [Test]
        public void GoMapNavigator_TargetsAnExistingMapScreen()
        {
            var root = BuildTree();
            // ScreenManager maps a 'go-<id>' navigator to the screen named <id>.
            Assert.IsNotEmpty(root.Query<VisualElement>(name: "go-map").ToList(),
                "Expected at least one 'go-map' navigator.");
            var map = root.Q<VisualElement>("map");
            Assert.IsNotNull(map, "'go-map' must target an existing 'map' screen.");
            Assert.IsTrue(map.ClassListContains("screen"), "'map' target must be a screen.");

            // The Main Menu hub is retired: 'menu' is the Sign In gate now, so no
            // screen anywhere may navigate back into it.
            Assert.IsEmpty(root.Query<VisualElement>(name: "go-menu").ToList(),
                "No 'go-menu' navigator may survive — nothing returns to the Sign In gate.");
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
            foreach (var className in new[] { "content", "cam-actionbar", "cam-live", "skip", "combine-content",
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
        public void IntroToMapRoute_RemainsUnchanged()
        {
            var root = BuildTree();

            Assert.IsNotNull(root.Q<VisualElement>("title"), "Expected a 'title' screen.");
            Assert.IsNotNull(root.Q<VisualElement>("intro"), "Expected an 'intro' screen.");

            // intro → map (Continue / Skip both exit to the Map via LoreExitController)
            var intro = root.Q<VisualElement>("intro");
            Assert.IsNotNull(intro.Q<VisualElement>("lore-skip"), "Intro must keep a 'lore-skip' route to the Map.");
            Assert.IsNotNull(intro.Q<VisualElement>("lore-continue"), "Intro must keep a 'lore-continue' route to the Map.");
            var map = root.Q<VisualElement>("map");
            Assert.IsNotNull(map, "Lore's exit must target an existing 'map' screen.");
            Assert.IsTrue(map.ClassListContains("screen"), "'map' target must be a screen.");
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

        // 32 — Settings is a local overlay, not a ScreenManager screen:
        // opening/closing it can never raise ScreenChanged, so AudioController's
        // hub soundtrack (which only reacts to ScreenChanged) is structurally
        // unaffected by it — see AudioControllerHubMusicTests. (The Vow overlay
        // that used to share this contract is retired with the Main Menu.)
        [Test]
        public void SharedSettingsModal_IsNotAScreen_SoItCannotInterruptHubMusic()
        {
            var root = BuildTree();
            var settings = root.Q<VisualElement>("shared-settings-modal");
            Assert.IsNotNull(settings, "Expected the shared Settings modal.");
            Assert.IsFalse(settings.ClassListContains("screen"),
                "The shared Settings modal must not carry the .screen class — ScreenManager (and anything keyed to ScreenChanged, like hub music) must never react to it opening/closing.");
            Assert.IsNull(root.Q<VisualElement>("menu-vow-modal"),
                "The Vow overlay is retired with the Main Menu.");
        }

        // 33 — theme.uss ".mikey-app" remains the ONE place the GLOBAL Mikey
        // font is declared, and no other stylesheet may restate it. Every
        // descendant TextElement (including labels the Profile radar creates
        // dynamically in C#) inherits it purely through the USS cascade; a
        // local re-declaration of that same font would both defeat the
        // inheritance for its subtree and violate theme.uss's own "screens
        // should not redeclare font-family locally" contract.
        //
        // What this does NOT forbid is a DIFFERENT typeface deliberately
        // introduced for one component. The Okinawa level scroll is the first:
        // its brush display and body serif ARE the design, not a screen
        // restating the app font. So the rule is "one global font, plus an
        // explicit allowlist of intentional faces" rather than a raw count of
        // declarations — a raw count could only be satisfied by hoisting the
        // scroll's two fonts into theme.uss, where nothing else would ever use
        // them and where they would sit further from the rules that need them.
        // Adding a face to the allowlist is meant to be a deliberate edit; a
        // screen quietly reaching for its own font still fails here.
        private static readonly string[] IntentionalLocalFonts =
        {
            "YujiMai-Latin.ttf",        // Okinawa level scroll — brush display
            "ShipporiMincho-Latin.ttf", // Okinawa level scroll — body serif
        };

        private const string GlobalFontFile = "mikey_ui.otf";

        [Test]
        public void GlobalMikeyFont_IsDeclaredExactlyOnce_InThemeUss()
        {
            string uiRoot = Path.Combine(UnityEngine.Application.dataPath, "UI");
            Assert.IsTrue(Directory.Exists(uiRoot), $"Expected {uiRoot} to exist.");

            int globalDeclarations = 0;
            foreach (string path in Directory.GetFiles(uiRoot, "*.uss", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);
                string file = Path.GetFileName(path);

                var declared = System.Text.RegularExpressions.Regex
                    .Matches(source, "-unity-font-definition\\s*:\\s*url\\(\"([^\"]+)\"\\)")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => Path.GetFileName(m.Groups[1].Value))
                    .ToList();

                // Every declaration must be the url("...") form, or this test
                // silently stops seeing which face a rule actually names.
                Assert.AreEqual(CountOccurrences(source, "-unity-font-definition"), declared.Count,
                    $"'{file}' declares a font in a form this test cannot read — write it as -unity-font-definition: url(\"/Assets/...\").");

                foreach (string font in declared)
                {
                    if (font == GlobalFontFile)
                    {
                        globalDeclarations++;
                        Assert.AreEqual("theme.uss", file,
                            $"'{file}' restates the global app font — theme.uss's '.mikey-app' is the sole authoritative source, and every screen inherits from it.");
                        continue;
                    }

                    CollectionAssert.Contains(IntentionalLocalFonts, font,
                        $"'{file}' introduces the typeface '{font}', which is not on the intentional-faces allowlist in this test. A new face is a design decision: add it here with the component it belongs to, or use the inherited app font.");
                }
            }

            Assert.AreEqual(1, globalDeclarations, $"Expected exactly one '{GlobalFontFile}' declaration across all of Assets/UI (in theme.uss).");

            string themeUss = File.ReadAllText("Assets/UI/theme.uss");
            StringAssert.Contains(GlobalFontFile, themeUss);
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
