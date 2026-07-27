using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Combine.Tests
{
    /// <summary>
    /// Contract for the Combine results screen's progression actions ("START LVL 1"
    /// / "Retry Assessment"): they exist as real, controller-bound (NOT "go-")
    /// buttons in the ready state, and the controller wires them through the
    /// forward-only Advance / navigator as specified — verified by reading the
    /// source, mirroring CombineScreenUxmlTests.DevControls_AreGatedToEditorOrDevelopmentBuild_InController.
    /// </summary>
    public class CombineProgressionTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string ControllerPath = "Assets/UI/Combine/CombineScreenController.cs";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        [Test]
        public void ReadyState_HasStartLevel1AndRetryAssessmentActions_NotGoNavigators()
        {
            var root = BuildTree();
            var ready = root.Q<VisualElement>("combine-ready");
            Assert.IsNotNull(ready, "Expected the 'combine-ready' state.");

            var startLevel1 = ready.Q<Button>("combine-start-lvl1");
            Assert.IsNotNull(startLevel1, "Expected a 'combine-start-lvl1' Button in the ready state.");
            Assert.AreEqual("START LVL 1", startLevel1.text);
            Assert.IsFalse(startLevel1.name.StartsWith("go-"),
                "'combine-start-lvl1' must be controller-bound, not a static 'go-' navigator.");
            Assert.IsTrue(startLevel1.ClassListContains("icon-btn"),
                "'combine-start-lvl1' must use the reusable '.icon-btn' touch-target class.");

            var retry = ready.Q<Button>("combine-retry-assessment");
            Assert.IsNotNull(retry, "Expected a 'combine-retry-assessment' Button in the ready state.");
            Assert.AreEqual("Retry Assessment", retry.text);
            Assert.IsFalse(retry.name.StartsWith("go-"),
                "'combine-retry-assessment' must be controller-bound, not a static 'go-' navigator.");
            Assert.IsTrue(retry.ClassListContains("icon-btn"),
                "'combine-retry-assessment' must use the reusable '.icon-btn' touch-target class.");
        }

        [Test]
        public void ReadyState_StillHasReturnHomeAction()
        {
            var root = BuildTree();
            var ready = root.Q<VisualElement>("combine-ready");
            Assert.IsNotNull(ready.Q<Button>("go-menu"),
                "The ready state must keep its 'go-menu' Return Home exit alongside the new progression actions.");
        }

        [Test]
        public void StartLevel1_AdvancesToCombineCompletedThenLevel1Unlocked_AndOpensMap()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private void OnStartLevel1()", source);
            StringAssert.Contains("_progress?.Advance(TutorialProgressState.CombineCompleted);", source);
            StringAssert.Contains("_progress?.Advance(TutorialProgressState.Level1Unlocked);", source);
            StringAssert.Contains("_navigator?.Show(\"map\");", source);
        }

        [Test]
        public void RetryAssessment_OnlyResetsBeforeCombineCompleted_AndReturnsToCombineIntro()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private void OnRetryAssessment()", source);
            StringAssert.Contains("_progress.State <= TutorialProgressState.CombineCompleted", source,
                "Retry Assessment must not silently regress progress already made beyond Combine.");
            StringAssert.Contains("_progress.SetState(TutorialProgressState.IntroCompleted);", source);
            StringAssert.Contains("_navigator?.Show(\"combineIntro\");", source);
        }

        [Test]
        public void BindWhenReady_WiresBothNewProgressionButtons()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("BindButton(root, \"combine-start-lvl1\", OnStartLevel1);", source);
            StringAssert.Contains("BindButton(root, \"combine-retry-assessment\", OnRetryAssessment);", source);
        }
    }
}
