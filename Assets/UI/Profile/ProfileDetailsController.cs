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
    /// next time. Save validates every field, persists the whole
    /// <see cref="ProfileUserData"/> as one JSON blob, then navigates back to
    /// "profile" itself (not a "go-" navigator, since a failed validation must not
    /// navigate).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProfileDetailsController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "profileDetails";
        private const string SelectedGenderClass = "profile-details-gender-option--selected";
        private const string ErrorVisibleClass = "profile-details-error--visible";
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
        private Label _errorLabel;
        private Button _saveButton;

        private readonly Button[] _genderButtons = new Button[GenderCount];
        private readonly EventCallback<ClickEvent>[] _genderCallbacks = new EventCallback<ClickEvent>[GenderCount];
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
                    if (_genderButtons[i] != null && _genderCallbacks[i] != null)
                        _genderButtons[i].UnregisterCallback(_genderCallbacks[i]);
                }
            }

            if (_navigator != null)
                _navigator.ScreenChanged -= OnScreenChanged;

            _navigator = null;
            _displayNameField = null;
            _ageField = null;
            _weightField = null;
            _heightField = null;
            _errorLabel = null;
            _saveButton = null;
            for (int i = 0; i < GenderCount; i++)
            {
                _genderButtons[i] = null;
                _genderCallbacks[i] = null;
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
            _errorLabel = root.Q<Label>("profile-details-error");
            _saveButton = root.Q<Button>("profile-details-save");

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
                int index = i;
                EventCallback<ClickEvent> callback = _ => SelectGender(GenderValues[index]);
                _genderButtons[i].RegisterCallback(callback);
                _genderCallbacks[i] = callback;
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
            HideError();
        }

        private void SelectGender(string gender)
        {
            _selectedGender = gender;
            for (int i = 0; i < GenderCount; i++)
            {
                if (_genderButtons[i] == null)
                    continue;
                if (GenderValues[i] == gender)
                    _genderButtons[i].AddToClassList(SelectedGenderClass);
                else
                    _genderButtons[i].RemoveFromClassList(SelectedGenderClass);
            }
        }

        private void OnSaveClicked()
        {
            string displayName = ProfileDisplayNameStorage.Validate(_displayNameField?.value);
            if (displayName == null)
            {
                ShowError("Enter a display name.");
                return;
            }

            if (!ProfileUserDataValidation.IsValidGender(_selectedGender))
            {
                ShowError("Select a gender.");
                return;
            }

            if (!int.TryParse(_ageField?.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int age)
                || !ProfileUserDataValidation.IsValidAge(age))
            {
                ShowError("Enter an age between 10 and 100.");
                return;
            }

            if (!float.TryParse(_weightField?.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float weightKg)
                || !ProfileUserDataValidation.IsValidWeightKg(weightKg))
            {
                ShowError("Enter a weight between 30 and 300 kg.");
                return;
            }

            if (!int.TryParse(_heightField?.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int heightCm)
                || !ProfileUserDataValidation.IsValidHeightCm(heightCm))
            {
                ShowError("Enter a height between 100 and 250 cm.");
                return;
            }

            var data = new ProfileUserData
            {
                DisplayName = displayName,
                Gender = _selectedGender,
                Age = age,
                WeightKg = weightKg,
                HeightCm = heightCm,
            };
            ProfileUserDataStorage.Save(data);

            HideError();
            _navigator?.Show("profile");
        }

        private void ShowError(string message)
        {
            if (_errorLabel == null)
                return;
            _errorLabel.text = message;
            _errorLabel.AddToClassList(ErrorVisibleClass);
        }

        private void HideError() => _errorLabel?.RemoveFromClassList(ErrorVisibleClass);
    }
}
