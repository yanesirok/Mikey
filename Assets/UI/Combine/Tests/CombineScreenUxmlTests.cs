using System.Collections.Generic;
using System.IO;
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

        [Test]
        public void InteractiveIconControls_UseReusableTouchTargetClass()
        {
            var screen = CombineScreen(BuildTree());
            // Every interactive control on the screen must carry the reusable
            // .icon-btn touch-target class (>= 48x48 logical, centered, no-shrink).
            foreach (var name in new[]
            {
                "combine-dev-loading", "combine-dev-empty", "combine-dev-ready",
                "combine-dev-error", "combine-dev-cycle", "combine-retry",
            })
            {
                var button = screen.Q<Button>(name);
                Assert.IsNotNull(button, $"Expected a Button named '{name}'.");
                Assert.IsTrue(button.ClassListContains("icon-btn"),
                    $"'{name}' must use the reusable .icon-btn class.");
            }
        }

        [Test]
        public void DevSwitcherButtons_UseReusableVisibleIconSize()
        {
            var screen = CombineScreen(BuildTree());
            foreach (var name in new[]
            {
                "combine-dev-loading", "combine-dev-empty", "combine-dev-ready",
                "combine-dev-error", "combine-dev-cycle",
            })
            {
                var button = screen.Q<Button>(name);
                Assert.IsNotNull(button, $"Expected a Button named '{name}'.");
                Assert.IsTrue(button.ClassListContains("icon-24"),
                    $"'{name}' must use the reusable .icon-24 visible-icon size variant.");
            }
        }

        [Test]
        public void ReadyState_HasProductionReturnHomeButton()
        {
            var screen = CombineScreen(BuildTree());
            var ready = screen.Q<VisualElement>("combine-ready");
            Assert.IsNotNull(ready, "Expected the 'combine-ready' success state.");

            // The production exit lives inside the ready state (only reachable on success).
            var home = ready.Q<Button>("go-menu");
            Assert.IsNotNull(home, "Ready state must contain a production 'go-menu' button.");
            Assert.IsNotNull(NearestSafeAreaAncestor(home),
                "'go-menu' must be inside .safe-area-content.");

            // Honest label: Level 1 is not implemented, so it must not claim to unlock it.
            StringAssert.DoesNotContain("Unlock Level 1", home.text,
                "Return-Home button must not be labelled 'Unlock Level 1'.");

            // Reusable >=48px touch target, consistent with the Combine icon-button kit.
            Assert.IsTrue(home.ClassListContains("icon-btn"),
                "'go-menu' must use the reusable .icon-btn touch-target class.");
        }

        [Test]
        public void DevControls_AreGatedToEditorOrDevelopmentBuild_InController()
        {
            // The dev switcher must remain present in markup but enabled only in the
            // Editor or a Development Build. Guard the controller's compile-time gate
            // so a refactor can't silently ship the switcher to production players.
            const string ControllerPath = "Assets/UI/Combine/CombineScreenController.cs";
            Assert.IsTrue(File.Exists(ControllerPath), $"Expected controller at {ControllerPath}.");
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("#if UNITY_EDITOR || DEVELOPMENT_BUILD", source,
                "CombineScreenController must gate dev controls behind UNITY_EDITOR || DEVELOPMENT_BUILD.");
        }

        [Test]
        public void Controller_UnbindsLocalButtonCallbacksOnDisable()
        {
            const string ControllerPath = "Assets/UI/Combine/CombineScreenController.cs";
            Assert.IsTrue(File.Exists(ControllerPath), $"Expected controller at {ControllerPath}.");
            string source = File.ReadAllText(ControllerPath);

            StringAssert.Contains("_buttonBindings", source,
                "CombineScreenController must retain local button bindings for teardown.");
            StringAssert.Contains("UnbindButtons();", source,
                "CombineScreenController must unregister local button callbacks on disable.");
            StringAssert.Contains("button.clicked -= _callback", source,
                "CombineScreenController must remove each previously-added clicked callback.");
        }
    }
}
