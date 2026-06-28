using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Practice.Tests
{
    /// <summary>
    /// Structural contract for the landscape Practice training slice (the
    /// "practice" screen) in MikeyApp.uxml: exactly one screen with one
    /// safe-area wrapper, a full-bleed training feed outside that wrapper, the
    /// lesson name/objective + a large focal pose graphic + score/cue/progress
    /// readouts inside it, a working go-techniques exit, controller-bound local
    /// actions (NOT go- navigators), and reusable touch/icon/wrapping classes.
    /// </summary>
    public class PracticeScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string NavPrefix = "go-";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        private static VisualElement Practice(VisualElement root)
        {
            var screen = root.Q<VisualElement>("practice");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'practice'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'practice' must carry the .screen class.");
            return screen;
        }

        private static VisualElement NearestSafeAreaAncestor(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
                if (p.ClassListContains("safe-area-content"))
                    return p;
            return null;
        }

        // 2
        [Test]
        public void Practice_ExistsAsExactlyOneScreen()
        {
            var count = BuildTree().Query<VisualElement>(className: "screen").ToList()
                .Count(s => s.name == "practice");
            Assert.AreEqual(1, count, "There must be exactly one screen named 'practice'.");
        }

        // 4
        [Test]
        public void Practice_HasExactlyOneSafeAreaContent()
        {
            var count = Practice(BuildTree()).Query<VisualElement>(className: "safe-area-content").ToList().Count;
            Assert.AreEqual(1, count, $"'practice' must contain exactly one .safe-area-content (found {count}).");
        }

        // 5
        [Test]
        public void Feed_IsFullBleed_OutsideSafeArea()
        {
            var feed = Practice(BuildTree()).Q<VisualElement>(className: "pr-feed");
            Assert.IsNotNull(feed, "Expected a .pr-feed full-bleed training background.");
            Assert.IsNull(NearestSafeAreaAncestor(feed), ".pr-feed must not be inside .safe-area-content.");
        }

        // 6
        [Test]
        public void Hud_IsInsideSafeArea()
        {
            var hud = Practice(BuildTree()).Q<VisualElement>(className: "pr-hud");
            Assert.IsNotNull(hud, "Expected a .pr-hud foreground HUD.");
            Assert.IsNotNull(NearestSafeAreaAncestor(hud), ".pr-hud must be inside .safe-area-content.");
        }

        // Required HUD content: lesson title/objective, large focal graphic, score, cue, progress.
        [Test]
        public void Hud_ContainsLessonObjectiveFocalScoreCueAndProgress()
        {
            var screen = Practice(BuildTree());
            Assert.IsNotNull(screen.Q<VisualElement>(className: "pr-title"), "Expected the lesson title.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "pr-objective"), "Expected the lesson objective/instruction.");
            Assert.IsNotNull(screen.Q<VisualElement>(className: "pr-figure"), "Expected the large focal pose graphic (.pr-figure).");
            Assert.IsNotNull(screen.Q<Label>("practice-score"), "Expected the bound 'practice-score' readout.");
            Assert.IsNotNull(screen.Q<Label>("practice-cue-text"), "Expected the bound 'practice-cue-text' instruction.");
            Assert.IsNotNull(screen.Q<VisualElement>("practice-progress"), "Expected the bound 'practice-progress' indicator.");
            Assert.GreaterOrEqual(screen.Query<VisualElement>(className: "pr-pip").ToList().Count, 4,
                "Progress indicator must expose its keyframe pips.");
        }

        // 23 — the back action targets the Techniques hub.
        [Test]
        public void BackAction_TargetsTechniques()
        {
            var root = BuildTree();
            var back = Practice(root).Q<Button>("go-techniques");
            Assert.IsNotNull(back, "Practice must expose a 'go-techniques' back action.");
            Assert.AreEqual("techniques", back.name.Substring(NavPrefix.Length));
            Assert.IsTrue(root.Q<VisualElement>("techniques").ClassListContains("screen"),
                "'go-techniques' must target the existing 'techniques' screen.");
        }

        // Local mock actions are controller-bound, NOT go- navigators.
        [Test]
        public void LocalActions_AreNotNavigators()
        {
            var screen = Practice(BuildTree());
            foreach (var name in new[] { "practice-action", "practice-complete" })
            {
                var ctrl = screen.Q<Button>(name);
                Assert.IsNotNull(ctrl, $"Expected the local action '{name}'.");
                Assert.IsFalse(ctrl.name.StartsWith(NavPrefix),
                    $"Local state-changing action '{name}' must not use a 'go-' navigator name.");
            }
        }

        // 25 — interactive controls use the reusable minimum touch-target class.
        [Test]
        public void InteractiveControls_UseTouchTargetClass()
        {
            var screen = Practice(BuildTree());
            foreach (var name in new[] { "go-techniques", "practice-action", "practice-complete" })
            {
                var ctrl = screen.Q<VisualElement>(name);
                Assert.IsNotNull(ctrl, $"Expected interactive control '{name}'.");
                Assert.IsTrue(ctrl.ClassListContains("tap-target-lg"),
                    $"Control '{name}' must use the '.tap-target-lg' (>=56x56) touch-target class.");
            }
        }

        // 26 + 27 — visible HUD/action icons use explicit reusable size classes on the non-shrinking base.
        [Test]
        public void VisibleIcons_UseExplicitSizeClasses_OnNonShrinkingBase()
        {
            var screen = Practice(BuildTree());
            var icons = screen.Query<VisualElement>(className: "pr-icon").ToList();
            Assert.GreaterOrEqual(icons.Count, 4, "Expected several .pr-icon visible icons (HUD + actions).");

            var actionIcons = screen.Query<VisualElement>(className: "pr-icon--action").ToList();
            Assert.IsNotEmpty(actionIcons, "Action icons must use the explicit '.pr-icon--action' size class.");
            foreach (var icon in actionIcons)
                Assert.IsTrue(icon.ClassListContains("pr-icon"),
                    "Action icons must use the non-shrinking '.pr-icon' base class.");

            Assert.IsNotEmpty(screen.Query<VisualElement>(className: "pr-icon--hud").ToList(),
                "HUD status icons must use the explicit '.pr-icon--hud' size class.");
        }

        // 28 — the action container uses the responsive wrapping class.
        [Test]
        public void ActionContainer_UsesResponsiveWrappingClass()
        {
            var bar = Practice(BuildTree()).Q<VisualElement>(className: "pr-actionbar");
            Assert.IsNotNull(bar, "Expected a '.pr-actionbar' responsive (wrapping) action container.");
        }

        // 29 — sibling action buttons must NOT reintroduce the overflow-prone width:100% global .btn.
        [Test]
        public void ActionButtons_DoNotUseFullWidthGlobalBtn()
        {
            var screen = Practice(BuildTree());
            foreach (var btn in screen.Query<Button>().ToList())
            {
                Assert.IsFalse(btn.ClassListContains("btn"),
                    $"Action button '{btn.name}' must not use the width:100% global '.btn' class " +
                    "(use the content-sized '.pr-btn' classes instead).");
            }
        }
    }
}
