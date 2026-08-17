using System;
using System.Collections;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives the Okinawa chapter map screen ("mapOkinawa"): LVL 0-6 marker
    /// selection, the shared level detail overlay popup, the top quick-access
    /// bar, and completing the ink-fade begun by JapanMapController on entry.
    /// The top bar's Settings button opens the one shared Settings modal —
    /// this controller doesn't wire it at all (see
    /// Mikey.UI.Settings.SettingsModalController, which finds
    /// "okinawa-topbar-settings" itself). LVL 0 is always unlocked and routes
    /// to the existing assessment intro ("combineIntro"); LVL 1 unlocks once
    /// <see cref="TutorialProgressState.Level1Unlocked"/> is reached and
    /// routes to the existing lesson/techniques flow ("techniques",
    /// mirroring the old MapLevelPreviewController's StartLessonTarget);
    /// LVL 2-6 are always locked placeholders — no gameplay built for them
    /// yet. Okinawa's final MVP mission set is exactly 7 missions (1 Special
    /// + 3 Training + 2 Fight + 1 Boss Fight, see MapMarkerLayout.Missions).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OkinawaMapController : MonoBehaviour
    {
        private const int LevelCount = 7;
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "mapOkinawa";

        public const string CombineIntroScreenId = "combineIntro";
        public const string TechniquesScreenId = "techniques";
        public const string JapanMapScreenId = "map";

        private const string SelectedNodeClass = "level-node--selected";
        private const string LockedNodeClass = "level-node--locked";
        private const string PanelOpenClass = "detail-panel--open";
        private const string LockedCtaClass = "detail-panel__cta--locked";
        private const string TransitionVisibleClass = "map-transition-overlay--visible";
        private const string NavLockedClass = "map-topbar__nav-btn--locked";

        private VisualElement _root;
        private readonly Button[] _levelNodes = new Button[LevelCount];
        private VisualElement _outsideCatcher;
        private VisualElement _panel;
        private Label _panelEyebrow;
        private Label _panelTitle;
        private Label _panelSubtitle;
        private Label _panelDesc;
        private Button _panelCta;
        private Label _panelCtaText;
        private VisualElement _transitionOverlay;

        private Button _topbarMap;
        private Button _topbarTechniques;
        private Button _topbarStats;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

        private int _selectedLevel = -1;
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
                for (int i = 0; i < LevelCount; i++)
                {
                    if (_levelNodes[i] != null)
                        _levelNodes[i].clicked -= _levelClickHandlers[i];
                }
                _outsideCatcher.UnregisterCallback<PointerDownEvent>(OnOutsideCatcherPointerDown);
                _panelCta.clicked -= OnLevelCtaClicked;
                if (_topbarMap != null)
                    _topbarMap.clicked -= OnTopbarMapClicked;
                if (_topbarTechniques != null)
                    _topbarTechniques.clicked -= OnTopbarTechniquesClicked;
                if (_topbarStats != null)
                    _topbarStats.clicked -= OnTopbarStatsClicked;
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            if (_progress != null)
            {
                _progress.Changed -= OnProgressChanged;
                _progress = null;
            }

            _selectedLevel = -1;
            _bound = false;
        }

        private readonly Action[] _levelClickHandlers = new Action[LevelCount];

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[OkinawaMapController] UIDocument root unavailable; Okinawa map not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;

            for (int i = 0; i < LevelCount; i++)
                _levelNodes[i] = _root.Q<Button>($"level-node-{i}");

            _outsideCatcher = _root.Q<VisualElement>("okinawa-outside-catcher");
            _panel = _root.Q<VisualElement>("level-panel");
            _panelEyebrow = _root.Q<Label>("level-panel-eyebrow");
            _panelTitle = _root.Q<Label>("level-panel-title");
            _panelSubtitle = _root.Q<Label>("level-panel-subtitle");
            _panelDesc = _root.Q<Label>("level-panel-desc");
            _panelCta = _root.Q<Button>("level-panel-cta");
            _panelCtaText = _root.Q<Label>("level-panel-cta-text");
            _transitionOverlay = _root.Q<VisualElement>("okinawa-transition-overlay");

            _topbarMap = _root.Q<Button>("okinawa-topbar-map");
            _topbarTechniques = _root.Q<Button>("okinawa-topbar-techniques");
            _topbarStats = _root.Q<Button>("okinawa-topbar-stats");

            if (_panel == null || _panelCta == null || _levelNodes[0] == null)
            {
                Debug.LogError("[OkinawaMapController] Okinawa map elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            // Marker positions are stored as SOURCE-IMAGE-normalized
            // coordinates (see MapMarkerLayout) and must be converted
            // through the current viewport size to land correctly under the
            // map art's cover-fit crop — never applied as a raw percentage.
            var canvas = _root.Q<VisualElement>("okinawa-canvas");
            float viewportWidth = canvas?.resolvedStyle.width ?? 0f;
            float viewportHeight = canvas?.resolvedStyle.height ?? 0f;

            for (int i = 0; i < LevelCount; i++)
            {
                if (_levelNodes[i] == null)
                    continue;
                int levelIndex = i;
                Action handler = () => OnLevelNodeClicked(levelIndex);
                _levelClickHandlers[i] = handler;
                _levelNodes[i].clicked += handler;
                ApplyMissionLayout(_levelNodes[i], levelIndex, viewportWidth, viewportHeight);
            }

            _outsideCatcher?.RegisterCallback<PointerDownEvent>(OnOutsideCatcherPointerDown);
            _panelCta.clicked += OnLevelCtaClicked;
            if (_topbarMap != null)
                _topbarMap.clicked += OnTopbarMapClicked;
            if (_topbarTechniques != null)
                _topbarTechniques.clicked += OnTopbarTechniquesClicked;
            if (_topbarStats != null)
                _topbarStats.clicked += OnTopbarStatsClicked;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                if (_navigator.CurrentScreen == ScreenId)
                    OnEnteredScreen();
            }

            _progress = GetComponent<ITutorialProgress>();
            if (_progress != null)
                _progress.Changed += OnProgressChanged;

            RefreshLevelLockStates();
            RefreshTechniquesGate();

            _bound = true;
            _bindRoutine = null;
        }

        private const string SpecialIconClass = "level-node__icon--special";
        private const string TrainingIconClass = "level-node__icon--training";
        private const string FightIconClass = "level-node__icon--fight";
        private const string BossFightIconClass = "level-node__icon--boss-fight";

        /// <summary>
        /// Applies this level's source-image-normalized position (converted
        /// through the current viewport size, see
        /// MapMarkerLayout.ApplySourceCoordinate/MapCoordinateMapping) and
        /// mission-type icon (special/training/fight/boss fight) from
        /// MapMarkerLayout — the only place a mission's coordinates and type
        /// live. The icon class is added here at bind time rather than baked
        /// into MikeyApp.uxml so mission type stays centralized in data,
        /// never inferred from static CSS.
        /// </summary>
        private static void ApplyMissionLayout(VisualElement node, int levelIndex, float viewportWidth, float viewportHeight)
        {
            MissionMarkerLayout mission = default;
            bool found = false;
            foreach (var candidate in MapMarkerLayout.Missions)
            {
                if (candidate.LevelIndex != levelIndex)
                    continue;
                mission = candidate;
                found = true;
                break;
            }
            if (!found)
                return;

            MapMarkerLayout.ApplySourceCoordinate(node, mission.NormalizedX, mission.NormalizedY, viewportWidth, viewportHeight);

            var icon = node.Q<VisualElement>(className: "level-node__icon");
            if (icon == null)
                return;
            icon.RemoveFromClassList(SpecialIconClass);
            icon.RemoveFromClassList(TrainingIconClass);
            icon.RemoveFromClassList(FightIconClass);
            icon.RemoveFromClassList(BossFightIconClass);
            icon.AddToClassList(IconClassFor(mission.Type));
        }

        private static string IconClassFor(MissionMarkerType type)
        {
            switch (type)
            {
                case MissionMarkerType.Special:
                    return SpecialIconClass;
                case MissionMarkerType.Fight:
                    return FightIconClass;
                case MissionMarkerType.BossFight:
                    return BossFightIconClass;
                default:
                    return TrainingIconClass;
            }
        }

        private void OnLevelNodeClicked(int index)
        {
            if (_selectedLevel == index)
            {
                ClosePanel();
                return;
            }
            SelectLevel(index);
        }

        private void SelectLevel(int index)
        {
            DeselectCurrentNode();

            _selectedLevel = index;
            _levelNodes[index].AddToClassList(SelectedNodeClass);
            SetOutsideCatcherActive(true);

            ShowLevelPanel(index);
            _panel.AddToClassList(PanelOpenClass);
            _panel.pickingMode = PickingMode.Position;
        }

        private void ShowLevelPanel(int index)
        {
            bool locked = IsLevelLocked(index);

            _panelEyebrow.text = "LEVEL";
            _panelTitle.text = $"LVL {index}";

            switch (index)
            {
                case 0:
                    _panelSubtitle.text = "ASSESSMENT";
                    _panelDesc.text = "Measure your starting ability and learn how Mikey evaluates your movement.";
                    break;
                case 1:
                    _panelSubtitle.text = string.Empty;
                    _panelDesc.text = "Foundations";
                    break;
                default:
                    _panelSubtitle.text = string.Empty;
                    _panelDesc.text = "A future level. Complete earlier levels to unlock it.";
                    break;
            }

            if (!locked)
            {
                _panelCtaText.text = index == 0 ? "BEGIN" : "START";
                _panelCta.SetEnabled(true);
                _panelCta.RemoveFromClassList(LockedCtaClass);
            }
            else
            {
                _panelCtaText.text = index == 1 ? "COMPLETE LVL 0" : "LOCKED";
                _panelCta.SetEnabled(false);
                _panelCta.AddToClassList(LockedCtaClass);
            }
        }

        private void ClosePanel()
        {
            _panel.RemoveFromClassList(PanelOpenClass);
            _panel.pickingMode = PickingMode.Ignore;
            SetOutsideCatcherActive(false);
            DeselectCurrentNode();
            _selectedLevel = -1;
        }

        private void DeselectCurrentNode()
        {
            if (_selectedLevel >= 0 && _selectedLevel < LevelCount && _levelNodes[_selectedLevel] != null)
                _levelNodes[_selectedLevel].RemoveFromClassList(SelectedNodeClass);
        }

        private void SetOutsideCatcherActive(bool active)
        {
            if (_outsideCatcher != null)
                _outsideCatcher.pickingMode = active ? PickingMode.Position : PickingMode.Ignore;
        }

        private void OnOutsideCatcherPointerDown(PointerDownEvent evt) => ClosePanel();

        private void OnLevelCtaClicked()
        {
            if (_selectedLevel < 0 || IsLevelLocked(_selectedLevel))
                return;

            switch (_selectedLevel)
            {
                case 0:
                    _navigator?.Show(CombineIntroScreenId);
                    break;
                case 1:
                    _navigator?.Show(TechniquesScreenId);
                    break;
            }
        }

        /// <summary>LVL 0 is always unlocked; LVL 1 unlocks at Level1Unlocked; LVL 2-6 are always locked (no gameplay built yet).</summary>
        private bool IsLevelLocked(int index)
        {
            switch (index)
            {
                case 0:
                    return false;
                case 1:
                    return _progress != null && !TutorialProgressPresenter.IsMapUnlocked(_progress.State);
                default:
                    return true;
            }
        }

        private void RefreshLevelLockStates()
        {
            for (int i = 0; i < LevelCount; i++)
            {
                if (_levelNodes[i] == null)
                    continue;

                if (IsLevelLocked(i))
                    _levelNodes[i].AddToClassList(LockedNodeClass);
                else
                    _levelNodes[i].RemoveFromClassList(LockedNodeClass);
            }

            if (_selectedLevel >= 0)
                ShowLevelPanel(_selectedLevel);
        }

        private void OnProgressChanged() => RefreshLevelLockStates();

        private void OnTopbarMapClicked()
        {
            // The explicit "return to world map" action from inside Okinawa —
            // the one case where context must reset to Japan even though the
            // player is deliberately leaving Okinawa (see MapNavigationState).
            MapNavigationState.Current = MapContext.JapanWorld;
            _navigator?.Show(JapanMapScreenId);
        }

        private void OnTopbarTechniquesClicked()
        {
            if (_progress != null && !TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))
                return;
            _navigator?.Show(TechniquesScreenId);
        }

        private void OnTopbarStatsClicked() => _navigator?.Show("profile");

        private void RefreshTechniquesGate()
        {
            if (_topbarTechniques == null)
                return;

            bool unlocked = _progress == null || TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State);
            if (unlocked)
                _topbarTechniques.RemoveFromClassList(NavLockedClass);
            else
                _topbarTechniques.AddToClassList(NavLockedClass);
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
                OnEnteredScreen();
        }

        /// <summary>No selection, popup closed, and the ink-fade begun on the Japan screen finishes here.</summary>
        private void OnEnteredScreen()
        {
            MapNavigationState.Current = MapContext.OkinawaChapter;
            ClosePanel();
            RefreshLevelLockStates();
            RefreshTechniquesGate();
            _transitionOverlay?.RemoveFromClassList(TransitionVisibleClass);
        }
    }
}
