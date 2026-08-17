using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Structural contract for the new "profileDetails" screen: a proper full
    /// screen (not a popup) with Display Name / Gender / Age / Weight / Height
    /// fields and Cancel/Save actions.
    /// </summary>
    public class ProfileDetailsScreenUxmlTests
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

        private static VisualElement Details(VisualElement root)
        {
            var screen = root.Q<VisualElement>("profileDetails");
            Assert.IsNotNull(screen, "MikeyApp.uxml must contain a screen named 'profileDetails'.");
            Assert.IsTrue(screen.ClassListContains("screen"), "'profileDetails' must carry the .screen class.");
            return screen;
        }

        [Test]
        public void Screen_ExistsExactlyOnce_WithVisibleTitle()
        {
            var root = BuildTree();
            var screens = root.Query<VisualElement>(className: "screen").ToList();
            Assert.AreEqual(1, screens.Count(s => s.name == "profileDetails"));

            var labels = Details(root).Query<Label>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(labels, "Profile Details");
        }

        [Test]
        public void HasExactlyOneSafeAreaContent_AndFullBleedBackgroundOutsideIt()
        {
            var screen = Details(BuildTree());
            Assert.AreEqual(1, screen.Query<VisualElement>(className: "safe-area-content").ToList().Count);

            var bg = screen.Q<VisualElement>(className: "profile-details-bg");
            Assert.IsNotNull(bg, "Expected a '.profile-details-bg' full-bleed background.");
            for (var p = bg.parent; p != null; p = p.parent)
                Assert.IsFalse(p.ClassListContains("safe-area-content"), "'.profile-details-bg' must not be inside .safe-area-content.");
        }

        [Test]
        public void AllFiveFields_Exist()
        {
            var screen = Details(BuildTree());
            Assert.IsNotNull(screen.Q<TextField>("profile-details-display-name"), "Expected Display Name field.");
            Assert.IsNotNull(screen.Q<VisualElement>("profile-details-gender-group"), "Expected a named Gender selection group.");
            Assert.IsNotNull(screen.Q<TextField>("profile-details-age"), "Expected Age field.");
            Assert.IsNotNull(screen.Q<TextField>("profile-details-weight"), "Expected Weight field.");
            Assert.IsNotNull(screen.Q<TextField>("profile-details-height"), "Expected Height field.");
        }

        [Test]
        public void LayoutIsTwoColumns_LeftHasIdentityFields_RightHasBodyMetricsAndNote()
        {
            var screen = Details(BuildTree());
            var form = screen.Q<VisualElement>(className: "profile-details-form");
            Assert.IsNotNull(form, "Expected the '.profile-details-form' two-column row.");

            var columns = form.Query<VisualElement>(className: "profile-details-column").ToList();
            Assert.AreEqual(2, columns.Count, "Expected exactly a left and a right column.");

            var left = columns[0];
            Assert.IsNotNull(left.Q<TextField>("profile-details-display-name"), "Display Name must be in the left column.");
            Assert.IsNotNull(left.Q<VisualElement>("profile-details-gender-group"), "Gender must be in the left column.");
            Assert.IsNotNull(left.Q<TextField>("profile-details-age"), "Age must be in the left column.");

            var right = columns[1];
            Assert.IsTrue(right.ClassListContains("profile-details-column--right"));
            Assert.IsNotNull(right.Q<TextField>("profile-details-weight"), "Weight must be in the right column.");
            Assert.IsNotNull(right.Q<TextField>("profile-details-height"), "Height must be in the right column.");

            var noteTexts = right.Query<Label>(className: "profile-details-note").ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(noteTexts, "Used to personalize future training statistics.");
        }

        [Test]
        public void DisplayNameField_HasMaxLengthTwentyFour()
        {
            var field = Details(BuildTree()).Q<TextField>("profile-details-display-name");
            Assert.AreEqual(24, field.maxLength);
        }

        [Test]
        public void Gender_HasAllFourOptions()
        {
            var screen = Details(BuildTree());
            foreach (var name in new[]
            {
                "profile-details-gender-male", "profile-details-gender-female",
                "profile-details-gender-other", "profile-details-gender-undisclosed",
            })
            {
                Assert.IsNotNull(screen.Q<Button>(name), $"Expected gender option '{name}'.");
            }

            var texts = screen.Query<Button>(className: "profile-details-gender-option").ToList().Select(b => b.text).ToList();
            CollectionAssert.AreEquivalent(new[] { "Male", "Female", "Other", "Prefer not to say" }, texts);
        }

        [Test]
        public void WeightAndHeight_ShowVisibleUnits()
        {
            var screen = Details(BuildTree());
            var labels = screen.Query<Label>(className: "profile-details-field__unit").ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(labels, "kg");
            CollectionAssert.Contains(labels, "cm");
        }

        [Test]
        public void PerFieldErrorLabels_Exist_OneForEachOfTheFiveFields()
        {
            var screen = Details(BuildTree());
            foreach (var name in new[]
            {
                "profile-details-display-name-error", "profile-details-gender-error", "profile-details-age-error",
                "profile-details-weight-error", "profile-details-height-error",
            })
            {
                var error = screen.Q<Label>(name);
                Assert.IsNotNull(error, $"Expected a compact per-field error label named '{name}'.");
                Assert.IsTrue(error.ClassListContains("profile-details-field__error"),
                    $"'{name}' must carry the field-error class so it stays hidden until shown.");
            }
        }

        [Test]
        public void OldSingleGlobalErrorLabel_IsRemoved()
        {
            var screen = Details(BuildTree());
            Assert.IsNull(screen.Q<Label>("profile-details-error"),
                "The old single vague global error label must be replaced by per-field errors.");
        }

        [Test]
        public void Actions_CancelIsPlainGoProfile_SaveIsControllerBound()
        {
            var root = BuildTree();
            var screen = Details(root);

            var cancel = screen.Q<Button>("go-profile");
            Assert.IsNotNull(cancel, "Cancel must be a plain 'go-profile' navigator — nothing is saved until Save, so there is nothing to revert.");
            Assert.AreEqual("Cancel", cancel.text);
            Assert.IsTrue(root.Q<VisualElement>("profile").ClassListContains("screen"), "'go-profile' target must exist.");

            var save = screen.Q<Button>("profile-details-save");
            Assert.IsNotNull(save, "Expected a controller-bound Save button (must validate before navigating).");
            Assert.AreEqual("Save", save.text);
        }

        [Test]
        public void ProfileDetailsStylesheet_IsLinked_AndDoesNotRedeclareTheGlobalFont()
        {
            const string ussPath = "Assets/UI/Profile/ProfileDetails.uss";
            Assert.IsTrue(File.Exists(ussPath), $"Expected stylesheet at {ussPath}.");
            string uss = File.ReadAllText(ussPath);
            StringAssert.DoesNotContain("-unity-font-definition", uss,
                "Must inherit the global mikey_ui.otf from theme.uss, never redeclare it locally.");
        }

        [Test]
        public void Background_ReusesTheSameFinalProfileArt_BehindAStrongDarkScrim()
        {
            var screen = Details(BuildTree());
            var bg = screen.Q<VisualElement>(className: "profile-details-bg");
            Assert.IsNotNull(bg);
            var scrim = bg.Q<VisualElement>(className: "profile-details-scrim");
            Assert.IsNotNull(scrim, "Expected a '.profile-details-scrim' child inside '.profile-details-bg'.");

            const string ussPath = "Assets/UI/Profile/ProfileDetails.uss";
            string uss = File.ReadAllText(ussPath);
            StringAssert.Contains("url(\"/Assets/UI/Media/Images/Profile/profile_background.jpg\")", uss,
                "Must reuse the exact same Profile background art, not a different or new image.");

            string scrimBlock = ExtractRuleBlock(uss, "\n.profile-details-scrim {");
            Assert.IsNotNull(scrimBlock, "Expected a '.profile-details-scrim' rule.");
            var alphaMatch = Regex.Match(scrimBlock, @"rgba\(\s*\d+,\s*\d+,\s*\d+,\s*(\d*\.?\d+)\s*\)");
            Assert.IsTrue(alphaMatch.Success, "Expected the scrim's background-color to be an rgba(...) with an alpha.");
            float alpha = float.Parse(alphaMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(alpha, 0.78f, "Scrim must be strong (>=0.78 alpha).");
            Assert.LessOrEqual(alpha, 0.86f, "Scrim must not fully black out the background art (<=0.86 alpha).");
        }

        [Test]
        public void Inputs_AreDarkTransparent_NotBrightGrayFilledBoxes_AndUseCrimsonOnFocusOrInvalid()
        {
            const string ussPath = "Assets/UI/Profile/ProfileDetails.uss";
            string uss = File.ReadAllText(ussPath);

            string baseBlock = ExtractRuleBlock(uss, "\n.profile-details-field__input .unity-base-field__input {");
            Assert.IsNotNull(baseBlock, "Expected the inner input-box rule.");
            var bgMatch = Regex.Match(baseBlock, @"background-color:\s*rgba\(\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d*\.?\d+)\s*\)");
            Assert.IsTrue(bgMatch.Success, "Expected a dark, translucent rgba(...) background-color.");
            int r = int.Parse(bgMatch.Groups[1].Value);
            int g = int.Parse(bgMatch.Groups[2].Value);
            int b = int.Parse(bgMatch.Groups[3].Value);
            Assert.Less(r, 60, "Input fill must be near-black, not a bright gray desktop-style box.");
            Assert.Less(g, 60, "Input fill must be near-black, not a bright gray desktop-style box.");
            Assert.Less(b, 60, "Input fill must be near-black, not a bright gray desktop-style box.");

            string focusBlock = ExtractRuleBlock(uss, "\n.profile-details-field__input .unity-base-field__input:focus {");
            Assert.IsNotNull(focusBlock, "Expected a :focus rule on the input box.");
            StringAssert.Contains("var(--crimson)", focusBlock, "Focused input must use the crimson accent.");

            string invalidBlock = ExtractRuleBlock(uss, "\n.profile-details-field__input--invalid .unity-base-field__input {");
            Assert.IsNotNull(invalidBlock, "Expected an --invalid modifier rule on the input box.");
            StringAssert.Contains("var(--crimson)", invalidBlock, "Invalid input must also read as crimson.");
        }

        [Test]
        public void SaveAndCancelButtons_MeetMobileTouchSizing()
        {
            var screen = Details(BuildTree());
            var cancel = screen.Q<Button>("go-profile");
            var save = screen.Q<Button>("profile-details-save");
            Assert.IsTrue(cancel.ClassListContains("tap-target"), "Cancel must carry the shared touch-target sizing class.");
            Assert.IsTrue(save.ClassListContains("tap-target"), "Save must carry the shared touch-target sizing class.");

            const string ussPath = "Assets/UI/Profile/ProfileDetails.uss";
            string uss = File.ReadAllText(ussPath);
            string btnBlock = ExtractRuleBlock(uss, "\n.profile-details-btn {");
            Assert.IsNotNull(btnBlock);
            var heightMatch = Regex.Match(btnBlock, @"height:\s*(\d+(\.\d+)?)px");
            Assert.IsTrue(heightMatch.Success, "Expected an explicit button height.");
            float height = float.Parse(heightMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(height, 56f, "Save/Cancel must be at least 56px tall for mobile touch sizing.");
        }

        [Test]
        public void Layout_IsCenteredWithASensibleMaxWidth_NotStretchedAcrossTheFullScreen()
        {
            const string ussPath = "Assets/UI/Profile/ProfileDetails.uss";
            string uss = File.ReadAllText(ussPath);
            string layoutBlock = ExtractRuleBlock(uss, "\n.profile-details-layout {");
            Assert.IsNotNull(layoutBlock);
            StringAssert.Contains("max-width:", layoutBlock, "The form must not stretch across the entire 16:9 screen.");
            var maxWidthMatch = Regex.Match(layoutBlock, @"max-width:\s*(\d+(\.\d+)?)px");
            Assert.IsTrue(maxWidthMatch.Success);
            float maxWidth = float.Parse(maxWidthMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.LessOrEqual(maxWidth, 1000f, "Expected a sensible, non-full-bleed max width.");
        }

        /// <summary>Body of the first "{header} {{ ... }}" rule found in a USS file (brace-depth aware).</summary>
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
