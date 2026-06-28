using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Practice
{
    /// <summary>
    /// Drives the mock training interaction on the "practice" screen inside the
    /// shared UIDocument: binds the local "Begin/Next" control to a pure
    /// <see cref="PracticeSessionModel"/>, renders the deterministic score, cue
    /// and progress back into the HUD, and gates the completion control so it is
    /// usable only once the session is complete (then it navigates to the
    /// Techniques hub).
    ///
    /// Coexists with ScreenManager (which shows/hides whole screens),
    /// CombineScreenController and CameraTestController — this only owns the
    /// Practice HUD. Frontend / mock-only: there is no real camera, pose
    /// detection, ML, networking or backend code here; the step counter is a
    /// local stand-in for pose input so the screen can be demonstrated without a
    /// live feed. Mirrors CameraTestController's bind/entry/unsubscribe pattern.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PracticeController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller owns; entering it resets the session.</summary>
        public const string ScreenId = "practice";

        /// <summary>Where the completion action navigates (an existing production screen).</summary>
        public const string CompleteTarget = "techniques";

        // Cue modifier classes toggled on the cue pill (defined in Practice.uss).
        private const string CueReadyClass = "pr-cue--ready";
        private const string CueAdjustClass = "pr-cue--adjust";
        private const string CueGoodClass = "pr-cue--good";

        // Filled-pip + disabled-button modifiers (defined in Practice.uss).
        private const string PipOnClass = "pr-pip--on";
        private const string BtnDisabledClass = "pr-btn--disabled";

        private readonly PracticeSessionModel _model = new PracticeSessionModel();

        private Label _score;
        private Label _cueText;
        private Label _cueGlyph;
        private VisualElement _cuePill;
        private VisualElement _pips;
        private Button _action;
        private Button _complete;

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
                _model.Changed -= Render;

                if (_action != null)
                    _action.clicked -= OnActionClicked;
                if (_complete != null)
                    _complete.clicked -= OnCompleteClicked;
            }

            // Unsubscribe from the navigator so re-enabling never stacks a second
            // handler (no duplicate-subscription leak across enable/disable cycles).
            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenEntered;
                _navigator = null;
            }

            _score = null;
            _cueText = null;
            _cueGlyph = null;
            _cuePill = null;
            _pips = null;
            _action = null;
            _complete = null;
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
                    Debug.LogError("[PracticeController] UIDocument root unavailable; Practice screen not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _score = root.Q<Label>("practice-score");
            _cueText = root.Q<Label>("practice-cue-text");
            _cueGlyph = root.Q<Label>("practice-cue-glyph");
            _cuePill = root.Q<VisualElement>("practice-cue-pill");
            _pips = root.Q<VisualElement>("practice-progress");
            _action = root.Q<Button>("practice-action");
            _complete = root.Q<Button>("practice-complete");

            if (_score == null || _cueText == null || _action == null || _complete == null)
            {
                Debug.LogError("[PracticeController] Practice HUD elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            // Local mock action only. Deliberately NOT a "go-" navigator: ScreenManager
            // must not treat the advance/complete controls as a screen jump.
            _action.clicked += OnActionClicked;
            _complete.clicked += OnCompleteClicked;

            _model.Changed += Render;

            // Subscribe to the navigation entry signal. OnEnable is not a reliable
            // screen-entry hook here: the shared UI GameObject stays enabled while
            // ScreenManager only toggles the visibility of individual screens, so the
            // reset must be driven by a genuine "entered practice" navigation event.
            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            _bound = true;
            _bindRoutine = null;

            // Establish the initial view. If practice is already the shown screen at
            // bind time, treat it as an entry and reset; otherwise render the current
            // (fresh, Ready) state so the HUD reads its starting baseline.
            if (_navigator != null && IsPracticeEntry(_navigator.CurrentScreen))
                _model.Reset();
            else
                Render();
        }

        private void OnActionClicked() => _model.Advance();

        private void OnCompleteClicked()
        {
            // Completion is gated: only navigate once the session is actually complete.
            if (_model.CanComplete)
                _navigator?.Show(CompleteTarget);
        }

        /// <summary>
        /// Navigation entry handler: resets the session (and re-renders) only when the
        /// entered screen is exactly practice. ScreenManager raises this once per genuine
        /// entry — not while the user remains on practice, and not on Begin/Next.
        /// </summary>
        private void OnScreenEntered(string screenId)
        {
            if (IsPracticeEntry(screenId))
                _model.Reset();
        }

        /// <summary>Pure entry predicate: true only for an exact practice entry. Unit-tested.</summary>
        public static bool IsPracticeEntry(string screenId) => screenId == ScreenId;

        private void Render()
        {
            if (_score != null)
                _score.text = _model.Score.ToString();

            if (_cueText != null)
                _cueText.text = _model.Instruction;

            if (_cueGlyph != null)
                _cueGlyph.text = GlyphFor(_model.Cue);

            if (_cuePill != null)
            {
                _cuePill.RemoveFromClassList(CueReadyClass);
                _cuePill.RemoveFromClassList(CueAdjustClass);
                _cuePill.RemoveFromClassList(CueGoodClass);
                _cuePill.AddToClassList(ClassFor(_model.Cue));
            }

            RenderPips();
            RenderActions();
        }

        private void RenderPips()
        {
            if (_pips == null)
                return;

            int filled = _model.Step;
            int i = 0;
            foreach (VisualElement pip in _pips.Children())
            {
                if (i < filled)
                    pip.AddToClassList(PipOnClass);
                else
                    pip.RemoveFromClassList(PipOnClass);
                i++;
            }
        }

        private void RenderActions()
        {
            if (_action != null)
            {
                var label = _action.Q<Label>(className: "pr-btn__text");
                if (label != null)
                    label.text = _model.ActionLabel;

                // Once complete, the advance control is spent: dim + disable it.
                bool advanceEnabled = !_model.IsComplete;
                _action.SetEnabled(advanceEnabled);
                ToggleClass(_action, BtnDisabledClass, !advanceEnabled);
            }

            if (_complete != null)
            {
                // The completion action becomes available only at completion.
                bool canComplete = _model.CanComplete;
                _complete.SetEnabled(canComplete);
                ToggleClass(_complete, BtnDisabledClass, !canComplete);
            }
        }

        private static void ToggleClass(VisualElement el, string className, bool on)
        {
            if (on)
                el.AddToClassList(className);
            else
                el.RemoveFromClassList(className);
        }

        private static string ClassFor(PracticeCue cue)
        {
            switch (cue)
            {
                case PracticeCue.Ready: return CueReadyClass;
                case PracticeCue.Adjust: return CueAdjustClass;
                case PracticeCue.Good: return CueGoodClass;
                default: return CueReadyClass;
            }
        }

        private static string GlyphFor(PracticeCue cue)
        {
            switch (cue)
            {
                case PracticeCue.Ready: return "構"; // stance / frame up
                case PracticeCue.Adjust: return "正"; // correct
                case PracticeCue.Good: return "良"; // good
                default: return "構";
            }
        }
    }
}
