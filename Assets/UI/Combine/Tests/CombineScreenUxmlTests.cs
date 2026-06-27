using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Combine.Tests
{
    /// <summary>
    /// Structural contract for the "combine" screen in MikeyApp.uxml: the
    /// full-bleed background stays outside the safe-area wrapper, the content and
    /// all four mock state views stay inside it, and the dev switcher exposes the
    /// names the controller binds to.
    /// </summary>
    public class CombineScreenUxmlTests
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

        private static VisualElement CombineScreen(VisualElement root)
        {
            var screen = root.Q<VisualElement>("combine");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'combine'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'combine' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        [Test]
        public void CombineScreen_HasExactlyOneSafeAreaContent()
        {
            var screen = CombineScreen(BuildTree());
            Assert.AreEqual(1, screen.Query<VisualElement>(className: "safe-area-content").ToList().Count);
        }

        [Test]
        public void Background_IsFullBleed_OutsideSafeAreaContent()
        {
            var screen = CombineScreen(BuildTree());
            var bg = screen.Q<VisualElement>(className: "combine-bg");
            Assert.IsNotNull(bg, "Expected a .combine-bg full-bleed layer.");
            Assert.IsNull(NearestSafeAreaAncestor(bg),
                ".combine-bg must not be inside .safe-area-content.");
        }

        [Test]
        public void ContentAndStateViews_AreInsideSafeAreaContent()
        {
            var screen = CombineScreen(BuildTree());
            foreach (var name in new[] { "combine-loading", "combine-empty", "combine-ready", "combine-error" })
            {
                var el = screen.Q<VisualElement>(name);
                Assert.IsNotNull(el, $"Expected a state view named '{name}'.");
                Assert.IsNotNull(NearestSafeAreaAncestor(el),
                    $"'{name}' must be inside .safe-area-content.");
            }

            var content = screen.Q<VisualElement>(className: "combine-content");
            Assert.IsNotNull(content, "Expected a .combine-content column.");
            Assert.IsNotNull(NearestSafeAreaAncestor(content),
                ".combine-content must be inside .safe-area-content.");
        }

        [Test]
        public void ReadyState_HasItemsContainer()
        {
            var screen = CombineScreen(BuildTree());
            Assert.IsNotNull(screen.Q<VisualElement>("combine-items"),
                "Ready state must expose a 'combine-items' container for the controller to fill.");
        }

        [Test]
        public void DevSwitcher_ExposesAllControllerBoundButtons()
        {
            var screen = CombineScreen(BuildTree());
            foreach (var name in new[]
            {
                "combine-dev-loading", "combine-dev-empty", "combine-dev-ready",
                "combine-dev-error", "combine-dev-cycle", "combine-retry",
            })
            {
                Assert.IsNotNull(screen.Q<Button>(name), $"Expected a Button named '{name}'.");
            }
        }
    }
}
