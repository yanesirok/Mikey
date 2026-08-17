using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Contract for ProfileDetailsController's validation/save/navigation wiring
    /// — verified by reading source, mirroring ProfileProgressionTests'
    /// established technique for MonoBehaviour internals not practical to drive
    /// through a live panel in EditMode. Validation ranges themselves (10-100 /
    /// 30-300 / 100-250) are covered by ProfileUserDataValidationTests; this file
    /// checks that the controller wires those checks up and surfaces a specific
    /// field-level message for each, not that the ranges are correct.
    /// </summary>
    public class ProfileDetailsControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Profile/ProfileDetailsController.cs";

        [Test]
        public void Save_ValidatesEveryField_NotJustTheFirstFailure()
        {
            string source = File.ReadAllText(SourcePath);
            string body = ExtractMethodBody(source, "OnSaveClicked");
            Assert.IsNotNull(body, "Expected an OnSaveClicked method.");

            // All five checks must run unconditionally (no early "return" between
            // them) so every invalid field is highlighted at once, per the
            // explicit "not just markup tests" / "clear per-field errors" ask.
            StringAssert.Contains("ProfileDisplayNameStorage.Validate(_displayNameField?.value)", body);
            StringAssert.Contains("ProfileUserDataValidation.IsValidGender(_selectedGender)", body);
            StringAssert.Contains("ProfileUserDataValidation.IsValidAge(age)", body);
            StringAssert.Contains("ProfileUserDataValidation.IsValidWeightKg(weightKg)", body);
            StringAssert.Contains("ProfileUserDataValidation.IsValidHeightCm(heightCm)", body);

            int firstCheckIndex = body.IndexOf("ProfileDisplayNameStorage.Validate", System.StringComparison.Ordinal);
            int lastCheckIndex = body.IndexOf("ProfileUserDataValidation.IsValidHeightCm", System.StringComparison.Ordinal);
            string betweenChecks = body.Substring(firstCheckIndex, lastCheckIndex - firstCheckIndex);
            StringAssert.DoesNotContain("return;", betweenChecks,
                "Validation must not stop at the first failing field.");
        }

        [Test]
        public void Save_ShowsExactFieldSpecificMessages_ForEachInvalidField()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("\"Display name is required.\"", source);
            StringAssert.Contains("\"Select a gender.\"", source);
            StringAssert.Contains("\"Age must be between 10 and 100.\"", source);
            StringAssert.Contains("\"Weight must be between 30 and 300 kg.\"", source);
            StringAssert.Contains("\"Height must be between 100 and 250 cm.\"", source);
        }

        [Test]
        public void Save_OnlyPersistsAndNavigates_WhenAllFiveFieldsAreValid()
        {
            string source = File.ReadAllText(SourcePath);
            string body = ExtractMethodBody(source, "OnSaveClicked");
            Assert.IsNotNull(body);

            var guardMatch = System.Text.RegularExpressions.Regex.Match(
                body,
                @"if\s*\(!displayNameValid\s*\|\|\s*!genderValid\s*\|\|\s*!ageValid\s*\|\|\s*!weightValid\s*\|\|\s*!heightValid\)\s*return;");
            Assert.IsTrue(guardMatch.Success, "Save must guard on all five validity flags before persisting.");

            string afterGuard = body.Substring(guardMatch.Index);
            StringAssert.Contains("ProfileUserDataStorage.Save(data);", afterGuard);
            StringAssert.Contains("_navigator?.Show(\"profile\");", afterGuard);
        }

        [Test]
        public void Save_ClearsAllFieldErrors_BeforeRevalidating()
        {
            string source = File.ReadAllText(SourcePath);
            string body = ExtractMethodBody(source, "OnSaveClicked");
            Assert.IsNotNull(body);
            StringAssert.Contains("ClearAllFieldErrors();", body);
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
        public void GenderButtons_WireTheirOwnClickedEvent_LikeEveryOtherButtonInTheApp()
        {
            string source = File.ReadAllText(SourcePath);
            // Matches the ".clicked +=" idiom used by every other Button in the
            // codebase (SettingsModalController, the Attribute popup's Close
            // button, the Save button here) instead of a manually-registered
            // ClickEvent callback.
            StringAssert.Contains("_genderButtons[i].clicked += _genderClickedHandlers[i];", source);
            StringAssert.Contains("_genderButtons[i].clicked -= _genderClickedHandlers[i];", source,
                "Handlers registered in OnEnable/bind must be unregistered in OnDisable to avoid leaks/double-fires.");
            StringAssert.DoesNotContain("DropdownField", source, "Must not use the desktop-style dropdown control.");
            StringAssert.DoesNotContain("PopupField", source, "Must not use the desktop-style dropdown control.");
        }

        [Test]
        public void GenderSelection_DelegatesToThePureTestedHelper_NotAReimplementedLoop()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void SelectGender(string gender)", source);
            string body = ExtractMethodBody(source, "SelectGender");
            Assert.IsNotNull(body);
            StringAssert.Contains("ProfileDetailsGenderSelection.ComputeSelectedFlags(GenderValues, gender)", body);
            StringAssert.Contains("SelectedGenderClass", body);
        }

        [Test]
        public void SelectGender_UpdatesTheUnderlyingSelectedGenderField_ThatSaveReads()
        {
            string source = File.ReadAllText(SourcePath);
            string selectBody = ExtractMethodBody(source, "SelectGender");
            Assert.IsNotNull(selectBody);
            StringAssert.Contains("_selectedGender = gender;", selectBody);

            string saveBody = ExtractMethodBody(source, "OnSaveClicked");
            Assert.IsNotNull(saveBody);
            StringAssert.Contains("Gender = _selectedGender,", saveBody,
                "Save must persist the same field SelectGender writes, not a separate/stale value.");
        }

        [Test]
        public void ErrorPresentation_IsPerField_NoDialog_NoSingleGlobalLabel()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("DisplayDialog", source, "Validation errors must never use a popup alert dialog.");
            StringAssert.DoesNotContain("private void ShowError(string message)", source,
                "The old single global error method must be replaced by per-field error handling.");
            StringAssert.Contains("private static void MarkFieldInvalid(VisualElement input, Label error, string message)", source);
            StringAssert.Contains("private void MarkGenderInvalid(string message)", source);
            StringAssert.Contains("private void ClearAllFieldErrors()", source);
        }

        /// <summary>
        /// Extracts a method's body, anchored on its DECLARATION ("private ...
        /// Name(" — never a call site elsewhere in the file). Handles both brace
        /// bodies and this file's expression-bodied ("=> ...;") one-liners.
        /// </summary>
        private static string ExtractMethodBody(string source, string methodName)
        {
            int declIndex = -1;
            foreach (var prefix in new[] { "private void " + methodName + "(", "private static void " + methodName + "(" })
            {
                declIndex = source.IndexOf(prefix, System.StringComparison.Ordinal);
                if (declIndex >= 0)
                    break;
            }
            if (declIndex < 0)
                return null;

            int parenClose = source.IndexOf(')', declIndex);
            if (parenClose < 0)
                return null;

            int cursor = parenClose + 1;
            while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
                cursor++;

            if (cursor < source.Length && source[cursor] == '{')
            {
                int depth = 0;
                for (int i = cursor; i < source.Length; i++)
                {
                    if (source[i] == '{')
                        depth++;
                    else if (source[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return source.Substring(cursor + 1, i - cursor - 1);
                    }
                }
                return null;
            }

            if (cursor + 1 < source.Length && source[cursor] == '=' && source[cursor + 1] == '>')
            {
                int semicolon = source.IndexOf(';', cursor);
                return semicolon < 0 ? null : source.Substring(cursor, semicolon - cursor);
            }

            return null;
        }
    }
}
