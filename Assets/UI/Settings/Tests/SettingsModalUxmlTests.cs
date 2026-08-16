using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Settings.Tests
{
    /// <summary>
    /// Structural contract for the ONE shared Settings modal in MikeyApp.uxml:
    /// exactly one instance (not duplicated per screen), containing Music /
    /// Sound Effects / Trainer Voice / Close, declared outside every screen
    /// (as the last child of the root "app" element, after "profile" — the
    /// last screen) so it always paints/hit-tests above whichever screen is
    /// active, and preserving the larger dark-glass design introduced for the
    /// Map HUD (60% width, ~40px title, ~24px labels, upgraded sliders, a
    /// self-contained 56px Close button).
    /// </summary>
    public class SettingsModalUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Settings/Settings.uss";

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
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

        private static float ExtractPx(string block, string property)
        {
            Assert.IsNotNull(block, "Expected a non-null rule block.");
            var match = System.Text.RegularExpressions.Regex.Match(block, property + @"\s*:\s*(-?\d+(\.\d+)?)px");
            Assert.IsTrue(match.Success, $"Expected a '{property}: <n>px' declaration in: {block}");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------- exactly one shared instance ----------

        [Test]
        public void ExactlyOneSettingsModal_ExistsInTheWholeDocument()
        {
            var root = BuildTree();
            var modals = root.Query<VisualElement>(className: "settings-modal").ToList();
            Assert.AreEqual(1, modals.Count, "There must be exactly one shared Settings modal, not one per screen.");
            Assert.AreEqual("shared-settings-modal", modals[0].name);
        }

        [Test]
        public void SettingsModal_IsDeclaredOutsideEveryScreen_AsTheRootsLastChild()
        {
            var root = BuildTree();
            var modal = root.Q<VisualElement>("shared-settings-modal");
            Assert.IsNotNull(modal);

            for (var p = modal.parent; p != null; p = p.parent)
                Assert.IsFalse(p.ClassListContains("screen"), "The shared Settings modal must not live inside any screen.");

            // Declared after every ".screen" element (siblings under "app") so
            // it always paints/hit-tests on top of whichever screen is active.
            var appChildren = root.Q<VisualElement>("app")?.Children().ToList() ?? root.Children().ToList();
            int modalIndex = appChildren.IndexOf(modal);
            Assert.Greater(modalIndex, -1, "Expected the modal to be a direct child of the root 'app' element.");
            var lastScreenIndex = appChildren.FindLastIndex(c => c.ClassListContains("screen"));
            Assert.Greater(modalIndex, lastScreenIndex, "The shared Settings modal must be declared after every screen.");
        }

        [Test]
        public void SettingsModal_IsHiddenByDefault()
        {
            var modal = BuildTree().Q<VisualElement>("shared-settings-modal");
            Assert.IsNotNull(modal);
            Assert.IsFalse(modal.ClassListContains("settings-modal--open"));

            string block = ExtractRuleBlock(File.ReadAllText(UssPath), "\n.settings-modal {");
            Assert.IsNotNull(block);
            StringAssert.Contains("display: none", block);
        }

        // ---------- Music / Sound Effects / Trainer Voice / Close — shared, not duplicated ----------

        [Test]
        public void MusicSoundEffectsTrainerVoice_EachExistExactlyOnce_InTheWholeDocument()
        {
            var root = BuildTree();
            Assert.AreEqual(1, root.Query<Slider>(name: "shared-settings-music").ToList().Count);
            Assert.AreEqual(1, root.Query<Slider>(name: "shared-settings-sfx").ToList().Count);
            Assert.AreEqual(1, root.Query<Slider>(name: "shared-settings-trainer").ToList().Count);
        }

        [Test]
        public void OldPerScreenDuplicates_AreGone()
        {
            var root = BuildTree();
            foreach (var oldName in new[]
            {
                "menu-settings-modal", "menu-settings-music", "menu-settings-sfx", "menu-settings-trainer", "menu-settings-close",
                "map-settings-modal", "map-settings-music", "map-settings-sfx", "map-settings-trainer", "map-settings-close",
                "okinawa-settings-modal", "okinawa-settings-music", "okinawa-settings-sfx", "okinawa-settings-trainer", "okinawa-settings-close",
            })
            {
                Assert.IsNull(root.Q<VisualElement>(oldName), $"Old duplicated Settings element '{oldName}' must be gone.");
            }
        }

        [Test]
        public void SettingsModal_ContainsCloseAction()
        {
            var modal = BuildTree().Q<VisualElement>("shared-settings-modal");
            Assert.IsNotNull(modal.Q<Button>("shared-settings-close"));
        }

        [Test]
        public void SettingsModal_ContainsTitleAndControlLabels_InTitleCase()
        {
            var modal = BuildTree().Q<VisualElement>("shared-settings-modal");
            var labels = modal.Query<Label>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(labels, "Settings");
            CollectionAssert.Contains(labels, "Music");
            CollectionAssert.Contains(labels, "Sound Effects");
            CollectionAssert.Contains(labels, "Trainer Voice");
        }

        [Test]
        public void Sliders_RangeAndDefaultValues_MatchTheAudioSettingsStoreDefaults()
        {
            var modal = BuildTree().Q<VisualElement>("shared-settings-modal");
            var music = modal.Q<Slider>("shared-settings-music");
            var sfx = modal.Q<Slider>("shared-settings-sfx");
            var trainer = modal.Q<Slider>("shared-settings-trainer");

            foreach (var slider in new[] { music, sfx, trainer })
            {
                Assert.AreEqual(0f, slider.lowValue);
                Assert.AreEqual(1f, slider.highValue);
            }
            Assert.AreEqual(0.70f, music.value, 0.001f, "Music's markup default must match AudioSettingsStore's safe default.");
            Assert.AreEqual(1.00f, sfx.value, 0.001f);
            Assert.AreEqual(1.00f, trainer.value, 0.001f);
        }

        [Test]
        public void AllThreeEntryPoints_ExistAndAreDistinctFromTheModalItself()
        {
            var root = BuildTree();
            Assert.IsNotNull(root.Q<Button>("menu-settings-open"), "Main Menu's Settings entry point must exist.");
            Assert.IsNotNull(root.Q<Button>("map-topbar-settings"), "Japan map's Settings entry point must exist.");
            Assert.IsNotNull(root.Q<Button>("okinawa-topbar-settings"), "Okinawa map's Settings entry point must exist.");
        }

        // ---------- preserved Pass-2 (commit 1186308) sizing/style ----------

        [Test]
        public void ModalCard_Is60PercentWidth_LargeDarkGlassPanel()
        {
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), "\n.settings-modal__card {");
            Assert.IsNotNull(block);
            StringAssert.Contains("width: 60%", block);
            StringAssert.DoesNotContain("width: 420px", block, "The old small fixed-width card must not return.");
        }

        [Test]
        public void Title_IsWithinPreservedTarget_36To44Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.settings-modal__title {"), "font-size");
            Assert.GreaterOrEqual(size, 36f);
            Assert.LessOrEqual(size, 44f);
        }

        [Test]
        public void ControlLabels_AreWithinPreservedTarget_22To26Px()
        {
            float size = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.setting__label {"), "font-size");
            Assert.GreaterOrEqual(size, 22f);
            Assert.LessOrEqual(size, 26f);
        }

        [Test]
        public void Sliders_KeepTheUpgradedThumbAndTrackSize()
        {
            string uss = File.ReadAllText(UssPath);
            string tracker = ExtractRuleBlock(uss, ".setting__slider .unity-base-slider__tracker {");
            string dragger = ExtractRuleBlock(uss, ".setting__slider .unity-base-slider__dragger {");
            Assert.IsNotNull(tracker);
            Assert.IsNotNull(dragger);
            Assert.GreaterOrEqual(ExtractPx(tracker, "height"), 6f);
            Assert.GreaterOrEqual(ExtractPx(dragger, "width"), 24f);
        }

        [Test]
        public void AllThreeSliders_CarryTheSharedSliderClass()
        {
            var modal = BuildTree().Q<VisualElement>("shared-settings-modal");
            var sliders = modal.Query<Slider>().ToList();
            Assert.AreEqual(3, sliders.Count);
            foreach (var slider in sliders)
                Assert.IsTrue(slider.ClassListContains("setting__slider"));
        }

        [Test]
        public void CloseButton_Is56pxTall_AndSelfContained_NotFightingTheSharedThemeButtonClass()
        {
            var modal = BuildTree().Q<VisualElement>("shared-settings-modal");
            var close = modal.Q<Button>("shared-settings-close");
            Assert.IsNotNull(close);
            Assert.IsFalse(close.ClassListContains("btn"), "Must not reuse theme.uss's shared '.btn' class, which would fight this modal's own specificity.");

            float height = ExtractPx(ExtractRuleBlock(File.ReadAllText(UssPath), "\n.settings-modal__close {"), "height");
            Assert.AreEqual(56f, height, 0.01f);
        }
    }
}
