using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Verifies the MikeyApp.uxml safe-area wrapping contract: one dedicated
    /// ".safe-area-content" per screen, full-bleed elements outside the wrappers,
    /// and the mapped foreground elements inside them.
    /// </summary>
    public class MikeyAppUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";

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

        // The seven production screens that survive the Combine flow consolidation.
        private static readonly string[] ExpectedScreenIds =
            { "splash", "intro", "title", "menu", "combineIntro", "camTest", "combine" };

        [Test]
        public void HasExactlySevenScreens()
        {
            Assert.AreEqual(7, ByClass(BuildTree(), "screen").Count);
        }

        [Test]
        public void ScreenIds_AreExactlyTheSevenProductionScreens()
        {
            var ids = ByClass(BuildTree(), "screen").Select(s => s.name).ToList();
            CollectionAssert.AreEquivalent(ExpectedScreenIds, ids);
        }

        [Test]
        public void LegacyCombineResultsScreen_DoesNotExist()
        {
            Assert.IsNull(BuildTree().Q<VisualElement>("combineResults"),
                "Legacy 'combineResults' screen must be removed.");
        }

        [Test]
        public void LegacyGoCombineResultsNavigator_DoesNotExist()
        {
            Assert.IsNull(BuildTree().Q<VisualElement>("go-combineResults"),
                "Legacy 'go-combineResults' navigator must be removed.");
        }

        [Test]
        public void CamTest_RoutesToModernCombineScreen()
        {
            var root = BuildTree();
            var camTest = root.Q<VisualElement>("camTest");
            Assert.IsNotNull(camTest, "Expected a 'camTest' screen.");
            Assert.IsNotNull(camTest.Q<Button>("go-combine"),
                "camTest must route to the modern Combine screen via a 'go-combine' button.");
        }

        [Test]
        public void ModernCombineScreen_Exists()
        {
            var combine = BuildTree().Q<VisualElement>("combine");
            Assert.IsNotNull(combine, "Expected the modern 'combine' screen.");
            Assert.IsTrue(combine.ClassListContains("screen"), "'combine' must carry the .screen class.");
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

        [Test]
        public void RemovedLegacySelectors_AreNotReferencedByUxml()
        {
            string text = File.ReadAllText(UxmlPath);
            foreach (var selector in new[] { "combineResults", "go-combineResults", "class=\"bar", "class=\"fill" })
            {
                StringAssert.DoesNotContain(selector, text,
                    $"MikeyApp.uxml must not reference the removed legacy selector '{selector}'.");
            }
        }

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

        [Test]
        public void FullBleedElementsAreNotInsideSafeAreaContent()
        {
            var root = BuildTree();
            foreach (var className in new[] { "got-sun", "cam-feed", "combine-bg" })
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
            foreach (var className in new[] { "title-block", "content", "cam-actions", "live", "skip", "splash-title", "combine-content" })
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
    }
}
