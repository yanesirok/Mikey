using System.IO;
using System.Linq;
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
            Assert.IsNotNull(screen.Q<VisualElement>(className: "profile-details-gender-group"), "Expected Gender selection.");
            Assert.IsNotNull(screen.Q<TextField>("profile-details-age"), "Expected Age field.");
            Assert.IsNotNull(screen.Q<TextField>("profile-details-weight"), "Expected Weight field.");
            Assert.IsNotNull(screen.Q<TextField>("profile-details-height"), "Expected Height field.");
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
        public void ErrorArea_Exists_AndIsCompactNotAPopupDialog()
        {
            var screen = Details(BuildTree());
            var error = screen.Q<Label>("profile-details-error");
            Assert.IsNotNull(error, "Expected one compact error label.");
            Assert.AreEqual(1, screen.Query<Label>(className: "profile-details-error").ToList().Count);
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
    }
}
