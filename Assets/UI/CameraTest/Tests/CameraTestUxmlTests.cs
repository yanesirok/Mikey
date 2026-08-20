using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.CameraTest.Tests
{
    /// <summary>
    /// Structural contract for the rebuilt landscape "camTest" screen in
    /// MikeyApp.uxml: the full-bleed mock feed stays outside the safe-area
    /// wrapper, the HUD and bottom controls stay inside it, the local simulate
    /// control uses a stable (non-"go-") controller-bound name, the production
    /// completion route still targets the modern Combine screen, and the visible
    /// icons / action bar use the reusable, non-shrinking size + responsive classes.
    /// </summary>
    public class CameraTestUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/CameraTest/CameraTest.uss";
        private const string ControllerPath = "Assets/UI/CameraTest/CameraTestController.cs";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static VisualElement CamScreen(VisualElement root)
        {
            var screen = root.Q<VisualElement>("camTest");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'camTest'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'camTest' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        // 1
        [Test]
        public void CamTestScreen_ExistsExactlyOnce()
        {
            var root = BuildTree();
            Assert.AreEqual(1, root.Query<VisualElement>("camTest").ToList().Count,
                "There must be exactly one 'camTest' screen.");
        }

        // 2
        [Test]
        public void CamTest_HasExactlyOneSafeAreaContent()
        {
            var screen = CamScreen(BuildTree());
            Assert.AreEqual(1, screen.Query<VisualElement>(className: "safe-area-content").ToList().Count,
                "camTest must have exactly one .safe-area-content wrapper.");
        }

        // 3
        [Test]
        public void MockFeed_IsFullBleed_OutsideSafeAreaContent()
        {
            var screen = CamScreen(BuildTree());
            var feed = screen.Q<VisualElement>(className: "cam-feed");
            Assert.IsNotNull(feed, "Expected a .cam-feed full-bleed mock-camera layer.");
            Assert.IsNull(NearestSafeAreaAncestor(feed),
                ".cam-feed must not be inside .safe-area-content (it bleeds under device insets).");
        }

        // 4
        [Test]
        public void HudAndBottomControls_AreInsideSafeAreaContent()
        {
            var screen = CamScreen(BuildTree());
            foreach (var className in new[] { "cam-hud", "cam-live", "cam-stage", "cam-actionbar" })
            {
                var el = screen.Q<VisualElement>(className: className);
                Assert.IsNotNull(el, $"Expected a .{className} element on camTest.");
                Assert.IsNotNull(NearestSafeAreaAncestor(el),
                    $".{className} must be inside .safe-area-content.");
            }

            foreach (var name in new[] { "camera-simulate-rep", "camera-test-complete" })
            {
                var btn = screen.Q<Button>(name);
                Assert.IsNotNull(btn, $"Expected a Button named '{name}'.");
                Assert.IsNotNull(NearestSafeAreaAncestor(btn),
                    $"'{name}' must be inside .safe-area-content.");
            }
        }

        // 5 + 6
        [Test]
        public void CompleteButton_ExistsOnCamTest_AndTargetsTheCombineChecklistScreen()
        {
            var root = BuildTree();
            var screen = CamScreen(root);

            // Deliberately NOT a "go-" navigator: completing Camera Test also has
            // the side effect of marking Level 0's Camera Test complete (see
            // CameraTestController.OnCompleteClicked), so it needs an explicit
            // controller-bound handler rather than ScreenManager's generic swap.
            var done = screen.Q<Button>("camera-test-complete");
            Assert.IsNotNull(done, "camTest must keep its 'camera-test-complete' completion route.");
            Assert.IsFalse(done.name.StartsWith("go-"),
                "'camera-test-complete' must NOT be a bare 'go-' navigator — it has a progression side effect.");

            var combine = root.Q<VisualElement>("combine");
            Assert.IsNotNull(combine, "The Camera Test completion route must target an existing 'combine' screen.");
            Assert.IsTrue(combine.ClassListContains("screen"), "'combine' target must be a screen.");
        }

        // 7
        [Test]
        public void SimulateControl_HasStableNonNavigatorName()
        {
            var screen = CamScreen(BuildTree());
            var simulate = screen.Q<Button>("camera-simulate-rep");
            Assert.IsNotNull(simulate, "Expected the local simulate control named 'camera-simulate-rep'.");
            Assert.IsFalse(simulate.name.StartsWith("go-"),
                "The simulate control must NOT use a production 'go-' navigator name.");
        }

        // 10
        [Test]
        public void BothBottomControls_UseMinimumTouchTargetClass()
        {
            var screen = CamScreen(BuildTree());
            foreach (var name in new[] { "camera-simulate-rep", "camera-test-complete" })
            {
                var btn = screen.Q<Button>(name);
                Assert.IsNotNull(btn, $"Expected a Button named '{name}'.");
                Assert.IsTrue(btn.ClassListContains("tap-target-lg"),
                    $"'{name}' must carry the .tap-target-lg minimum touch-target class (>=56px high).");
                Assert.IsTrue(btn.ClassListContains("cam-btn"),
                    $"'{name}' must carry the shared .cam-btn responsive button class.");
            }
        }

        // 11 + 12
        [Test]
        public void VisibleIcons_UseReusableSizeClasses_ThatPreventShrinking()
        {
            var screen = CamScreen(BuildTree());

            // Every element carrying .cam-icon must also carry an explicit size variant.
            var icons = screen.Query<VisualElement>(className: "cam-icon").ToList();
            Assert.IsNotEmpty(icons, "Expected visible icons carrying the reusable .cam-icon class.");

            // The form glyph must carry .cam-icon (sized + non-shrinking).
            foreach (var name in new[] { "camera-form-glyph" })
            {
                var glyph = screen.Q<VisualElement>(name);
                Assert.IsNotNull(glyph, $"Expected a sized glyph named '{name}'.");
                Assert.IsTrue(glyph.ClassListContains("cam-icon"), $"'{name}' must carry .cam-icon.");
            }

            // .cam-icon must set flex-shrink:0 so the artwork is never collapsed.
            string uss = File.ReadAllText(UssPath);
            var block = Regex.Match(uss, @"\.cam-icon\s*\{[^}]*\}");
            Assert.IsTrue(block.Success, "CameraTest.uss must define a .cam-icon rule.");
            StringAssert.Contains("flex-shrink: 0", block.Value,
                ".cam-icon must set flex-shrink:0 so visible icons never shrink to nothing.");
        }

        // 13
        [Test]
        public void ActionContainer_UsesResponsiveClass()
        {
            var screen = CamScreen(BuildTree());
            var bar = screen.Q<VisualElement>(className: "cam-actionbar");
            Assert.IsNotNull(bar, "Expected the responsive .cam-actionbar action container.");

            // The responsive class must wrap (stack) rather than overflow on narrow widths.
            string uss = File.ReadAllText(UssPath);
            var block = Regex.Match(uss, @"\.cam-actionbar\s*\{[^}]*\}");
            Assert.IsTrue(block.Success, "CameraTest.uss must define a .cam-actionbar rule.");
            StringAssert.Contains("flex-wrap: wrap", block.Value,
                ".cam-actionbar must allow wrapping so bottom actions stack instead of overflowing.");
            StringAssert.Contains("max-width", block.Value,
                ".cam-actionbar must be width-capped and centered, not edge-to-edge.");
        }

        // Overflow-fix guard: the responsive buttons must NOT reintroduce the
        // width:100% basis that caused the original Done-button overflow, and must
        // keep min-width:0 so a flex child can shrink instead of pushing past the edge.
        [Test]
        public void ResponsiveButtons_DoNotUseFullWidthBasis()
        {
            string uss = File.ReadAllText(UssPath);
            var block = Regex.Match(uss, @"\.cam-btn\s*\{[^}]*\}");
            Assert.IsTrue(block.Success, "CameraTest.uss must define a .cam-btn rule.");
            StringAssert.DoesNotContain("width: 100%", block.Value,
                ".cam-btn must not use width:100% (the original Done-button overflow cause).");
            StringAssert.Contains("min-width: 0", block.Value,
                ".cam-btn must set min-width:0 so a flex child can shrink rather than overflow.");
        }

        // No real camera / ML / networking leaked into the mock controller.
        [Test]
        public void Controller_HasNoRealCameraOrNetworkingApis()
        {
            string src = File.ReadAllText(ControllerPath);
            foreach (var banned in new[] { "WebCamTexture", "UnityWebRequest", "Permission", "NativeCamera" })
            {
                StringAssert.DoesNotContain(banned, src,
                    $"CameraTestController must stay mock-only (found '{banned}').");
            }
        }

        [Test]
        public void Controller_UnbindsSimulateButtonOnDisable()
        {
            string src = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private Button _simulate", src,
                "CameraTestController must retain the simulate button reference for teardown.");
            StringAssert.Contains("_simulate.clicked -= _model.SimulateRep", src,
                "CameraTestController must remove the simulate callback on disable.");
            StringAssert.Contains("_simulate.clicked += _model.SimulateRep", src,
                "CameraTestController must bind the simulate callback explicitly once during setup.");
        }

        [Test]
        public void Controller_UnbindsCompleteButtonOnDisable()
        {
            string src = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private Button _complete", src,
                "CameraTestController must retain the complete button reference for teardown.");
            StringAssert.Contains("_complete.clicked -= OnCompleteClicked", src,
                "CameraTestController must remove the complete callback on disable.");
            StringAssert.Contains("_complete.clicked += OnCompleteClicked", src,
                "CameraTestController must bind the complete callback explicitly once during setup.");
        }

        [Test]
        public void CompleteButton_MarksLevel0CameraTestComplete_ThenReturnsToCombine()
        {
            string src = File.ReadAllText(ControllerPath);
            StringAssert.Contains("_level0?.Complete(Level0Test.CameraTest);", src,
                "Completing Camera Test must mark Level 0's Camera Test complete.");
            StringAssert.Contains("_navigator?.Show(\"combine\");", src,
                "Completing Camera Test must return to the Combine checklist, never auto-start the next test.");
        }
    }
}
