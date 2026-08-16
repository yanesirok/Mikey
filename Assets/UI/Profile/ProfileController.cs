using System.Collections;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Drives the redesigned Profile screen: gates the shared top HUD's Map/
    /// Techniques nav items behind the same Level1Unlocked progression check Home
    /// already uses (see <see cref="TutorialProgressPresenter"/>) — unchanged from
    /// before, just relocated from the old bottom dock into the HUD (same element
    /// names, "profile-nav-map"/"profile-nav-techniques", so this gating needed no
    /// rework) — and mounts/animates the capability radar (see
    /// <see cref="ProfileRadarChart"/>). Every Profile number below (LVL, XP,
    /// streak, chapter progress, radar values) is frontend mock data centralized
    /// here; see the class-level REAL/PLACEHOLDER split in the PR description.
    /// The radar's collapse -> target entrance animation replays on every fresh
    /// navigation entry to "profile" (via <see cref="IScreenNavigator.ScreenChanged"/>,
    /// mirroring OkinawaMapController's OnEnteredScreen pattern) but never on
    /// Settings modal open/close, which never fires that event.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProfileController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "profile";
        private const float RadarEntranceSeconds = 0.65f;
        private const float RadarMaxValue = 100f;

        /// <summary>Strength, Speed, Form, Stamina, Control — see <see cref="ProfileRadarMath.AxisLabels"/> for the matching order.</summary>
        private static readonly float[] RadarValues = { 60f, 50f, 45f, 55f, 40f };

        private VisualElement _navMap;
        private VisualElement _navTechniques;
        private VisualElement _radarMount;
        private ProfileRadarChart _radarChart;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

        private EventCallback<ClickEvent> _navMapClickCallback;
        private EventCallback<ClickEvent> _navTechniquesClickCallback;

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
            }

            if (_navigator != null)
                _navigator.ScreenChanged -= OnScreenChanged;

            _navigator = null;
            _progress = null;
            _navMap = null;
            _navTechniques = null;
            _radarMount = null;
            _radarChart = null;
            _navMapClickCallback = null;
            _navTechniquesClickCallback = null;
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
                    Debug.LogError("[ProfileController] UIDocument root unavailable; Profile navigation gating not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _navMap = root.Q<VisualElement>("profile-nav-map");
            _navTechniques = root.Q<VisualElement>("profile-nav-techniques");
            _radarMount = root.Q<VisualElement>("profile-radar-mount");

            if (_navMap == null || _navTechniques == null)
            {
                Debug.LogError("[ProfileController] Profile dock elements missing; navigation gating not bound.", this);
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
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                if (_navigator.CurrentScreen == ScreenId)
                    PlayRadarEntrance();
            }

            _bound = true;
            _bindRoutine = null;
        }

        private void OnNavMapClicked()
        {
            if (_progress == null || _navigator == null)
                return;
            if (!TutorialProgressPresenter.IsMapUnlocked(_progress.State))
                return; // Locked: Combine (LVL0) must be completed first.

            _navigator.Show("map");
        }

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
    }
}
