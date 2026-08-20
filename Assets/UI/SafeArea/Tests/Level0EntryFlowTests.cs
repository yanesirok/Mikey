using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// End-to-end entry-flow contract for Level 0: pressing "Begin Level 0" from
    /// Okinawa's LVL0 marker must land on the Combine checklist FIRST — never
    /// directly on Camera Test. Fixes a regression where combineIntro's primary
    /// CTA routed straight to "camTest", skipping the checklist entirely (so the
    /// player never saw Push-Ups/Squats/Wall Sit/Yoko-Geri as locked, and
    /// completing Camera Test dumped them onto a checklist they'd never seen
    /// before). Consolidates the whole chain in one place; the individual legs
    /// are also covered where they naturally live (OkinawaMapControllerSourceTests,
    /// CombineIntroScreenUxmlTests, CombineProgressionTests, CameraTestUxmlTests,
    /// Level0ProgressionStoreTests).
    /// </summary>
    public class Level0EntryFlowTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string OkinawaControllerPath = "Assets/UI/Map/OkinawaMapController.cs";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        [Test]
        public void FullChain_OkinawaLvl0_ToCombineIntro_ToCombine_NeverToCamTestDirectly()
        {
            // Leg 1: Okinawa's LVL0 CTA routes to combineIntro (unchanged by this fix).
            string okinawaSource = File.ReadAllText(OkinawaControllerPath);
            StringAssert.Contains("CombineIntroScreenId = \"combineIntro\";", okinawaSource);
            StringAssert.Contains("_navigator?.Show(CombineIntroScreenId);", okinawaSource);

            var root = BuildTree();

            // Leg 2 (the fix): combineIntro's primary CTA routes to "combine" — the
            // Combine checklist — not "camTest".
            var combineIntro = root.Q<VisualElement>("combineIntro");
            Assert.IsNotNull(combineIntro, "Expected a 'combineIntro' screen.");
            Assert.IsNotNull(combineIntro.Q<Button>("go-combine"),
                "combineIntro's primary CTA must route to 'combine' (the Level 0 checklist).");
            Assert.IsNull(combineIntro.Q<Button>("go-camTest"),
                "Level 0 entry must never route directly to camTest — it must land on the checklist first.");

            var combine = root.Q<VisualElement>("combine");
            Assert.IsNotNull(combine, "'go-combine' must target an existing 'combine' screen.");
            Assert.IsTrue(combine.ClassListContains("screen"), "'combine' target must be a screen.");
        }

        [Test]
        public void NothingElseInTheApp_RoutesDirectlyToCamTest()
        {
            // camTest is reached from exactly one place: the Combine checklist's
            // dynamic, controller-bound START action (Mikey.UI.Combine.
            // CombineScreenController.DestinationFor) — never a static "go-camTest"
            // navigator anywhere in the production markup.
            var root = BuildTree();
            var matches = root.Query<VisualElement>(name: "go-camTest").ToList();
            Assert.IsEmpty(matches, "No screen may auto-route directly to camTest via a 'go-camTest' navigator.");
        }
    }
}
