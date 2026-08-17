using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for the final cloud PNG asset integration and cloud overlay
    /// structure (Map Pass 3B): the 4 supplied final PNGs exist on disk and
    /// are referenced by the correct cloud class in Map.uss, the overlay
    /// lives outside the pan/zoom-transformed canvas, and resting cloud
    /// elements are non-picking decoration. "Source PNGs not modified" is
    /// enforced the same way as every other final map asset in this
    /// project — by process (never editing bytes under
    /// Assets/UI/Media/Images/**), verified per-pass via byte-size
    /// comparison, matching MapMarkerAssetsTests' existence-only convention
    /// for the same reason (no established byte-hash test pattern in this
    /// project to diverge from).
    /// </summary>
    public class MapCloudAssetsTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string CloudsRoot = "Assets/UI/Media/Images/Map/Clouds";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        // ---------- final assets exist on disk ----------

        [TestCase("cloud_left_01.png")]
        [TestCase("cloud_left_02.png")]
        [TestCase("cloud_right_01.png")]
        [TestCase("cloud_bottom_01.png")]
        public void FinalCloudAsset_ExistsOnDisk(string fileName)
        {
            string path = $"{CloudsRoot}/{fileName}";
            Assert.IsTrue(File.Exists(path), $"Expected the final cloud asset at {path}.");
        }

        // ---------- each cloud class references the correct final PNG ----------

        [TestCase(".map-cloud--left-01 {", "cloud_left_01.png")]
        [TestCase(".map-cloud--left-02 {", "cloud_left_02.png")]
        [TestCase(".map-cloud--right-01 {", "cloud_right_01.png")]
        [TestCase(".map-cloud--bottom-01 {", "cloud_bottom_01.png")]
        public void CloudClass_ReferencesItsFinalAsset(string selector, string expectedFileName)
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, selector);
            Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
            StringAssert.Contains(expectedFileName, block);
        }

        [Test]
        public void NoUssShapesOrGeneratedArt_ReplaceTheCloudPngs()
        {
            // "Do NOT... replace with USS shapes" — each cloud class must be
            // an image reference, not a solid-color/gradient/border fake.
            string uss = File.ReadAllText(UssPath);
            foreach (var selector in new[] { ".map-cloud--left-01 {", ".map-cloud--left-02 {", ".map-cloud--right-01 {", ".map-cloud--bottom-01 {" })
            {
                string block = ExtractRuleBlock(uss, selector);
                Assert.IsNotNull(block);
                StringAssert.Contains("background-image: url(", block);
            }
        }

        // ---------- structure: overlay lives outside the transformed canvas ----------

        [Test]
        public void JapanCloudLayer_IsASiblingOfMapStage_NotAChildOfThePanCanvas()
        {
            var root = BuildTree();
            var mapRoot = FindAncestorWithClass(root.Q<VisualElement>("map-stage"), "map-root");
            Assert.IsNotNull(mapRoot, "Expected 'map-stage' to live inside '.map-root'.");

            var cloudLayer = root.Q<VisualElement>("map-cloud-layer");
            Assert.IsNotNull(cloudLayer, "Expected a 'map-cloud-layer' element.");
            Assert.AreSame(mapRoot, cloudLayer.parent, "'map-cloud-layer' must be a direct child of '.map-root', a sibling of 'map-stage' — never nested inside 'pan-canvas'.");

            var canvas = root.Q<VisualElement>("map-canvas");
            Assert.IsNotNull(canvas);
            Assert.IsFalse(IsDescendantOf(cloudLayer, canvas), "'map-cloud-layer' must not be a descendant of the transformed 'map-canvas' — clouds must never pan/zoom with the map art.");
        }

        [Test]
        public void OkinawaCloudLayer_IsASiblingOfOkinawaStage_NotAChildOfThePanCanvas()
        {
            var root = BuildTree();
            var mapRoot = FindAncestorWithClass(root.Q<VisualElement>("okinawa-stage"), "map-root");
            Assert.IsNotNull(mapRoot, "Expected 'okinawa-stage' to live inside '.map-root'.");

            var cloudLayer = root.Q<VisualElement>("okinawa-cloud-layer");
            Assert.IsNotNull(cloudLayer, "Expected an 'okinawa-cloud-layer' element.");
            Assert.AreSame(mapRoot, cloudLayer.parent, "'okinawa-cloud-layer' must be a direct child of '.map-root', a sibling of 'okinawa-stage'.");

            var canvas = root.Q<VisualElement>("okinawa-canvas");
            Assert.IsNotNull(canvas);
            Assert.IsFalse(IsDescendantOf(cloudLayer, canvas), "'okinawa-cloud-layer' must not be a descendant of the transformed 'okinawa-canvas'.");
        }

        [Test]
        public void CloudLayer_DoesNotAlterMapStageOrCanvasGeometry()
        {
            // Regression: the cloud layer's own CSS must not touch the
            // pan-stage/pan-canvas rules markers/pan-zoom depend on.
            string uss = File.ReadAllText(UssPath);
            string stageBlock = ExtractRuleBlock(uss, ".pan-stage {");
            string canvasBlock = ExtractRuleBlock(uss, ".pan-canvas {");
            Assert.IsNotNull(stageBlock);
            Assert.IsNotNull(canvasBlock);
            StringAssert.DoesNotContain("map-cloud", stageBlock);
            StringAssert.DoesNotContain("map-cloud", canvasBlock);
        }

        // ---------- resting cloud elements are decorative, never click targets ----------

        [Test]
        public void JapanCloudSprites_AreAllNonPicking_InTheDefaultUxml()
        {
            var root = BuildTree();
            foreach (var name in new[] { "map-cloud-left-01", "map-cloud-left-02", "map-cloud-right-01", "map-cloud-bottom-01" })
            {
                var cloud = root.Q<VisualElement>(name);
                Assert.IsNotNull(cloud, $"Expected '{name}'.");
                Assert.AreEqual(PickingMode.Ignore, cloud.pickingMode, $"'{name}' must be decorative (picking-mode Ignore) — input locking is the layer container's job, not the individual sprites'.");
            }
        }

        [Test]
        public void OkinawaCloudSprites_AreAllNonPicking_InTheDefaultUxml()
        {
            var root = BuildTree();
            foreach (var name in new[] { "okinawa-cloud-left-01", "okinawa-cloud-left-02", "okinawa-cloud-right-01", "okinawa-cloud-bottom-01" })
            {
                var cloud = root.Q<VisualElement>(name);
                Assert.IsNotNull(cloud, $"Expected '{name}'.");
                Assert.AreEqual(PickingMode.Ignore, cloud.pickingMode);
            }
        }

        [Test]
        public void CloudLayers_AreIgnoredByDefault_InTheDefaultUxml()
        {
            // At rest (no transition running), the layer container itself
            // must also default to Ignore in the static UXML — the
            // transition controller is what flips it to Position at runtime.
            var root = BuildTree();
            Assert.AreEqual(PickingMode.Ignore, root.Q<VisualElement>("map-cloud-layer").pickingMode);
            Assert.AreEqual(PickingMode.Ignore, root.Q<VisualElement>("okinawa-cloud-layer").pickingMode);
        }

        [Test]
        public void CloudLayer_IsDeclaredBeforeItsDetailPanel_SoAnOpenPanelPaintsAboveRestingClouds()
        {
            var root = BuildTree();
            var mapRoot = root.Q<VisualElement>("map-cloud-layer").parent;
            var children = mapRoot.Children().ToList();
            int cloudIndex = children.IndexOf(root.Q<VisualElement>("map-cloud-layer"));
            int panelIndex = children.IndexOf(root.Q<VisualElement>("chapter-panel"));
            Assert.GreaterOrEqual(cloudIndex, 0);
            Assert.GreaterOrEqual(panelIndex, 0);
            Assert.Less(cloudIndex, panelIndex, "The cloud layer must be declared before 'chapter-panel' so the panel's own opaque background always paints over resting clouds.");
        }

        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            for (var current = element.parent; current != null; current = current.parent)
            {
                if (current == ancestor)
                    return true;
            }
            return false;
        }

        private static VisualElement FindAncestorWithClass(VisualElement element, string className)
        {
            for (var current = element?.parent; current != null; current = current.parent)
            {
                if (current.ClassListContains(className))
                    return current;
            }
            return null;
        }

        private static string ExtractRuleBlock(string uss, string header)
        {
            int start = uss.IndexOf(header, System.StringComparison.Ordinal);
            if (start < 0)
                return null;

            int open = uss.IndexOf('{', start);
            if (open < 0)
                return null;

            int depth = 0;
            for (int i = open; i < uss.Length; i++)
            {
                if (uss[i] == '{')
                    depth++;
                else if (uss[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return uss.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }
    }
}
