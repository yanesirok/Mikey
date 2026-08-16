using System.Collections;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Drives the redesigned Profile screen: the shared top HUD's Map link is a
    /// plain, unconditional navigation to "map" — same behavior as Main Menu's
    /// "go-map" PLAY button (see HomeControllerSourceTests,
    /// "HomeController must not special-case it") — because gating it behind
    /// Level1Unlocked made it a dead duplicate whenever Menu's own PLAY already
    /// let the player through. Techniques keeps its existing
    /// <see cref="TutorialProgressPresenter.IsTechniquesUnlocked"/> gate,
    /// unchanged. Also mounts/animates the capability radar (see
    /// <see cref="ProfileRadarChart"/>), and owns two Profile-LOCAL overlays (no
    /// new screen ids): the Attribute Details popup (opened by tapping the radar)
    /// and the username edit popup (opened by the edit icon next to the
    /// displayed name, backed by <see cref="ProfileDisplayNameStorage"/>'s one
    /// approved PlayerPrefs key). Neither popup ever touches
    /// <see cref="IScreenNavigator"/>, so opening/closing them can never replay
    /// the radar's entrance animation — that only reacts to
    /// <see cref="IScreenNavigator.ScreenChanged"/> firing for "profile" (mirrors
    /// OkinawaMapController's OnEnteredScreen pattern). Every Profile number
    /// below (LVL, XP, streak, chapter progress, radar values) is frontend mock
    /// data centralized here; see the class-level REAL/PLACEHOLDER split in the
    /// PR description.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProfileController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "profile";
        private const string PopupOpenClass = "profile-popup--open";
        private const string UsernameErrorVisibleClass = "profile-username-edit__error--visible";
        private const float RadarEntranceSeconds = 0.65f;
        private const float RadarMaxValue = 100f;

        /// <summary>Strength, Speed, Form, Stamina, Control — see <see cref="ProfileRadarMath.AxisLabels"/> for the matching order.</summary>
        private static readonly float[] RadarValues = { 60f, 50f, 45f, 55f, 40f };

        private VisualElement _navMap;
        private VisualElement _navTechniques;
        private VisualElement _radarMount;
        private ProfileRadarChart _radarChart;

        private VisualElement _attributePopup;
        private VisualElement _attributePopupScrim;
        private Button _attributePopupClose;

        private Label _displayNameLabel;
        private Button _nameEditOpenButton;
        private VisualElement _usernameEditPopup;
        private TextField _usernameField;
        private Label _usernameError;
        private Button _usernameSaveButton;
        private Button _usernameCancelButton;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

        private EventCallback<ClickEvent> _navMapClickCallback;
        private EventCallback<ClickEvent> _navTechniquesClickCallback;
        private EventCallback<ClickEvent> _radarClickCallback;
        private EventCallback<ClickEvent> _attributeScrimClickCallback;

        private Coroutine _bindRoutine;
        private Coroutine _radarEntranceRoutine;
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
            if (_radarEntranceRoutine != null)
            {
                StopCoroutine(_radarEntranceRoutine);
                _radarEntranceRoutine = null;
            }

            if (_bound)
            {
                if (_navMap != null && _navMapClickCallback != null)
                    _navMap.UnregisterCallback(_navMapClickCallback);
                if (_navTechniques != null && _navTechniquesClickCallback != null)
                    _navTechniques.UnregisterCallback(_navTechniquesClickCallback);
                if (_radarMount != null && _radarClickCallback != null)
                    _radarMount.UnregisterCallback(_radarClickCallback);
                if (_attributePopupScrim != null && _attributeScrimClickCallback != null)
                    _attributePopupScrim.UnregisterCallback(_attributeScrimClickCallback);
                if (_attributePopupClose != null)
                    _attributePopupClose.clicked -= CloseAttributePopup;
                if (_nameEditOpenButton != null)
                    _nameEditOpenButton.clicked -= OpenUsernameEditPopup;
                if (_usernameSaveButton != null)
                    _usernameSaveButton.clicked -= OnUsernameSaveClicked;
                if (_usernameCancelButton != null)
                    _usernameCancelButton.clicked -= CloseUsernameEditPopup;
            }

            if (_navigator != null)
                _navigator.ScreenChanged -= OnScreenChanged;

            _navigator = null;
            _progress = null;
            _navMap = null;
            _navTechniques = null;
            _radarMount = null;
            _radarChart = null;
            _attributePopup = null;
            _attributePopupScrim = null;
            _attributePopupClose = null;
            _displayNameLabel = null;
            _nameEditOpenButton = null;
            _usernameEditPopup = null;
            _usernameField = null;
            _usernameError = null;
            _usernameSaveButton = null;
            _usernameCancelButton = null;
            _navMapClickCallback = null;
            _navTechniquesClickCallback = null;
            _radarClickCallback = null;
            _attributeScrimClickCallback = null;
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
                    Debug.LogError("[ProfileController] UIDocument root unavailable; Profile not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _navMap = root.Q<VisualElement>("profile-nav-map");
            _navTechniques = root.Q<VisualElement>("profile-nav-techniques");
            _radarMount = root.Q<VisualElement>("profile-radar-mount");

            _attributePopup = root.Q<VisualElement>("profile-attribute-popup");
            _attributePopupScrim = root.Q<VisualElement>("profile-attribute-popup-scrim");
            _attributePopupClose = root.Q<Button>("profile-attribute-popup-close");

            _displayNameLabel = root.Q<Label>("profile-display-name");
            _nameEditOpenButton = root.Q<Button>("profile-name-edit-open");
            _usernameEditPopup = root.Q<VisualElement>("profile-username-edit-popup");
            _usernameField = root.Q<TextField>("profile-username-edit-field");
            _usernameError = root.Q<Label>("profile-username-edit-error");
            _usernameSaveButton = root.Q<Button>("profile-username-edit-save");
            _usernameCancelButton = root.Q<Button>("profile-username-edit-cancel");

            if (_navMap == null || _navTechniques == null)
            {
                Debug.LogError("[ProfileController] Profile HUD elements missing; navigation not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _navigator = GetComponent<IScreenNavigator>();
            _progress = GetComponent<ITutorialProgress>();

            _navMapClickCallback = _ => OnNavMapClicked();
            _navMap.RegisterCallback(_navMapClickCallback);

            _navTechniquesClickCallback = _ => OnNavTechniquesClicked();
            _navTechniques.RegisterCallback(_navTechniquesClickCallback);

            if (_radarMount != null)
            {
                _radarChart = new ProfileRadarChart();
                _radarChart.style.width = new Length(100, LengthUnit.Percent);
                _radarChart.style.height = new Length(100, LengthUnit.Percent);
                _radarChart.SetValues(RadarValues, RadarMaxValue);
                _radarMount.Add(_radarChart);

                _radarClickCallback = _ => ToggleAttributePopup();
                _radarMount.RegisterCallback(_radarClickCallback);
            }

            if (_attributePopupScrim != null)
            {
                _attributeScrimClickCallback = _ => CloseAttributePopup();
                _attributePopupScrim.RegisterCallback(_attributeScrimClickCallback);
            }
            if (_attributePopupClose != null)
                _attributePopupClose.clicked += CloseAttributePopup;

            if (_displayNameLabel != null)
                _displayNameLabel.text = ProfileDisplayNameStorage.Load();
            if (_nameEditOpenButton != null)
                _nameEditOpenButton.clicked += OpenUsernameEditPopup;
            if (_usernameSaveButton != null)
                _usernameSaveButton.clicked += OnUsernameSaveClicked;
            if (_usernameCancelButton != null)
                _usernameCancelButton.clicked += CloseUsernameEditPopup;

            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                if (_navigator.CurrentScreen == ScreenId)
                    PlayRadarEntrance();
            }

            _bound = true;
            _bindRoutine = null;
        }

        /// <summary>Plain, unconditional navigation — mirrors Main Menu's "go-map" PLAY button, never gated.</summary>
        private void OnNavMapClicked() => _navigator?.Show("map");

        private void OnNavTechniquesClicked()
        {
            if (_progress == null || _navigator == null)
                return;
            if (!TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))
                return; // Locked: Combine (LVL0) must be completed first.

            _navigator.Show("techniques");
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
                PlayRadarEntrance();
        }

        private void PlayRadarEntrance()
        {
            if (_radarChart == null)
                return;

            if (_radarEntranceRoutine != null)
                StopCoroutine(_radarEntranceRoutine);

            _radarChart.Progress = 0f;
            _radarEntranceRoutine = StartCoroutine(AnimateRadarEntrance());
        }

        private IEnumerator AnimateRadarEntrance()
        {
            float elapsed = 0f;
            while (elapsed < RadarEntranceSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / RadarEntranceSeconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic, no overshoot/bounce
                _radarChart.Progress = eased;
                yield return null;
            }

            _radarChart.Progress = 1f;
            _radarEntranceRoutine = null;
        }

        // ---------- Attribute Details popup (Profile-local overlay) ----------

        private void ToggleAttributePopup()
        {
            if (_attributePopup == null)
                return;

            if (_attributePopup.ClassListContains(PopupOpenClass))
                CloseAttributePopup();
            else
                OpenAttributePopup();
        }

        private void OpenAttributePopup() => _attributePopup?.AddToClassList(PopupOpenClass);

        private void CloseAttributePopup() => _attributePopup?.RemoveFromClassList(PopupOpenClass);

        // ---------- username edit popup (Profile-local overlay) ----------

        private void OpenUsernameEditPopup()
        {
            if (_usernameField != null)
                _usernameField.value = _displayNameLabel != null ? _displayNameLabel.text : ProfileDisplayNameStorage.DefaultDisplayName;

            HideUsernameError();
            _usernameEditPopup?.AddToClassList(PopupOpenClass);
        }

        private void CloseUsernameEditPopup() => _usernameEditPopup?.RemoveFromClassList(PopupOpenClass);

        private void OnUsernameSaveClicked()
        {
            string validated = ProfileDisplayNameStorage.Validate(_usernameField?.value);
            if (validated == null)
            {
                ShowUsernameError();
                return;
            }

            ProfileDisplayNameStorage.Save(validated);
            if (_displayNameLabel != null)
                _displayNameLabel.text = validated;
            CloseUsernameEditPopup();
        }

        private void ShowUsernameError() => _usernameError?.AddToClassList(UsernameErrorVisibleClass);

        private void HideUsernameError() => _usernameError?.RemoveFromClassList(UsernameErrorVisibleClass);
    }
}
