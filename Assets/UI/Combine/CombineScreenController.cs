using System.Collections;
using System.Collections.Generic;
using System;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Combine
{
    /// <summary>
    /// Binds a <see cref="CombineViewModel"/> to the "combine" screen inside the
    /// shared UIDocument: shows exactly one state sub-view at a time and renders the
    /// mock selected-item cards in the ready state.
    ///
    /// Coexists with ScreenManager (which shows/hides whole screens) — this only
    /// drives the Combine screen's internal Loading / Empty / Ready / Error views.
    /// A development-only switcher bar (Editor or Development Build only) lets a
    /// tester jump between states. Frontend / mock-only: no backend, networking,
    /// camera, or pose code here.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CombineScreenController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller owns; entering it through the normal flow shows results.</summary>
        public const string ScreenId = "combine";

        [Tooltip("State shown when the screen first binds.")]
        [SerializeField] private CombineState _initialState = CombineState.Loading;

        [Tooltip("Show the on-screen state switcher. Honoured only in the Editor or a Development Build.")]
        [SerializeField] private bool _showDevControls = true;

        private readonly CombineViewModel _viewModel = new CombineViewModel();

        // Cached elements from the combine screen subtree.
        private readonly Dictionary<CombineState, VisualElement> _stateViews =
            new Dictionary<CombineState, VisualElement>();
        private readonly List<ButtonBinding> _buttonBindings = new List<ButtonBinding>();
        private VisualElement _itemsContainer;
        private VisualElement _devBar;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

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
                UnbindButtons();
            }

            if (_navigator != null)
                _navigator.ScreenChanged -= OnScreenEntered;

            _stateViews.Clear();
            _itemsContainer = null;
            _devBar = null;
            _navigator = null;
            _progress = null;
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

            _stateViews.Clear();
            _stateViews[CombineState.Loading] = root.Q<VisualElement>("combine-loading");
            _stateViews[CombineState.Empty] = root.Q<VisualElement>("combine-empty");
            _stateViews[CombineState.Ready] = root.Q<VisualElement>("combine-ready");
            _stateViews[CombineState.Error] = root.Q<VisualElement>("combine-error");
            _itemsContainer = root.Q<VisualElement>("combine-items");
            _devBar = root.Q<VisualElement>("combine-devbar");

            foreach (KeyValuePair<CombineState, VisualElement> pair in _stateViews)
            {
                if (pair.Value == null)
                {
                    Debug.LogError(
                        $"[CombineScreenController] Missing '{pair.Key}' state view in the UIDocument; Combine screen not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
            }

            // Always-available retry affordance in the error state (mock: just
            // returns to loading). Independent of the dev-only switcher.
            BindButton(root, "combine-retry", () => _viewModel.SetState(CombineState.Loading));

            _navigator = GetComponent<IScreenNavigator>();
            _progress = GetComponent<ITutorialProgress>();

            // Ready-state progression actions.
            BindButton(root, "combine-start-lvl1", OnStartLevel1);
            BindButton(root, "combine-retry-assessment", OnRetryAssessment);

            WireDevControls(root);

            // Subscribe to the navigation entry signal so a normal production entry
            // (Camera Test completed -> "go-combine", or Home's "VIEW RESULTS" CTA)
            // always reaches a usable result instead of being stuck on whatever
            // state happened to be showing. OnEnable is not a reliable entry hook
            // here: the shared UI GameObject stays enabled while ScreenManager only
            // toggles screen visibility (see CameraTestController for the same
            // pattern).
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            _viewModel.Changed += Render;
            _bound = true;
            _bindRoutine = null;

            // If Combine is already the active screen at bind time, treat it as a
            // genuine entry (shows Ready); otherwise fall back to the configured
            // initial state (Loading by default) until a real entry occurs.
            if (_navigator != null && IsCombineEntry(_navigator.CurrentScreen))
                OnScreenEntered(_navigator.CurrentScreen);
            else
                _viewModel.SetState(_initialState);
        }

        /// <summary>
        /// Navigation entry handler: every genuine entry into Combine Results from the
        /// normal flow means the (mock) assessment data is already known, so the
        /// screen shows the Ready result immediately — no artificial wait, and no
        /// dependency on the Editor/Development-only dev controls to escape Loading.
        /// </summary>
        private void OnScreenEntered(string screenId)
        {
            if (!IsCombineEntry(screenId))
                return;

            _viewModel.SetState(CombineState.Ready);
        }

        /// <summary>Pure entry predicate: true only for an exact Combine Results entry. Unit-tested.</summary>
        public static bool IsCombineEntry(string screenId) => screenId == ScreenId;

        /// <summary>
        /// "START LVL 1": marks the Combine (LVL0) assessment completed, unlocks
        /// Level 1, then opens the Map. Uses the forward-only Advance so this is
        /// safe to invoke more than once (e.g. a stray extra click) without any
        /// unexpected side effect.
        /// </summary>
        private void OnStartLevel1()
        {
            _progress?.Advance(TutorialProgressState.CombineCompleted);
            _progress?.Advance(TutorialProgressState.Level1Unlocked);
            _navigator?.Show("map");
        }

        /// <summary>
        /// "Retry Assessment": resets only the Combine (LVL0) portion of progress
        /// (back to IntroCompleted, i.e. "briefed but not yet re-attempted") and
        /// returns to the Combine briefing to redo it. Only resets when progress
        /// hasn't moved past Combine yet — if Level 1 (or anything beyond it) has
        /// already unlocked, retrying the LVL0 baseline must not silently wipe
        /// that real progress, so the state is left untouched in that case
        /// (never wipes progress without explicit confirmation, which this MVP
        /// has no dialog system to present).
        /// </summary>
        private void OnRetryAssessment()
        {
            if (_progress != null && _progress.State <= TutorialProgressState.CombineCompleted)
                _progress.SetState(TutorialProgressState.IntroCompleted);
            _navigator?.Show("combineIntro");
        }

        private void WireDevControls(VisualElement root)
        {
            bool allowed = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            allowed = _showDevControls;
#endif
            if (_devBar != null)
                _devBar.style.display = allowed ? DisplayStyle.Flex : DisplayStyle.None;

            if (!allowed)
                return;

            BindButton(root, "combine-dev-loading", () => _viewModel.SetState(CombineState.Loading));
            BindButton(root, "combine-dev-empty", () => _viewModel.SetState(CombineState.Empty));
            BindButton(root, "combine-dev-ready", () => _viewModel.SetState(CombineState.Ready));
            BindButton(root, "combine-dev-error", () => _viewModel.SetState(CombineState.Error));
            BindButton(root, "combine-dev-cycle", () => _viewModel.CycleState());
        }

        private void BindButton(VisualElement root, string name, Action onClick)
        {
            var button = root.Q<Button>(name);
            if (button == null)
                return;

            button.clicked += onClick;
            _buttonBindings.Add(new ButtonBinding(button, onClick));
        }

        private void UnbindButtons()
        {
            for (int i = 0; i < _buttonBindings.Count; i++)
                _buttonBindings[i].Unbind();
            _buttonBindings.Clear();
        }

        private void Render()
        {
            foreach (KeyValuePair<CombineState, VisualElement> pair in _stateViews)
                pair.Value.style.display = pair.Key == _viewModel.State ? DisplayStyle.Flex : DisplayStyle.None;

            if (_viewModel.State == CombineState.Ready)
                RenderItems(_viewModel.Items);
        }

        private void RenderItems(IReadOnlyList<CombineItem> items)
        {
            if (_itemsContainer == null)
                return;

            _itemsContainer.Clear();
            for (int i = 0; i < items.Count; i++)
                _itemsContainer.Add(BuildCard(items[i]));
        }

        private static VisualElement BuildCard(CombineItem item)
        {
            var card = new VisualElement();
            card.AddToClassList("combine-card");

            var glyph = new Label(item.Glyph);
            glyph.AddToClassList("combine-card__glyph");
            glyph.AddToClassList("icon-32"); // reusable visible-icon size
            card.Add(glyph);

            var value = new Label(item.Value);
            value.AddToClassList("combine-card__value");
            card.Add(value);

            var unit = new Label(item.Unit);
            unit.AddToClassList("combine-card__unit");
            card.Add(unit);

            var name = new Label(item.Name);
            name.AddToClassList("combine-card__name");
            card.Add(name);

            var stat = new Label(item.Stat);
            stat.AddToClassList("combine-card__stat");
            card.Add(stat);

            return card;
        }

        private readonly struct ButtonBinding
        {
            private readonly Button _button;
            private readonly Action _callback;

            public ButtonBinding(Button button, Action callback)
            {
                _button = button;
                _callback = callback;
            }

            public void Unbind()
            {
                if (_button != null && _callback != null)
                    _button.clicked -= _callback;
            }
        }
    }
}
