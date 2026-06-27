using System.Collections.Generic;
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

        [Test]
        public void HasExactlyEightScreens()
        {
            Assert.AreEqual(8, ByClass(BuildTree(), "screen").Count);
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
