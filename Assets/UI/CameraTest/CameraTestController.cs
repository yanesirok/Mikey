using System.Collections;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.CameraTest
{
    /// <summary>
    /// Drives the mock interaction on the "camTest" screen inside the shared
    /// UIDocument: binds the local "+ simulate reps" control to a pure
    /// <see cref="CameraTestRepModel"/>, and renders the visible rep count and the
    /// deterministic form/status copy back into the HUD.
    ///
    /// Coexists with ScreenManager (which shows/hides whole screens) and
    /// CombineScreenController — this only owns the Camera Test HUD's mock counter.
    /// Frontend / mock-only: there is no real camera, pose detection, ML,
    /// networking, or backend code here. The rep count is a local stand-in for
    /// pose input so the screen can be demonstrated without a live feed.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CameraTestController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller owns; entering it resets the mock counter.</summary>
        public const string ScreenId = "camTest";

        // Status modifier classes toggled on the form pill (defined in CameraTest.uss).
        private const string StatusReadyClass = "cam-status--ready";
        private const string StatusAdjustClass = "cam-status--adjust";
        private const string StatusGoodClass = "cam-status--good";

        private readonly CameraTestRepModel _model = new CameraTestRepModel();

        private Label _repCount;
        private Label _statusText;
        private Label _statusGlyph;
        private VisualElement _statusPill;
        private Button _simulate;
        private Button _complete;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;
        private ILevel0Progress _level0;

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

                if (_simulate != null)
                    _simulate.clicked -= _model.SimulateRep;
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

            _progress = null;
            _level0 = null;

            _repCount = null;
            _statusText = null;
            _statusGlyph = null;
            _statusPill = null;
            _simulate = null;
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
                    Debug.LogError("[CameraTestController] UIDocument root unavailable; Camera Test screen not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _repCount = root.Q<Label>("camera-rep-count");
            _statusText = root.Q<Label>("camera-form-status");
            _statusGlyph = root.Q<Label>("camera-form-glyph");
            _statusPill = root.Q<VisualElement>("camera-form-pill");

            if (_repCount == null || _statusText == null || _statusPill == null)
            {
                Debug.LogError("[CameraTestController] Camera Test HUD elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            // Local mock action only. Deliberately NOT a "go-" navigator: ScreenManager
            // must not treat it as a screen jump.
            _simulate = root.Q<Button>("camera-simulate-rep");
            if (_simulate != null)
                _simulate.clicked += _model.SimulateRep;

            // Also NOT a "go-" navigator: completing Camera Test has a side effect
            // (marking Level 0's Camera Test complete) before returning to the
            // Combine checklist, so it needs an explicit controller-bound handler
            // rather than ScreenManager's generic screen swap.
            _complete = root.Q<Button>("camera-test-complete");
            if (_complete != null)
                _complete.clicked += OnCompleteClicked;

            _model.Changed += Render;

            // Subscribe to the navigation entry signal. OnEnable is not a reliable
            // screen-entry hook here: the shared UI GameObject stays enabled while
            // ScreenManager only toggles the visibility of individual screens, so the
            // reset must be driven by a genuine "entered camTest" navigation event.
            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            _progress = GetComponent<ITutorialProgress>();
            _level0 = GetComponent<ILevel0Progress>();

            _bound = true;
            _bindRoutine = null;

            // Establish the initial view. If camTest is already the shown screen at
            // bind time, treat it as an entry (reset + mark CombineStarted);
            // otherwise just render the current (fresh, 0-rep) state so the HUD
            // reads 0 / "Align in frame".
            if (_navigator != null && IsCamTestEntry(_navigator.CurrentScreen))
                OnScreenEntered(_navigator.CurrentScreen);
            else
                Render();
        }

        /// <summary>
        /// Navigation entry handler: resets the mock counter (and re-renders) only when
        /// the entered screen is exactly camTest. ScreenManager raises this once per
        /// genuine entry — not while the user remains on camTest, and not on Simulate.
        /// </summary>
        private void OnScreenEntered(string screenId)
        {
            if (!IsCamTestEntry(screenId))
                return;

            _model.Reset();

            // "Starting Combine" = arriving at the interactive Camera Test (the
            // briefing screen is just a CTA). Advance is forward-only, so
            // re-entering camTest after progress has already moved past this
            // point (e.g. via Profile's dock or a developer control) never
            // regresses it.
            _progress?.Advance(TutorialProgressState.CombineStarted);
        }

        /// <summary>Pure entry predicate: true only for an exact camTest entry. Unit-tested.</summary>
        public static bool IsCamTestEntry(string screenId) => screenId == ScreenId;

        /// <summary>
        /// "Done": marks Level 0's Camera Test complete — this already-built (if
        /// mocked) rep-counting interaction is the genuine, pre-existing Camera
        /// Test implementation, unlike the four Level0Tests placeholder screens,
        /// which never call Complete from a button — then returns to the Combine
        /// checklist, where the next test (Max Push-Ups) becomes available. Does
        /// NOT auto-start the next test.
        /// </summary>
        private void OnCompleteClicked()
        {
            _level0?.Complete(Level0Test.CameraTest);
            _navigator?.Show("combine");
        }

        private void Render()
        {
            if (_repCount != null)
                _repCount.text = _model.Reps.ToString();

            if (_statusText != null)
                _statusText.text = _model.StatusText;

            if (_statusGlyph != null)
                _statusGlyph.text = GlyphFor(_model.Status);

            if (_statusPill != null)
            {
                _statusPill.RemoveFromClassList(StatusReadyClass);
                _statusPill.RemoveFromClassList(StatusAdjustClass);
                _statusPill.RemoveFromClassList(StatusGoodClass);
                _statusPill.AddToClassList(ClassFor(_model.Status));
            }
        }

        private static string ClassFor(CameraFormStatus status)
        {
            switch (status)
            {
                case CameraFormStatus.Ready: return StatusReadyClass;
                case CameraFormStatus.Adjust: return StatusAdjustClass;
                case CameraFormStatus.Good: return StatusGoodClass;
                default: return StatusReadyClass;
            }
        }

        private static string GlyphFor(CameraFormStatus status)
        {
            switch (status)
            {
                case CameraFormStatus.Ready: return "構"; // stance / frame up
                case CameraFormStatus.Adjust: return "正"; // correct
                case CameraFormStatus.Good: return "良"; // good
                default: return "構";
            }
        }
    }
}
