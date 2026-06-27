using System.Collections;
using System.Collections.Generic;
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

        [Tooltip("State shown when the screen first binds.")]
        [SerializeField] private CombineState _initialState = CombineState.Loading;

        [Tooltip("Show the on-screen state switcher. Honoured only in the Editor or a Development Build.")]
        [SerializeField] private bool _showDevControls = true;

        private readonly CombineViewModel _viewModel = new CombineViewModel();

        // Cached elements from the combine screen subtree.
        private readonly Dictionary<CombineState, VisualElement> _stateViews =
            new Dictionary<CombineState, VisualElement>();
        private VisualElement _itemsContainer;
        private VisualElement _devBar;

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
                _viewModel.Changed -= Render;

            _stateViews.Clear();
            _itemsContainer = null;
            _devBar = null;
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

            WireDevControls(root);

            _viewModel.Changed += Render;
            _bound = true;
            _bindRoutine = null;

            _viewModel.SetState(_initialState);
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

        private static void BindButton(VisualElement root, string name, System.Action onClick)
        {
            var button = root.Q<Button>(name);
            if (button != null)
                button.clicked += onClick;
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
    }
}
