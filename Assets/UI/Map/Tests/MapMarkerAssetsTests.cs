using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for the final chapter/mission marker asset integration: the 7
    /// supplied final PNGs (3 chapter + 4 mission) exist on disk, are
    /// referenced by the correct marker icon class in Map.uss, the old
    /// placeholder badge/dot/index circles are gone, icon sizes land in the
    /// approved mobile ranges, markers still live inside the
    /// pan/zoom-transformed map artboard rather than the viewport, and the
    /// node transform anchors the marker's bottom TIP (not its center) to
    /// its normalized coordinate.
    /// </summary>
    public class MapMarkerAssetsTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";
        private const string MarkersRoot = "Assets/UI/Media/Images/Map/Markers";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        // ---------- final assets exist on disk, source bytes untouched by us ----------

        [TestCase("Chapters/chapter_okinawa.png")]
        [TestCase("Chapters/chapter_fukuoka.png")]
        [TestCase("Chapters/chapter_hiroshima.png")]
        [TestCase("Missions/mission_lvl0.png")]
        [TestCase("Missions/mission_training.png")]
        [TestCase("Missions/mission_fight.png")]
        [TestCase("Missions/mission_boss.png")]
        public void FinalMarkerAsset_ExistsOnDisk(string relativePath)
        {
            string path = $"{MarkersRoot}/{relativePath}";
            Assert.IsTrue(File.Exists(path), $"Expected the final marker asset at {path}.");
        }

        // ---------- each icon class references the correct final PNG ----------

        [TestCase(".chapter-node__icon--okinawa {", "chapter_okinawa.png")]
        [TestCase(".chapter-node__icon--fukuoka {", "chapter_fukuoka.png")]
        [TestCase(".chapter-node__icon--hiroshima {", "chapter_hiroshima.png")]
        [TestCase(".level-node__icon--special {", "mission_lvl0.png")]
        [TestCase(".level-node__icon--training {", "mission_training.png")]
        [TestCase(".level-node__icon--fight {", "mission_fight.png")]
        [TestCase(".level-node__icon--boss-fight {", "mission_boss.png")]
        public void MarkerIconClass_ReferencesItsFinalAsset(string selector, string expectedFileName)
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, selector);
            Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
            StringAssert.Contains(expectedFileName, block);
        }

        // ---------- old placeholder circles/dots/index badges are gone ----------

        [Test]
        public void OldPlaceholderBadgeStyling_IsRemoved_FromUss()
        {
            string uss = File.ReadAllText(UssPath);
            StringAssert.DoesNotContain(".chapter-node__badge", uss, "The old dark circular chapter badge must be gone.");
            StringAssert.DoesNotContain(".chapter-node__badge-dot", uss, "The old chapter badge dot must be gone.");
            StringAssert.DoesNotContain(".level-node__badge", uss, "The old dark circular level badge must be gone.");
            StringAssert.DoesNotContain(".level-node__badge-index", uss, "The old numeric level badge index must be gone.");
        }

        [Test]
        public void OldPlaceholderBadgeElements_AreRemoved_FromUxml()
        {
            var root = BuildTree();
            Assert.AreEqual(0, root.Query<VisualElement>(className: "chapter-node__badge").ToList().Count);
            Assert.AreEqual(0, root.Query<VisualElement>(className: "chapter-node__badge-dot").ToList().Count);
            Assert.AreEqual(0, root.Query<VisualElement>(className: "level-node__badge").ToList().Count);
            Assert.AreEqual(0, root.Query<Label>(className: "level-node__badge-index").ToList().Count);
        }

        // ---------- icon sizes land in the approved mobile ranges ----------

        [Test]
        public void ChapterIcon_IsWithinApprovedSize_58To72Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), ".chapter-node__icon {"), "width");
            Assert.GreaterOrEqual(size, 58f);
            Assert.LessOrEqual(size, 72f);
        }

        [Test]
        public void MissionIcon_IsWithinApprovedSize_46To60Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), ".level-node__icon {"), "width");
            Assert.GreaterOrEqual(size, 46f);
            Assert.LessOrEqual(size, 60f);
        }

        // ---------- locked markers read clearly, not just barely visible ----------

        [TestCase(".chapter-node--locked .chapter-node__icon {")]
        [TestCase(".level-node--locked .level-node__icon {")]
        public void LockedIcon_OpacityIsWithinReadableTarget_78To86Percent(string selector)
        {
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), selector);
            Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
            var match = System.Text.RegularExpressions.Regex.Match(block, @"opacity:\s*(\d+(\.\d+)?)");
            Assert.IsTrue(match.Success, $"Expected an 'opacity: <n>' declaration in: {block}");
            float opacity = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(opacity, 0.78f);
            Assert.LessOrEqual(opacity, 0.86f);
        }

        [TestCase(".chapter-node--locked .chapter-node__icon {")]
        [TestCase(".level-node--locked .level-node__icon {")]
        public void LockedIcon_TintIsSubstantiallyLighterThanTheOldHeavyGray(string selector)
        {
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), selector);
            Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
            var match = System.Text.RegularExpressions.Regex.Match(block, @"-unity-background-image-tint-color:\s*rgb\((\d+),\s*(\d+),\s*(\d+)\)");
            Assert.IsTrue(match.Success, $"Expected an rgb(...) tint declaration in: {block}");
            int r = int.Parse(match.Groups[1].Value);
            // The old value was rgb(150,150,150) — a much heavier darken. The
            // final calibration must be substantially lighter so the
            // Training/Fight symbol and chapter emblems stay clearly readable
            // while locked, without removing the tint cue entirely (255=none).
            Assert.Greater(r, 180, "Locked tint must be substantially lighter than the old rgb(150,150,150).");
            Assert.Less(r, 255, "A tint must still be present — locked markers stay visually distinct from unlocked ones.");
        }

        [Test]
        public void ChapterAndLevelNodes_KeepTheSharedTapTargetTouchArea()
        {
            var root = BuildTree();
            foreach (var name in new[] { "chapter-node-okinawa", "chapter-node-fukuoka", "chapter-node-hiroshima" })
                Assert.IsTrue(root.Q<Button>(name).ClassListContains("tap-target-lg"), $"'{name}' must keep a >=touch-target class.");
            for (int i = 0; i < 7; i++)
                Assert.IsTrue(root.Q<Button>($"level-node-{i}").ClassListContains("tap-target-lg"), $"'level-node-{i}' must keep a >=touch-target class.");
        }

        // ---------- coordinate refers to the marker's bottom TIP, not its center ----------

        [TestCase(".chapter-node {")]
        [TestCase(".level-node {")]
        public void MarkerNode_AnchorsItsBottomTip_NotItsCenter(string selector)
        {
            string uss = File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, selector);
            Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
            StringAssert.Contains("translate: -50% -100%;", block,
                "The node's own bottom edge (its pin tip, since the label is ordered before the icon) must anchor to the coordinate — not a center anchor.");
            StringAssert.DoesNotContain("-50% -50%", block, "Must not be a center anchor.");
        }

        [Test]
        public void ChapterLabel_IsOrderedBeforeItsIcon_SoTheNodesBottomEdgeIsThePinTip()
        {
            var root = BuildTree();
            var okinawa = root.Q<Button>("chapter-node-okinawa");
            Assert.IsNotNull(okinawa);
            var children = okinawa.Children().ToList();
            int labelIndex = children.IndexOf(okinawa.Q<Label>(className: "chapter-node__label"));
            int iconIndex = children.IndexOf(okinawa.Q<VisualElement>(className: "chapter-node__icon"));
            Assert.GreaterOrEqual(labelIndex, 0);
            Assert.GreaterOrEqual(iconIndex, 0);
            Assert.Less(labelIndex, iconIndex, "Label must come before the icon so the node's bottom edge is the icon's bottom edge (the pin tip).");
        }

        [Test]
        public void MissionLabel_IsOrderedBeforeItsIcon_SoTheNodesBottomEdgeIsThePinTip()
        {
            var root = BuildTree();
            var level0 = root.Q<Button>("level-node-0");
            Assert.IsNotNull(level0);
            var children = level0.Children().ToList();
            int labelIndex = children.IndexOf(level0.Q<Label>(className: "level-node__label"));
            int iconIndex = children.IndexOf(level0.Q<VisualElement>(className: "level-node__icon"));
            Assert.GreaterOrEqual(labelIndex, 0);
            Assert.GreaterOrEqual(iconIndex, 0);
            Assert.Less(labelIndex, iconIndex, "Label must come before the icon so the node's bottom edge is the icon's bottom edge (the pin tip).");
        }

        // ---------- markers stay attached to the transformed map artboard ----------

        [Test]
        public void ChapterMarkers_LiveInsideTheTransformedPanCanvas_NotTheViewport()
        {
            var root = BuildTree();
            var canvas = root.Q<VisualElement>("map-canvas");
            Assert.IsNotNull(canvas, "Expected the '.pan-canvas' element named 'map-canvas'.");
            foreach (var name in new[] { "chapter-node-okinawa", "chapter-node-fukuoka", "chapter-node-hiroshima" })
            {
                var node = root.Q<Button>(name);
                Assert.IsNotNull(node, $"Expected '{name}'.");
                Assert.IsTrue(IsDescendantOf(node, canvas), $"'{name}' must be a descendant of 'map-canvas' so it pans/zooms with the map art, not the viewport.");
            }
        }

        [Test]
        public void MissionMarkers_LiveInsideTheTransformedPanCanvas_NotTheViewport()
        {
            var root = BuildTree();
            var canvas = root.Q<VisualElement>("okinawa-canvas");
            Assert.IsNotNull(canvas, "Expected the '.pan-canvas' element named 'okinawa-canvas'.");
            for (int i = 0; i < 7; i++)
            {
                var node = root.Q<Button>($"level-node-{i}");
                Assert.IsNotNull(node, $"Expected 'level-node-{i}'.");
                Assert.IsTrue(IsDescendantOf(node, canvas), $"'level-node-{i}' must be a descendant of 'okinawa-canvas' so it pans/zooms with the map art, not the viewport.");
            }
        }

        // ---------- clouds have NOT been introduced (out of scope until Pass 3B) ----------

        [Test]
        public void NoCloudAssetsOrOverlays_HaveBeenIntroduced()
        {
            string uss = File.ReadAllText(UssPath).ToLowerInvariant();
            string uxml = File.ReadAllText(UxmlPath).ToLowerInvariant();
            StringAssert.DoesNotContain("cloud", uss, "Clouds are Map Pass 3B — not this pass.");
            StringAssert.DoesNotContain("cloud", uxml, "Clouds are Map Pass 3B — not this pass.");
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

        private static float ExtractPx(string block, string property)
        {
            Assert.IsNotNull(block, "Expected a non-null rule block.");
            var match = System.Text.RegularExpressions.Regex.Match(block, property + @"\s*:\s*(-?\d+(\.\d+)?)px");
            Assert.IsTrue(match.Success, $"Expected a '{property}: <n>px' declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Body of the first USS rule whose header matches <paramref name="header"/> (e.g. ".pan-stage {"), or null.</summary>
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
