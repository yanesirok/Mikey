using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Combine
{
    /// <summary>
    /// Binds a <see cref="CombineChecklistViewModel"/> to the real "combine"
    /// screen inside the shared UIDocument: the Level 0 checklist of five
    /// sequential tests (Camera Test, Max Push-Ups, Max Squats, Wall Sit, Slow
    /// Yoko-Geri) with a left preview panel and a right checklist, matching the
    /// locked design reference. Tests unlock strictly one at a time; completing
    /// one never auto-starts the next — the player always returns here and
    /// presses START manually.
    ///
    /// Coexists with ScreenManager (which shows/hides whole screens) — this only
    /// drives the Combine screen's internal checklist/preview. Frontend only: no
    /// backend, networking, camera, or pose code here — the actual test screens
    /// (camTest and the four Level0Tests placeholders) own their own completion.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CombineScreenController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const int RowCount = 5;

        /// <summary>The screen id this controller owns.</summary>
        public const string ScreenId = "combine";

        private const string RowLockedClass = "combine-row--locked";
        private const string RowAvailableClass = "combine-row--available";
        private const string RowCompleteClass = "combine-row--complete";
        private const string RowSelectedClass = "combine-row--selected";
        private const string IconCheckClass = "combine-row__icon--check";
        private const string IconLockClass = "combine-row__icon--lock";

        private readonly VisualElement[] _rows = new VisualElement[RowCount];
        private readonly VisualElement[] _rowIcons = new VisualElement[RowCount];
        private readonly EventCallback<ClickEvent>[] _rowClickCallbacks = new EventCallback<ClickEvent>[RowCount];

        private VisualElement _illustration;
        private Label _progressLabel;
        private Label _testTitle;
        private Label _testDesc;
        private Label _testSecondary;
        private Label _testStat;
        private Button _startButton;
        private Button _startLvl1Button;

        private CombineChecklistViewModel _viewModel;
        private ILevel0Progress _level0;
        private IScreenNavigator _navigator;
        private ITutorialProgress _tutorialProgress;

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
                _viewModel.Changed -= Render;
                if (_level0 != null)
                    _level0.Changed -= OnLevel0Changed;

                for (int i = 0; i < RowCount; i++)
                {
                    if (_rows[i] != null && _rowClickCallbacks[i] != null)
                        _rows[i].UnregisterCallback(_rowClickCallbacks[i]);
                }

                if (_startButton != null)
                    _startButton.clicked -= OnStartClicked;
                if (_startLvl1Button != null)
                    _startLvl1Button.clicked -= OnStartLevel1;
            }

            if (_navigator != null)
                _navigator.ScreenChanged -= OnScreenEntered;

            for (int i = 0; i < RowCount; i++)
            {
                _rows[i] = null;
                _rowIcons[i] = null;
                _rowClickCallbacks[i] = null;
            }
            _illustration = null;
            _progressLabel = null;
            _testTitle = null;
            _testDesc = null;
            _testSecondary = null;
            _testStat = null;
            _startButton = null;
            _startLvl1Button = null;
            _viewModel = null;
            _level0 = null;
            _navigator = null;
            _tutorialProgress = null;
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
                    Debug.LogError("[CombineScreenController] UIDocument root unavailable; Combine screen not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            for (int i = 0; i < RowCount; i++)
            {
                _rows[i] = root.Q<VisualElement>($"combine-row-{i}");
                _rowIcons[i] = root.Q<VisualElement>($"combine-row-{i}-icon");
            }

            _illustration = root.Q<VisualElement>("combine-illustration");
            _progressLabel = root.Q<Label>("combine-progress");
            _testTitle = root.Q<Label>("combine-test-title");
            _testDesc = root.Q<Label>("combine-test-desc");
            _testSecondary = root.Q<Label>("combine-test-secondary");
            _testStat = root.Q<Label>("combine-test-stat");
            _startButton = root.Q<Button>("combine-start");
            _startLvl1Button = root.Q<Button>("combine-start-lvl1");

            if (_rows[0] == null || _startButton == null || _progressLabel == null)
            {
                Debug.LogError("[CombineScreenController] Combine checklist elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _level0 = GetComponent<ILevel0Progress>();
            if (_level0 == null)
            {
                Debug.LogError("[CombineScreenController] ILevel0Progress unavailable; Combine screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _viewModel = new CombineChecklistViewModel(_level0);

            for (int i = 0; i < RowCount; i++)
            {
                if (_rows[i] == null)
                    continue;
                var test = (Level0Test)i;
                EventCallback<ClickEvent> callback = _ => _viewModel.Select(test);
                _rowClickCallbacks[i] = callback;
                _rows[i].RegisterCallback(callback);
            }

            _startButton.clicked += OnStartClicked;

            _navigator = GetComponent<IScreenNavigator>();
            _tutorialProgress = GetComponent<ITutorialProgress>();

            // Legacy compatibility bridge only: this does NOT mean "LVL1 Training
            // was completed" — TutorialProgressState.Level1Unlocked is a pure
            // access gate (Home's CTA + reaching the Map/Techniques screens at
            // all), unrelated to the new IOkinawaProgress model, which is the
            // sole authority for actual Okinawa LVL0-6 unlock/completion state.
            if (_startLvl1Button != null)
                _startLvl1Button.clicked += OnStartLevel1;

            _viewModel.Changed += Render;
            _level0.Changed += OnLevel0Changed;

            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            _bound = true;
            _bindRoutine = null;

            if (_navigator != null && IsCombineEntry(_navigator.CurrentScreen))
                OnScreenEntered(_navigator.CurrentScreen);
            else
                Render();
        }

        /// <summary>
        /// Navigation entry handler: every genuine entry into Combine (from
        /// CombineIntro, a completed test screen returning here, or Home)
        /// re-selects the current available test (or the most recently completed
        /// one, if Level 0 is fully complete) so a stale prior preview never lingers.
        /// </summary>
        private void OnScreenEntered(string screenId)
        {
            if (!IsCombineEntry(screenId))
                return;

            _viewModel.SelectDefault();
        }

        /// <summary>Pure entry predicate: true only for an exact Combine entry. Unit-tested.</summary>
        public static bool IsCombineEntry(string screenId) => screenId == ScreenId;

        private void OnLevel0Changed() => _viewModel.NotifyProgressChanged();

        private void OnStartClicked()
        {
            Level0Test test = _viewModel.SelectedTest;
            if (_viewModel.StateOf(test) != Level0TestState.Available)
                return;

            _navigator?.Show(DestinationFor(test));
        }

        /// <summary>Pure test → destination-screen-id lookup. Unit-tested.</summary>
        public static string DestinationFor(Level0Test test)
        {
            switch (test)
            {
                case Level0Test.CameraTest: return "camTest";
                case Level0Test.PushUps: return "combinePushups";
                case Level0Test.Squats: return "combineSquats";
                case Level0Test.WallSit: return "combineWallsit";
                case Level0Test.YokoGeri: return "combineYokogeri";
                default: return ScreenId;
            }
        }

        /// <summary>
        /// Legacy compatibility bridge: only reachable once every one of the
        /// five Level 0 tests is complete. Advances the OLD linear
        /// TutorialProgressState (CombineCompleted -> Level1Unlocked) purely so
        /// Home's CTA and the ability to reach the Map/Techniques screens keep
        /// working exactly as before, then opens the Map — where the NEW
        /// IOkinawaProgress model (not this legacy flag) determines that LVL1
        /// Training and LVL2 Fight are now both unlocked.
        /// </summary>
        private void OnStartLevel1()
        {
            if (!_viewModel.IsLevel0Complete)
                return;

            _tutorialProgress?.Advance(TutorialProgressState.CombineCompleted);
            _tutorialProgress?.Advance(TutorialProgressState.Level1Unlocked);
            _navigator?.Show("map");
        }

        private void Render()
        {
            for (int i = 0; i < RowCount; i++)
                RenderRow(i, (Level0Test)i);

            RenderLeftPanel();

            if (_progressLabel != null)
                _progressLabel.text = $"{_viewModel.CompletedCount} / {RowCount} COMPLETE";
        }

        private void RenderRow(int index, Level0Test test)
        {
            VisualElement row = _rows[index];
            if (row == null)
                return;

            Level0TestState state = _viewModel.StateOf(test);

            row.RemoveFromClassList(RowLockedClass);
            row.RemoveFromClassList(RowAvailableClass);
            row.RemoveFromClassList(RowCompleteClass);
            row.AddToClassList(ClassForRowState(state));

            bool selected = state != Level0TestState.Locked && _viewModel.SelectedTest == test;
            ToggleClass(row, RowSelectedClass, selected);

            VisualElement icon = _rowIcons[index];
            if (icon == null)
                return;

            icon.RemoveFromClassList(IconCheckClass);
            icon.RemoveFromClassList(IconLockClass);
            if (state == Level0TestState.Complete)
                icon.AddToClassList(IconCheckClass);
            else if (state == Level0TestState.Locked)
                icon.AddToClassList(IconLockClass);
        }

        private void RenderLeftPanel()
        {
            Level0Test test = _viewModel.SelectedTest;
            Level0TestState state = _viewModel.StateOf(test);

            if (_illustration != null)
            {
                foreach (string className in IllustrationClasses)
                    _illustration.RemoveFromClassList(className);
                _illustration.AddToClassList(IllustrationClassFor(test));
            }

            if (_testTitle != null)
                _testTitle.text = Level0TestCopy.TitleFor(test);
            if (_testDesc != null)
                _testDesc.text = Level0TestCopy.DescriptionFor(test);

            SetOptionalLine(_testSecondary, Level0TestCopy.SecondaryFor(test));
            SetOptionalLine(_testStat, Level0TestCopy.StatFor(test));

            bool showStart = state == Level0TestState.Available;
            if (_startButton != null)
            {
                _startButton.style.display = showStart ? DisplayStyle.Flex : DisplayStyle.None;
                _startButton.SetEnabled(showStart);
            }

            if (_startLvl1Button != null)
                _startLvl1Button.style.display = _viewModel.IsLevel0Complete ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetOptionalLine(Label label, string text)
        {
            if (label == null)
                return;

            label.text = text;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static readonly string[] IllustrationClasses =
        {
            "combine-illustration--camera",
            "combine-illustration--pushups",
            "combine-illustration--squats",
            "combine-illustration--wallsit",
            "combine-illustration--yokogeri",
        };

        /// <summary>Pure test → illustration CSS class lookup. Unit-tested.</summary>
        public static string IllustrationClassFor(Level0Test test)
        {
            switch (test)
            {
                case Level0Test.CameraTest: return "combine-illustration--camera";
                case Level0Test.PushUps: return "combine-illustration--pushups";
                case Level0Test.Squats: return "combine-illustration--squats";
                case Level0Test.WallSit: return "combine-illustration--wallsit";
                case Level0Test.YokoGeri: return "combine-illustration--yokogeri";
                default: return "combine-illustration--camera";
            }
        }

        /// <summary>Pure state → row modifier class lookup. Unit-tested.</summary>
        public static string ClassForRowState(Level0TestState state)
        {
            switch (state)
            {
                case Level0TestState.Complete: return RowCompleteClass;
                case Level0TestState.Locked: return RowLockedClass;
                default: return RowAvailableClass;
            }
        }

        private static void ToggleClass(VisualElement element, string className, bool on)
        {
            if (on)
                element.AddToClassList(className);
            else
                element.RemoveFromClassList(className);
        }
    }
}
