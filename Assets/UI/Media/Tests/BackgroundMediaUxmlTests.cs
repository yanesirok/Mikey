using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Media.Tests
{
    /// <summary>
    /// Structural contract for the background-media layer added to the four
    /// MVP visual-assets screens in MikeyApp.uxml: each screen's existing
    /// full-bleed "&lt;x&gt;-bg" layer gets exactly one ".bg-media" element (the
    /// runtime video/image target), still outside .safe-area-content, and
    /// each screen keeps a legibility scrim so foreground content stays
    /// readable over the background media.
    /// </summary>
    public class BackgroundMediaUxmlTests
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

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        [TestCase("title", "title-bg", "title-bg-media")]
        [TestCase("menu", "home-bg", "home-bg-media")]
        [TestCase("combine", "combine-bg", "combine-bg-media")]
        [TestCase("map", "map-bg", "map-bg-media")]
        public void Screen_HasBgMediaElement_InsideBackgroundLayer_OutsideSafeArea(
            string screenId, string bgClass, string mediaName)
        {
            var root = BuildTree();
            var screen = root.Q<VisualElement>(screenId);
            Assert.IsNotNull(screen, $"Expected a screen named '{screenId}'.");

            var bg = screen.Q<VisualElement>(className: bgClass);
            Assert.IsNotNull(bg, $"Expected a '.{bgClass}' full-bleed layer on '{screenId}'.");

            var media = bg.Q<VisualElement>(mediaName);
            Assert.IsNotNull(media, $"Expected a '{mediaName}' element inside '.{bgClass}'.");
            Assert.IsTrue(media.ClassListContains("bg-media"),
                $"'{mediaName}' must carry the reusable '.bg-media' class.");
            Assert.IsNull(NearestSafeAreaAncestor(media),
                $"'{mediaName}' must not be a descendant of .safe-area-content (it is a full-bleed background).");
        }

        [TestCase("title", "title-scrim")]
        [TestCase("menu", "home-scrim")]
        [TestCase("combine", "combine-scrim")]
        [TestCase("map", "map-scrim")]
        public void Screen_HasLegibilityScrim_OutsideSafeArea(string screenId, string scrimClass)
        {
            var root = BuildTree();
            var screen = root.Q<VisualElement>(screenId);
            Assert.IsNotNull(screen, $"Expected a screen named '{screenId}'.");

            var scrim = screen.Q<VisualElement>(className: scrimClass);
            Assert.IsNotNull(scrim, $"Expected a '.{scrimClass}' legibility layer on '{screenId}'.");
            Assert.IsNull(NearestSafeAreaAncestor(scrim),
                $"'.{scrimClass}' must not be a descendant of .safe-area-content.");
        }
    }
}
