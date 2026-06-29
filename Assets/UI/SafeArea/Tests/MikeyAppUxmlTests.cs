using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Verifies the MikeyApp.uxml structural contract: exactly ten production
    /// screens (the six post-consolidation entry/Combine screens plus the
    /// Techniques hub, Practice slice and Map progression screen), one dedicated
    /// ".safe-area-content" per screen, full-bleed elements outside the wrappers,
    /// the mapped foreground elements inside them, the Title CTA route to Intro,
    /// and the untouched Home → Combine flow.
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

        // The ten production screens: the six post-consolidation entry/Combine
        // screens plus the Techniques lesson hub, the Practice training slice and
        // the Map progression screen.
        private static readonly string[] ExpectedScreenIds =
            { "title", "intro", "menu", "combineIntro", "camTest", "combine", "techniques", "practice", "map", "profile" };

        // 1
        [Test]
        public void HasExactlyTenScreens()
        {
            Assert.AreEqual(10, ByClass(BuildTree(), "screen").Count);
        }

        // 2
        [Test]
        public void ScreenIds_AreExactlyTheTenProductionScreens()
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

        // 4
        [Test]
        public void GoIntroNavigator_ExistsOnTitleScreen()
        {
            var root = BuildTree();
            var title = root.Q<VisualElement>("title");
            Assert.IsNotNull(title, "Expected a 'title' screen.");
            var goIntroOnTitle = title.Query<VisualElement>(name: "go-intro").ToList();
            Assert.AreEqual(1, goIntroOnTitle.Count,
                "Title must carry exactly one 'go-intro' production CTA.");

            // ...and there must be no stray 'go-intro' elsewhere in production.
            var goIntroAnywhere = root.Query<VisualElement>(name: "go-intro").ToList();
            Assert.AreEqual(1, goIntroAnywhere.Count,
                "'go-intro' must exist only as the Title CTA (no leftover Splash navigator).");
        }

        // 5
        [Test]
        public void GoIntroNavigator_TargetsExistingIntroScreen()
        {
            var root = BuildTree();
            // ScreenManager maps a 'go-<id>' navigator to the screen named <id>.
            Assert.IsNotEmpty(root.Query<VisualElement>(name: "go-intro").ToList(),
                "Expected a 'go-intro' navigator.");
            var intro = root.Q<VisualElement>("intro");
            Assert.IsNotNull(intro, "'go-intro' must target an existing 'intro' screen.");
            Assert.IsTrue(intro.ClassListContains("screen"), "'intro' target must be a screen.");
        }

        // 8
        [Test]
        public void TitleCta_UsesMinimumTouchTargetClass()
        {
            var cta = BuildTree().Q<VisualElement>("title")?.Q<Button>("go-intro");
            Assert.IsNotNull(cta, "Title must expose its CTA as a 'go-intro' Button.");
            Assert.IsTrue(cta.ClassListContains("tap-target"),
                "Title CTA must carry the .tap-target minimum-touch-target class.");
        }

        // 9
        [Test]
        public void TitleCta_Icon_UsesExplicitNonShrinkingSizingClass()
        {
            var title = BuildTree().Q<VisualElement>("title");
            var icons = ByClass(title, "title-icon");
            Assert.IsNotEmpty(icons,
                "Title CTA must include a visible .title-icon arrow with explicit sizing.");
            foreach (var icon in icons)
                Assert.IsTrue(icon.ClassListContains("title-icon--cta"),
                    "Title CTA icon must use the explicit non-shrinking .title-icon--cta sizing modifier.");
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

        // 17 — Home → Combine flow remains unchanged.
        [Test]
        public void HomeToCombineFlow_RemainsUnchanged()
        {
            var root = BuildTree();

            // menu → combineIntro
            var menu = root.Q<VisualElement>("menu");
            Assert.IsNotNull(menu, "Expected a 'menu' (Home) screen.");
            Assert.IsNotNull(menu.Q<Button>("go-combineIntro"),
                "Home must keep its 'go-combineIntro' CTA.");

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
            foreach (var className in new[] { "title-bg", "cam-feed", "combine-bg", "intro-bg", "tq-bg", "pr-feed", "map-bg", "profile-bg" })
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
            foreach (var className in new[] { "title-block", "title-name", "content", "cam-actionbar", "cam-live", "skip", "combine-content",
                "tq-layout", "tq-lessons", "tq-actionbar", "pr-hud", "pr-actionbar", "pr-stage",
                "map-layout", "map-route", "map-dock", "profile-dashboard", "profile-identity", "profile-stats", "profile-achievements", "profile-activity", "profile-dock" })
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

        // 30 — existing Title → Intro → Home entry route remains unchanged.
        [Test]
        public void TitleToIntroToHomeRoute_RemainsUnchanged()
        {
            var root = BuildTree();

            // title → intro (go-intro lives on the Title screen, targets the intro screen)
            var title = root.Q<VisualElement>("title");
            Assert.IsNotNull(title, "Expected a 'title' screen.");
            Assert.IsNotNull(title.Q<Button>("go-intro"), "Title must keep its 'go-intro' CTA.");
            Assert.IsNotNull(root.Q<VisualElement>("intro"), "'go-intro' must target an existing 'intro' screen.");

            // intro → menu (Enter the Dojo / Skip both navigate Home)
            var intro = root.Q<VisualElement>("intro");
            Assert.IsNotEmpty(intro.Query<VisualElement>(name: "go-menu").ToList(),
                "Intro must keep a 'go-menu' route to Home.");
            var menu = root.Q<VisualElement>("menu");
            Assert.IsNotNull(menu, "'go-menu' must target an existing 'menu' (Home) screen.");
            Assert.IsTrue(menu.ClassListContains("screen"), "'menu' target must be a screen.");
        }
    }
}
