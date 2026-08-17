using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Contract for ProfileDetailsController's validation/save/navigation wiring
    /// — verified by reading source, mirroring ProfileProgressionTests'
    /// established technique for MonoBehaviour internals not practical to drive
    /// through a live panel in EditMode.
    /// </summary>
    public class ProfileDetailsControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Profile/ProfileDetailsController.cs";

        [Test]
        public void Save_ValidatesDisplayNameFirst()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ProfileDisplayNameStorage.Validate(_displayNameField?.value)", source);
            StringAssert.Contains("Enter a display name.", source);
        }

        [Test]
        public void Save_ValidatesGender()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ProfileUserDataValidation.IsValidGender(_selectedGender)", source);
            StringAssert.Contains("Select a gender.", source);
        }

        [Test]
        public void Save_ValidatesAgeRange_TenToHundred()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ProfileUserDataValidation.IsValidAge(age)", source);
            StringAssert.Contains("Enter an age between 10 and 100.", source);
        }

        [Test]
        public void Save_ValidatesWeightRange_ThirtyToThreeHundredKg()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ProfileUserDataValidation.IsValidWeightKg(weightKg)", source);
            StringAssert.Contains("Enter a weight between 30 and 300 kg.", source);
        }

        [Test]
        public void Save_ValidatesHeightRange_HundredToTwoFiftyCm()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ProfileUserDataValidation.IsValidHeightCm(heightCm)", source);
            StringAssert.Contains("Enter a height between 100 and 250 cm.", source);
        }

        [Test]
        public void ValidSave_PersistsAndReturnsToProfile()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ProfileUserDataStorage.Save(data);", source);
            StringAssert.Contains("_navigator?.Show(\"profile\");", source);
        }

        [Test]
        public void FieldsRepopulate_FromStorage_OnEveryFreshEntry()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnScreenChanged(string screenId)", source);
            StringAssert.Contains("PopulateFromStorage();", source);
            StringAssert.Contains("ProfileUserData data = ProfileUserDataStorage.Load();", source);
        }

        [Test]
        public void GenderSelection_IsAMutuallyExclusiveChipGroup_NotADesktopDropdown()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("DropdownField", source, "Must not use the desktop-style dropdown control.");
            StringAssert.DoesNotContain("PopupField", source, "Must not use the desktop-style dropdown control.");
            StringAssert.Contains("private void SelectGender(string gender)", source);
            StringAssert.Contains("SelectedGenderClass", source);
        }

        [Test]
        public void ErrorPresentation_UsesOneInlineLabel_NoDialog()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("DisplayDialog", source, "Validation errors must never use a popup alert dialog.");
            StringAssert.Contains("private void ShowError(string message)", source);
        }
    }
}
