using System.Collections;
using Mikey.Pose;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.CameraTest
{
    /// <summary>
    /// Production HUD for the Camera Test / push-up screen: binds a live
    /// <see cref="PoseController"/> to the same "camTest" HUD elements the mock
    /// <see cref="CameraTestController"/> uses (rep count, form pill, glyph) and adds the
    /// prioritized correction cue and the camera background.
    ///
    /// Use this INSTEAD of the mock <see cref="CameraTestController"/> on the camTest
    /// screen: disable/remove that component and add this one (with a PoseController on the
    /// same or a referenced GameObject). Reuses <see cref="CameraFormStatus"/> and its
    /// USS classes so the visual language is unchanged.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PushUpHudBinder : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        private const string StatusReadyClass = "cam-status--ready";
        private const string StatusAdjustClass = "cam-status--adjust";
        private const string StatusGoodClass = "cam-status--good";

        [Tooltip("Pose input + scoring. If left empty, one is looked up on this GameObject.")]
        [SerializeField] private PoseController _poseController;

        private Label _repCount;
        private Label _statusText;
        private Label _statusGlyph;
        private VisualElement _statusPill;
        private Label _cue;
        private VisualElement _cameraBackground;

        private Coroutine _bindRoutine;
        private bool _bound;

        private void OnEnable()
        {
            if (_poseController == null)
                _poseController = GetComponent<PoseController>();

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

            if (_bound && _poseController != null)
                _poseController.Changed -= Render;

            _repCount = null;
            _statusText = null;
            _statusGlyph = null;
            _statusPill = null;
            _cue = null;
            _cameraBackground = null;
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
                    Debug.LogError("[PushUpHudBinder] UIDocument root unavailable; push-up HUD not bound.", this);
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
            _cue = root.Q<Label>("camera-cue");                    // optional (added for correction text)
            _cameraBackground = root.Q<VisualElement>("camera-bg"); // optional (live camera image)

            if (_repCount == null || _statusText == null || _statusPill == null)
            {
                Debug.LogError("[PushUpHudBinder] camTest HUD elements missing; push-up HUD not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            if (_poseController == null)
            {
                Debug.LogError("[PushUpHudBinder] No PoseController assigned; nothing to bind.", this);
                _bindRoutine = null;
                yield break;
            }

            _poseController.Changed += Render;

            // This screen is the push-up station: select it so the camera starts.
            if (!_poseController.HasExercise)
                _poseController.SelectExercise(ExerciseCatalog.Create("pushup"));

            _bound = true;
            _bindRoutine = null;
            Render();
        }

        private void Render()
        {
            IExerciseAnalyzer analyzer = _poseController.Analyzer;
            if (analyzer == null)
                return;

            CameraFormStatus status = StatusFor(analyzer);

            if (_repCount != null)
                _repCount.text = analyzer.Reps.ToString();

            if (_statusText != null)
                _statusText.text = TextFor(status);

            if (_cue != null)
                _cue.text = analyzer.Cue;

            if (_statusGlyph != null)
                _statusGlyph.text = GlyphFor(status);

            if (_statusPill != null)
            {
                _statusPill.RemoveFromClassList(StatusReadyClass);
                _statusPill.RemoveFromClassList(StatusAdjustClass);
                _statusPill.RemoveFromClassList(StatusGoodClass);
                _statusPill.AddToClassList(ClassFor(status));
            }

            if (_cameraBackground != null)
            {
                Texture cam = _poseController.CameraTexture;
                if (cam != null)
                    _cameraBackground.style.backgroundImage = new StyleBackground(Background.FromTexture2D(cam as Texture2D));
            }
        }

        /// <summary>
        /// Maps the analyzer state onto the three-state HUD pill: no body → Ready,
        /// a form fault → Adjust (correction), clean form → Good. Pure; unit-testable.
        /// </summary>
        public static CameraFormStatus StatusFor(IExerciseAnalyzer analyzer)
        {
            switch (analyzer.FormState)
            {
                case ExerciseFormState.NotVisible: return CameraFormStatus.Ready;
                case ExerciseFormState.GoodForm: return CameraFormStatus.Good;
                default: return CameraFormStatus.Adjust;
            }
        }

        private static string TextFor(CameraFormStatus status)
        {
            switch (status)
            {
                case CameraFormStatus.Ready: return "Align in frame";
                case CameraFormStatus.Adjust: return "Fix form";
                case CameraFormStatus.Good: return "Good form";
                default: return "Align in frame";
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
                case CameraFormStatus.Ready: return "構";
                case CameraFormStatus.Adjust: return "正";
                case CameraFormStatus.Good: return "良";
                default: return "構";
            }
        }
    }
}
