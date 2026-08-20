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
    /// <see cref="ProfileRadarChart"/>), owns the Attribute Details popup (opened
    /// by tapping the radar — a Profile-local overlay, no new screen id), and
    /// keeps the displayed name in sync with <see cref="ProfileUserDataStorage"/>
    /// (the edit icon itself is a plain "go-profileDetails" navigator — see
    /// MikeyApp.uxml — needing no click wiring here at all). The Attribute popup
    /// never touches <see cref="IScreenNavigator"/>, so opening/closing it can
    /// never replay the radar's entrance animation — that only reacts to
    /// <see cref="IScreenNavigator.ScreenChanged"/> firing for "profile" (mirrors
    /// OkinawaMapController's OnEnteredScreen pattern), which is also where the
    /// name refresh and the one-time incomplete-profile redirect to
    /// "profileDetails" live (see <see cref="HandleProfileEntered"/>). Every other
    /// Profile number (LVL, XP, streak, chapter progress, radar values) is
    /// frontend mock data centralized here; see the class-level REAL/PLACEHOLDER
    /// split in the PR description.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProfileController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "profile";
        private const string ProfileDetailsScreenId = "profileDetails";
        private const string PopupOpenClass = "profile-popup--open";
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

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

        private EventCallback<ClickEvent> _navMapClickCallback;
        private EventCallback<ClickEvent> _navTechniquesClickCallback;
        private EventCallback<ClickEvent> _radarClickCallback;
        private EventCallback<ClickEvent> _attributeScrimClickCallback;

        private Coroutine _bindRoutine;
        private Coroutine _radarEntranceRoutine;
        private bool _bound;

        /// <summary>
        /// In-memory only (never persisted): true once we've either auto-redirected
        /// to Profile Details this session or the profile was already complete on
        /// first check. Guarantees the incomplete-profile redirect fires at most
        /// once per app session — the player can always Cancel out of Profile
        /// Details and simply browse Profile afterward without being bounced back.
        /// </summary>
        private bool _profileDetailsRedirectOffered;

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

            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                if (_navigator.CurrentScreen == ScreenId)
                    HandleProfileEntered();
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
                HandleProfileEntered();
        }

        /// <summary>
        /// Runs on every fresh entry to "profile": refreshes the displayed name
        /// from storage (it may have just changed in Profile Details) and, at most
        /// once per session, redirects to Profile Details if the data has never
        /// been completed — see <see cref="_profileDetailsRedirectOffered"/>.
        /// </summary>
        private void HandleProfileEntered()
        {
            RefreshDisplayName();

            if (!_profileDetailsRedirectOffered)
            {
                _profileDetailsRedirectOffered = true;
                if (!ProfileUserDataStorage.IsComplete(ProfileUserDataStorage.Load()))
                {
                    _navigator?.Show(ProfileDetailsScreenId);
                    return; // leaving again immediately; skip the entrance animation this pass
                }
            }

            PlayRadarEntrance();
        }

        private void RefreshDisplayName()
        {
            if (_displayNameLabel != null)
                _displayNameLabel.text = ProfileUserDataStorage.Load().DisplayName;
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
    }
}
