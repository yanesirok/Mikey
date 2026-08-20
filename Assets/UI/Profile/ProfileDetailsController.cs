using System.Collections;
using System.Globalization;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Drives the "profileDetails" screen: Display Name / Gender / Age / Weight /
    /// Height. Reached only via Profile's "go-profileDetails" edit icon (plain
    /// ScreenManager navigation, no controller code needed there) or
    /// ProfileController's one-time incomplete-profile redirect. Fields are
    /// repopulated from <see cref="ProfileUserDataStorage"/> on every fresh entry
    /// (mirrors OkinawaMapController's ScreenChanged-driven refresh pattern) so
    /// Cancel (a plain "go-profile" navigator — nothing is written until Save, so
    /// there is nothing to explicitly revert) always shows the last-saved state
    /// next time.
    ///
    /// Save validates every field (not just the first failing one) and shows a
    /// specific crimson message under each invalid field rather than one vague
    /// global error, then persists the whole <see cref="ProfileUserData"/> as one
    /// JSON blob and navigates back to "profile" (not itself a "go-" navigator,
    /// since a failed validation must not navigate).
    ///
    /// Gender is wired with Button's own ".clicked" event (matching every other
    /// Button in this app — SettingsModalController, the Attribute popup's Close
    /// button, etc.) rather than a manually-registered ClickEvent callback, and
    /// delegates the "exactly one selected" logic to the pure, unit-tested
    /// <see cref="ProfileDetailsGenderSelection"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProfileDetailsController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "profileDetails";
        private const string SelectedGenderClass = "profile-details-gender-option--selected";
        private const string InvalidInputClass = "profile-details-field__input--invalid";
        private const string InvalidGenderGroupClass = "profile-details-gender-group--invalid";
        private const string ErrorVisibleClass = "profile-details-field__error--visible";
        private const int GenderCount = 4;

        private static readonly string[] GenderValues =
        {
            ProfileUserData.GenderMale, ProfileUserData.GenderFemale, ProfileUserData.GenderOther, ProfileUserData.GenderPreferNotToSay,
        };
        private static readonly string[] GenderButtonNames =
        {
            "profile-details-gender-male", "profile-details-gender-female", "profile-details-gender-other", "profile-details-gender-undisclosed",
        };

        private TextField _displayNameField;
        private TextField _ageField;
        private TextField _weightField;
        private TextField _heightField;
        private VisualElement _genderGroup;
        private Button _saveButton;

        private Label _displayNameError;
        private Label _genderError;
        private Label _ageError;
        private Label _weightError;
        private Label _heightError;

        private readonly Button[] _genderButtons = new Button[GenderCount];
        private readonly System.Action[] _genderClickedHandlers = new System.Action[GenderCount];
        private string _selectedGender = string.Empty;

        private IScreenNavigator _navigator;
        private Coroutine _bindRoutine;
        private bool _bound;

        private void OnEnable()
        {
            if (_bound)
                return;
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            if (_bound)
            {
                if (_saveButton != null)
                    _saveButton.clicked -= OnSaveClicked;

                for (int i = 0; i < GenderCount; i++)
                {
                    if (_genderButtons[i] != null && _genderClickedHandlers[i] != null)
                        _genderButtons[i].clicked -= _genderClickedHandlers[i];
                }
            }

            if (_navigator != null)
                _navigator.ScreenChanged -= OnScreenChanged;

            _navigator = null;
            _displayNameField = null;
            _ageField = null;
            _weightField = null;
            _heightField = null;
            _genderGroup = null;
            _saveButton = null;
            _displayNameError = null;
            _genderError = null;
            _ageError = null;
            _weightError = null;
            _heightError = null;
            for (int i = 0; i < GenderCount; i++)
            {
                _genderButtons[i] = null;
                _genderClickedHandlers[i] = null;
            }
            _selectedGender = string.Empty;
            _bound = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[ProfileDetailsController] UIDocument root unavailable; Profile Details not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _displayNameField = root.Q<TextField>("profile-details-display-name");
            _ageField = root.Q<TextField>("profile-details-age");
            _weightField = root.Q<TextField>("profile-details-weight");
            _heightField = root.Q<TextField>("profile-details-height");
            _genderGroup = root.Q<VisualElement>("profile-details-gender-group");
            _saveButton = root.Q<Button>("profile-details-save");

            _displayNameError = root.Q<Label>("profile-details-display-name-error");
            _genderError = root.Q<Label>("profile-details-gender-error");
            _ageError = root.Q<Label>("profile-details-age-error");
            _weightError = root.Q<Label>("profile-details-weight-error");
            _heightError = root.Q<Label>("profile-details-height-error");

            for (int i = 0; i < GenderCount; i++)
                _genderButtons[i] = root.Q<Button>(GenderButtonNames[i]);

            if (_displayNameField == null || _saveButton == null)
            {
                Debug.LogError("[ProfileDetailsController] Profile Details elements missing; not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            for (int i = 0; i < GenderCount; i++)
            {
                if (_genderButtons[i] == null)
                    continue;
                int index = i; // captured once per iteration — a fresh local, not the shared loop variable
                _genderClickedHandlers[i] = () => SelectGender(GenderValues[index]);
                _genderButtons[i].clicked += _genderClickedHandlers[i];
            }

            _saveButton.clicked += OnSaveClicked;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                if (_navigator.CurrentScreen == ScreenId)
                    PopulateFromStorage();
            }

            _bound = true;
            _bindRoutine = null;
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
                PopulateFromStorage();
        }

        private void PopulateFromStorage()
        {
            ProfileUserData data = ProfileUserDataStorage.Load();

            if (_displayNameField != null)
                _displayNameField.value = data.DisplayName;
            if (_ageField != null)
                _ageField.value = data.Age > 0 ? data.Age.ToString(CultureInfo.InvariantCulture) : string.Empty;
            if (_weightField != null)
                _weightField.value = data.WeightKg > 0f ? data.WeightKg.ToString(CultureInfo.InvariantCulture) : string.Empty;
            if (_heightField != null)
                _heightField.value = data.HeightCm > 0 ? data.HeightCm.ToString(CultureInfo.InvariantCulture) : string.Empty;

            SelectGender(data.Gender);
            ClearAllFieldErrors();
        }

        /// <summary>Updates the selected gender value and each chip's visual state via the pure, unit-tested selection logic.</summary>
        private void SelectGender(string gender)
        {
            _selectedGender = gender;
            bool[] selected = ProfileDetailsGenderSelection.ComputeSelectedFlags(GenderValues, gender);
            for (int i = 0; i < GenderCount; i++)
            {
                if (_genderButtons[i] == null)
                    continue;
                if (selected[i])
                    _genderButtons[i].AddToClassList(SelectedGenderClass);
                else
                    _genderButtons[i].RemoveFromClassList(SelectedGenderClass);
            }

            _genderGroup?.RemoveFromClassList(InvalidGenderGroupClass);
            HideFieldError(_genderError);
        }

        /// <summary>
        /// Validates every field (not just the first failure) so every invalid
        /// field is highlighted at once, then saves only if all five pass.
        /// </summary>
        private void OnSaveClicked()
        {
            ClearAllFieldErrors();

            string displayName = ProfileDisplayNameStorage.Validate(_displayNameField?.value);
            bool displayNameValid = displayName != null;
            if (!displayNameValid)
                MarkFieldInvalid(_displayNameField, _displayNameError, "Display name is required.");

            bool genderValid = ProfileUserDataValidation.IsValidGender(_selectedGender);
            if (!genderValid)
                MarkGenderInvalid("Select a gender.");

            bool ageParsed = int.TryParse(_ageField?.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int age);
            bool ageValid = ageParsed && ProfileUserDataValidation.IsValidAge(age);
            if (!ageValid)
                MarkFieldInvalid(_ageField, _ageError, "Age must be between 10 and 100.");

            bool weightParsed = float.TryParse(_weightField?.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float weightKg);
            bool weightValid = weightParsed && ProfileUserDataValidation.IsValidWeightKg(weightKg);
            if (!weightValid)
                MarkFieldInvalid(_weightField, _weightError, "Weight must be between 30 and 300 kg.");

            bool heightParsed = int.TryParse(_heightField?.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int heightCm);
            bool heightValid = heightParsed && ProfileUserDataValidation.IsValidHeightCm(heightCm);
            if (!heightValid)
                MarkFieldInvalid(_heightField, _heightError, "Height must be between 100 and 250 cm.");

            if (!displayNameValid || !genderValid || !ageValid || !weightValid || !heightValid)
                return;

            var data = new ProfileUserData
            {
                DisplayName = displayName,
                Gender = _selectedGender,
                Age = age,
                WeightKg = weightKg,
                HeightCm = heightCm,
            };
            ProfileUserDataStorage.Save(data);

            _navigator?.Show("profile");
        }

        private void ClearAllFieldErrors()
        {
            _displayNameField?.RemoveFromClassList(InvalidInputClass);
            _ageField?.RemoveFromClassList(InvalidInputClass);
            _weightField?.RemoveFromClassList(InvalidInputClass);
            _heightField?.RemoveFromClassList(InvalidInputClass);
            _genderGroup?.RemoveFromClassList(InvalidGenderGroupClass);

            HideFieldError(_displayNameError);
            HideFieldError(_genderError);
            HideFieldError(_ageError);
            HideFieldError(_weightError);
            HideFieldError(_heightError);
        }

        private static void MarkFieldInvalid(VisualElement input, Label error, string message)
        {
            input?.AddToClassList(InvalidInputClass);
            ShowFieldError(error, message);
        }

        private void MarkGenderInvalid(string message)
        {
            _genderGroup?.AddToClassList(InvalidGenderGroupClass);
            ShowFieldError(_genderError, message);
        }

        private static void ShowFieldError(Label error, string message)
        {
            if (error == null)
                return;
            error.text = message;
            error.AddToClassList(ErrorVisibleClass);
        }

        private static void HideFieldError(Label error) => error?.RemoveFromClassList(ErrorVisibleClass);
    }
}
