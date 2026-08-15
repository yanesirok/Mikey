using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Media.Tests
{
    /// <summary>
    /// Structural contract for the background-media layer on the two screens
    /// with a BackgroundMediaController-bound video in MikeyApp.uxml (menu/
    /// combine): each screen's existing full-bleed "&lt;x&gt;-bg" layer gets
    /// exactly one ".bg-media" element (the runtime video target), still outside
    /// .safe-area-content, and each screen keeps a legibility scrim so
    /// foreground content stays readable over the background media. Logo Intro
    /// ("title") and Map ("map") deliberately have neither: Title is a static
    /// near-black background + the centered logo, nothing else; Map's reused
    /// artwork now lives directly inside its own pannable ".map-canvas" (see
    /// Assets/UI/Map/Map.uss), with its own ".map-stage__scrim" legibility
    /// layer, not the shared BackgroundMediaController media model.
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

        [TestCase("menu", "home-bg", "home-bg-media")]
        [TestCase("combine", "combine-bg", "combine-bg-media")]
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

        [TestCase("menu", "home-scrim")]
        [TestCase("combine", "combine-scrim")]
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
