using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for the final cloud PNG asset integration and cloud overlay
    /// structure (Map Pass 3B, corrected architecture): the 4 supplied final
    /// PNGs exist on disk and are referenced by the correct cloud class in
    /// Map.uss, the overlay lives INSIDE the pan/zoom-transformed canvas (so
    /// it pans/zooms with the map art), resting cloud elements are
    /// non-picking decoration, and the texture import settings that caused
    /// the reported checkerboard/seam artifacts (wrap mode, alpha-is-
    /// transparency, mipmaps) are corrected. "Source PNGs not modified" is
    /// enforced the same way as every other final map asset in this
    /// project — by process (never editing bytes under
    /// Assets/UI/Media/Images/**), verified per-pass via byte-size
    /// comparison (see the investigation report), matching
    /// MapMarkerAssetsTests' existence-only convention for the same reason.
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

        // ---------- texture import settings (root cause of the reported checkerboard/seam) ----------

        [TestCase("cloud_left_01.png")]
        [TestCase("cloud_left_02.png")]
        [TestCase("cloud_right_01.png")]
        [TestCase("cloud_bottom_01.png")]
        public void CloudTexture_UsesClampWrapMode_NotRepeat(string fileName)
        {
            // wrapU/wrapV: 0 (Repeat) let bilinear sampling near a UV 0/1
            // boundary wrap around and blend the opposite edge of the image
            // in — the root cause of the reported thin seam at cloud
            // boundaries. 1 == Clamp.
            string meta = File.ReadAllText($"{CloudsRoot}/{fileName}.meta");
            StringAssert.Contains("wrapU: 1", meta);
            StringAssert.Contains("wrapV: 1", meta);
        }

        [TestCase("cloud_left_01.png")]
        [TestCase("cloud_left_02.png")]
        [TestCase("cloud_right_01.png")]
        [TestCase("cloud_bottom_01.png")]
        public void CloudTexture_HasAlphaIsTransparencyEnabled(string fileName)
        {
            // With this off, Unity's compressor/mipmap generator can bleed
            // the (normally invisible, alpha=0) RGB noise baked under fully
            // transparent pixels into visible edges — the root cause of the
            // reported checkerboard/square artifact. See the investigation
            // report: this noise is confirmed present but only under
            // alpha=0 pixels in the source PNGs, never baked into the
            // visible (semi-)opaque artwork itself.
            string meta = File.ReadAllText($"{CloudsRoot}/{fileName}.meta");
            StringAssert.Contains("alphaIsTransparency: 1", meta);
        }

        [TestCase("cloud_left_01.png")]
        [TestCase("cloud_left_02.png")]
        [TestCase("cloud_right_01.png")]
        [TestCase("cloud_bottom_01.png")]
        public void CloudTexture_HasMipmapsDisabled(string fileName)
        {
            // These are flat 2D UI sprites, never viewed at minifying
            // distance/angle — mipmaps only add another averaging pass that
            // can bleed transparent-region noise into visible edges.
            string meta = File.ReadAllText($"{CloudsRoot}/{fileName}.meta");
            StringAssert.Contains("enableMipMap: 0", meta);
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
            string uss = File.ReadAllText(UssPath);
            foreach (var selector in new[] { ".map-cloud--left-01 {", ".map-cloud--left-02 {", ".map-cloud--right-01 {", ".map-cloud--bottom-01 {" })
            {
                string block = ExtractRuleBlock(uss, selector);
                Assert.IsNotNull(block);
                StringAssert.Contains("background-image: url(", block);
            }
        }

        // ---------- structure: overlay is now INSIDE the transformed canvas ----------

        [Test]
        public void JapanCloudLayer_IsADescendantOfTheTransformedMapCanvas()
        {
            var root = BuildTree();
            var canvas = root.Q<VisualElement>("map-canvas");
            Assert.IsNotNull(canvas, "Expected the '.pan-canvas' element named 'map-canvas'.");

            var cloudLayer = root.Q<VisualElement>("map-cloud-layer");
            Assert.IsNotNull(cloudLayer, "Expected a 'map-cloud-layer' element.");
            Assert.IsTrue(IsDescendantOf(cloudLayer, canvas), "'map-cloud-layer' must be a descendant of 'map-canvas' so it pans/zooms together with the map art — clouds are part of the map composition, not a viewport-fixed overlay.");
        }

        [Test]
        public void OkinawaCloudLayer_IsADescendantOfTheTransformedOkinawaCanvas()
        {
            var root = BuildTree();
            var canvas = root.Q<VisualElement>("okinawa-canvas");
            Assert.IsNotNull(canvas, "Expected the '.pan-canvas' element named 'okinawa-canvas'.");

            var cloudLayer = root.Q<VisualElement>("okinawa-cloud-layer");
            Assert.IsNotNull(cloudLayer, "Expected an 'okinawa-cloud-layer' element.");
            Assert.IsTrue(IsDescendantOf(cloudLayer, canvas), "'okinawa-cloud-layer' must be a descendant of 'okinawa-canvas'.");
        }

        [Test]
        public void JapanCloudLayer_PaintsAboveTheMapArtAndMarkers_DeclaredLastInTheCanvas()
        {
            var root = BuildTree();
            var canvas = root.Q<VisualElement>("map-canvas");
            var children = canvas.Children().ToList();
            var cloudLayer = root.Q<VisualElement>("map-cloud-layer");
            Assert.AreEqual(children.Count - 1, children.IndexOf(cloudLayer), "'map-cloud-layer' must be the LAST child of 'map-canvas' so it paints above the map art and every marker.");
        }

        [Test]
        public void ChapterPanel_IsNotInsideMapStage_SoItAlwaysPaintsAboveRestingClouds()
        {
            // The panel has its own opaque background (".detail-panel") and
            // is a later sibling of "map-stage" at the map-root level —
            // never a descendant of "map-stage"/"map-canvas" — so it always
            // paints above whatever is inside the canvas, clouds included,
            // regardless of the canvas's internal child order.
            var root = BuildTree();
            var mapStage = root.Q<VisualElement>("map-stage");
            var panel = root.Q<VisualElement>("chapter-panel");
            Assert.IsNotNull(mapStage);
            Assert.IsNotNull(panel);
            Assert.IsFalse(IsDescendantOf(panel, mapStage), "'chapter-panel' must not be nested inside 'map-stage'.");
        }

        [Test]
        public void CloudLayer_DoesNotAlterMapStageOrCanvasGeometry()
        {
            string uss = File.ReadAllText(UssPath);
            string stageBlock = ExtractRuleBlock(uss, ".pan-stage {");
            string canvasBlock = ExtractRuleBlock(uss, ".pan-canvas {");
            Assert.IsNotNull(stageBlock);
            Assert.IsNotNull(canvasBlock);
            StringAssert.DoesNotContain("map-cloud", stageBlock);
            StringAssert.DoesNotContain("map-cloud", canvasBlock);
        }

        [Test]
        public void CloudLayer_HasNoOwnOverflowClip_PanStageAloneClipsAtAnyZoomOrPan()
        {
            // An overflow:hidden on the cloud layer itself would double-clip
            // incorrectly once the canvas is zoomed/panned relative to the
            // stage — ".pan-stage" is the one authoritative clip boundary.
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".map-cloud-layer {");
            Assert.IsNotNull(block);
            StringAssert.DoesNotContain("overflow", block);
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
            var root = BuildTree();
            Assert.AreEqual(PickingMode.Ignore, root.Q<VisualElement>("map-cloud-layer").pickingMode);
            Assert.AreEqual(PickingMode.Ignore, root.Q<VisualElement>("okinawa-cloud-layer").pickingMode);
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
