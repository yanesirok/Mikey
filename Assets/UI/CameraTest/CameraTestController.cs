using System.Collections;
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

        // Status modifier classes toggled on the form pill (defined in CameraTest.uss).
        private const string StatusReadyClass = "cam-status--ready";
        private const string StatusAdjustClass = "cam-status--adjust";
        private const string StatusGoodClass = "cam-status--good";

        private readonly CameraTestRepModel _model = new CameraTestRepModel();

        private Label _repCount;
        private Label _statusText;
        private Label _statusGlyph;
        private VisualElement _statusPill;

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
                _model.Changed -= Render;

            _repCount = null;
            _statusText = null;
            _statusGlyph = null;
            _statusPill = null;
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
            var simulate = root.Q<Button>("camera-simulate-rep");
            if (simulate != null)
                simulate.clicked += _model.SimulateRep;

            _model.Changed += Render;
            _bound = true;
            _bindRoutine = null;

            // Predictable start: the HUD always opens at 0 reps / "Align in frame".
            _model.Reset();
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
