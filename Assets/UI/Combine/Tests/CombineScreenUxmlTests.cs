using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Combine.Tests
{
    /// <summary>
    /// Structural contract for the real "combine" LEVEL 0 checklist screen in
    /// MikeyApp.uxml: the full-bleed background stays outside the safe-area
    /// wrapper, the left preview panel and right five-row checklist both stay
    /// inside it, every supplied asset is actually referenced, and the legacy
    /// bridge/Return-Home actions are still present.
    /// </summary>
    public class CombineScreenUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Combine/Combine.uss";

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
        public void Background_IsFullBleed_OutsideSafeAreaContent_AndKeepsTheOriginalBindingElement()
        {
            var screen = CombineScreen(BuildTree());
            var bg = screen.Q<VisualElement>(className: "combine-bg");
            Assert.IsNotNull(bg, "Expected a .combine-bg full-bleed layer.");
            Assert.IsNull(NearestSafeAreaAncestor(bg), ".combine-bg must not be inside .safe-area-content.");

            // BackgroundMediaController binds this exact element (by name) at the
            // scene level to the static combine_background.png — the element name
            // must survive, even though what it's bound to changed from a video.
            var media = screen.Q<VisualElement>("combine-bg-media");
            Assert.IsNotNull(media, "Expected a 'combine-bg-media' element for BackgroundMediaController to target.");
            Assert.IsTrue(media.ClassListContains("bg-media"), "'combine-bg-media' must keep the shared .bg-media class.");
        }

        [Test]
        public void FiveChecklistRows_Exist_InsideSafeAreaContent()
        {
            var screen = CombineScreen(BuildTree());
            for (int i = 0; i < 5; i++)
            {
                var row = screen.Q<VisualElement>($"combine-row-{i}");
                Assert.IsNotNull(row, $"Expected a checklist row named 'combine-row-{i}'.");
                Assert.IsNotNull(NearestSafeAreaAncestor(row), $"'combine-row-{i}' must be inside .safe-area-content.");
                Assert.IsNotNull(screen.Q<VisualElement>($"combine-row-{i}-icon"),
                    $"Expected a state icon element named 'combine-row-{i}-icon'.");
            }
        }

        [Test]
        public void YokoGeriRow_CarriesTheGedanChudanJodanSecondaryLine()
        {
            var screen = CombineScreen(BuildTree());
            var row = screen.Q<VisualElement>("combine-row-4");
            Assert.IsNotNull(row);
            var secondary = row.Q<Label>(className: "combine-row__secondary");
            Assert.IsNotNull(secondary, "The Slow Yoko-Geri row must show its Gedan/Chudan/Jodan secondary line.");
            Assert.AreEqual("GEDAN → CHUDAN → JODAN", secondary.text);
        }

        [Test]
        public void LeftPanel_HasHeaderProgressIllustrationCopyAndStartButton()
        {
            var screen = CombineScreen(BuildTree());
            Assert.IsNotNull(screen.Q<Label>("combine-progress"), "Expected a 'combine-progress' label (X / 5 COMPLETE).");
            Assert.IsNotNull(screen.Q<VisualElement>("combine-illustration"), "Expected a 'combine-illustration' element.");
            Assert.IsNotNull(screen.Q<Label>("combine-test-title"), "Expected a 'combine-test-title' label.");
            Assert.IsNotNull(screen.Q<Label>("combine-test-desc"), "Expected a 'combine-test-desc' label.");
            Assert.IsNotNull(screen.Q<Label>("combine-test-secondary"), "Expected a 'combine-test-secondary' label.");
            Assert.IsNotNull(screen.Q<Label>("combine-test-stat"), "Expected a 'combine-test-stat' label.");

            var start = screen.Q<Button>("combine-start");
            Assert.IsNotNull(start, "Expected the local 'combine-start' action (destination varies by selected test, so it is NOT a static 'go-' navigator).");
            Assert.IsFalse(start.name.StartsWith("go-"));
        }

        [Test]
        public void LevelZeroTitleAndKicker_MatchTheLockedReference()
        {
            var screen = CombineScreen(BuildTree());
            var texts = screen.Query<Label>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(texts, "LEVEL 0");
            CollectionAssert.Contains(texts, "COMBINE");
        }

        [Test]
        public void LegacyBridgeButton_StillExists_ForHomeCtaAndMapEntryCompatibility()
        {
            var screen = CombineScreen(BuildTree());
            var startLevel1 = screen.Q<Button>("combine-start-lvl1");
            Assert.IsNotNull(startLevel1, "Expected the legacy 'combine-start-lvl1' bridge button.");
            Assert.IsFalse(startLevel1.name.StartsWith("go-"),
                "'combine-start-lvl1' must be controller-bound, not a static 'go-' navigator.");
        }

        [Test]
        public void ReturnHomeRoute_StillExists()
        {
            var screen = CombineScreen(BuildTree());
            Assert.IsNotEmpty(screen.Query<VisualElement>(name: "go-menu").ToList(),
                "Combine must keep a 'go-menu' return-Home route.");
        }

        [Test]
        public void OldMockResultsMarkup_IsGone()
        {
            var screen = CombineScreen(BuildTree());
            foreach (var name in new[]
            {
                "combine-loading", "combine-empty", "combine-ready", "combine-error", "combine-items",
                "combine-devbar", "combine-dev-loading", "combine-dev-empty", "combine-dev-ready",
                "combine-dev-error", "combine-dev-cycle", "combine-retry", "combine-retry-assessment",
            })
            {
                Assert.IsNull(screen.Q<VisualElement>(name), $"Old mock-results element '{name}' must be removed.");
            }
        }

        [Test]
        public void EverySuppliedCombineAsset_IsReferencedByTheStylesheet()
        {
            string uss = System.IO.File.ReadAllText(UssPath);
            foreach (var file in new[]
            {
                "combine_background.png", "combine_camera.png", "combine_pushups.png", "combine_squats.png",
                "combine_wallsit.png", "combine_yokogeri.png", "combine_check.png", "combine_lock.png",
                "combine_start_brush.png", "combine_divider_horizontal.png", "combine_divider_vertical.png",
                "combine_selected_red.png",
            })
            {
                StringAssert.Contains(file, uss, $"Combine.uss must reference the supplied '{file}' asset.");
            }
        }

        [Test]
        public void RowStateModifierClasses_AreDefinedInTheStylesheet()
        {
            string uss = System.IO.File.ReadAllText(UssPath);
            foreach (var selector in new[]
            {
                ".combine-row--locked", ".combine-row--available", ".combine-row--complete", ".combine-row--selected",
                ".combine-row__icon--check", ".combine-row__icon--lock",
            })
            {
                StringAssert.Contains(selector, uss, $"Combine.uss must define '{selector}'.");
            }
        }

        [Test]
        public void EveryRow_HasATestIllustrationThumbnail()
        {
            var screen = CombineScreen(BuildTree());
            var expected = new[]
            {
                "combine-row__thumb--camera", "combine-row__thumb--pushups", "combine-row__thumb--squats",
                "combine-row__thumb--wallsit", "combine-row__thumb--yokogeri",
            };
            foreach (var className in expected)
                Assert.IsNotNull(screen.Q<VisualElement>(className: className), $"Expected a row thumbnail carrying '{className}'.");
        }

        [Test]
        public void ProgressRule_ExistsBetweenTitleAndProgressLabel()
        {
            var screen = CombineScreen(BuildTree());
            Assert.IsNotNull(screen.Q<VisualElement>(className: "combine-progress-rule"),
                "Expected a '.combine-progress-rule' divider between COMBINE and the X/5 COMPLETE line.");
        }

        /// <summary>
        /// Regression guard for the "huge stretched rectangular smear" bug: the
        /// three heavily-elongated brush assets (selected-row accent ~200x2758,
        /// START brush ~2400x98, vertical panel divider) must render at their
        /// own aspect ratio (scale-to-fit), never scale-and-crop/stretch-to-fill,
        /// which would crop or distort their true painted shape.
        /// </summary>
        [Test]
        public void ElongatedBrushAssets_UseScaleToFit_NeverCropOrStretch()
        {
            string uss = System.IO.File.ReadAllText(UssPath);

            string accentBlock = ExtractRuleBlock(uss, ".combine-row__accent {");
            Assert.IsNotNull(accentBlock, "Expected a '.combine-row__accent' rule.");
            StringAssert.Contains("scale-to-fit", accentBlock);
            StringAssert.DoesNotContain("scale-and-crop", accentBlock);
            StringAssert.DoesNotContain("stretch-to-fill", accentBlock);

            string startBlock = ExtractRuleBlock(uss, ".combine-start-btn {");
            Assert.IsNotNull(startBlock, "Expected a '.combine-start-btn' rule.");
            StringAssert.Contains("scale-to-fit", startBlock);
            StringAssert.DoesNotContain("scale-and-crop", startBlock);
            StringAssert.DoesNotContain("stretch-to-fill", startBlock);

            string dividerBlock = ExtractRuleBlock(uss, ".combine-divider-vertical {");
            Assert.IsNotNull(dividerBlock, "Expected a '.combine-divider-vertical' rule.");
            StringAssert.Contains("scale-to-fit", dividerBlock);
            StringAssert.DoesNotContain("scale-and-crop", dividerBlock);
            StringAssert.DoesNotContain("stretch-to-fill", dividerBlock);
        }

        [Test]
        public void SelectedRowAccent_IsNarrow_NotAPanelFillingSmear()
        {
            string uss = System.IO.File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, ".combine-row__accent {");
            Assert.IsNotNull(block);
            StringAssert.DoesNotContain("right: 0", block,
                "The selected-row accent must not stretch across the full row width — it must stay a narrow strip pinned to the left edge.");
        }

        private static string ExtractRuleBlock(string uss, string header)
        {
            int start = uss.IndexOf(header, System.StringComparison.Ordinal);
            if (start < 0)
                return null;
            int open = start + header.Length;
            int close = uss.IndexOf('}', open);
            return close < 0 ? null : uss.Substring(open, close - open);
        }
    }
}
