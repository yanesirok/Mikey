using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Intro
{
    /// <summary>
    /// Drives Lore's cinematic exit: both "Skip" (<c>lore-skip</c>) and the
    /// primary "Continue" CTA (<c>lore-continue</c>) darken Lore to black
    /// through the shared <see cref="ITransitionOverlay"/>, only then swap to
    /// "menu" while fully covered, then fade Main Menu in — so leaving Lore no
    /// longer reads as an instant hard cut.
    ///
    /// Deliberately named "go-menu" no longer: naming them that would let
    /// ScreenManager auto-wire its own instant <c>Show("menu")</c> handler on
    /// the same buttons, which would always fire (registered at scene load,
    /// before this controller's own binding) and swap the screen before this
    /// controller ever got a chance to darken Lore first. Kept separate from
    /// <see cref="IntroController"/>, which stays a pure progression
    /// side-effect observer of whatever navigation already happened — it does
    /// not care who drove it, so <see cref="IScreenNavigator.Show"/> called
    /// here still marks Lore complete exactly as before.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LoreExitController : MonoBehaviour
    {
        private const string NextScreenId = "menu";
        private const string SkipButtonName = "lore-skip";
        private const string ContinueButtonName = "lore-continue";
        private const int MaxRootResolveFrames = 30;

        /// <summary>How long Lore darkens to black (Phase A) before the screen swaps to Main Menu.</summary>
        private const float FadeOutSeconds = 0.5f;

        /// <summary>How long the screen holds on full black — Main Menu is activated and its video allowed to start during this hold, all while fully covered.</summary>
        private const float BlackHoldSeconds = 0.12f;

        /// <summary>How long Main Menu fades in from black (Phase B) once it is the active screen.</summary>
        private const float FadeInSeconds = 0.72f;

        private IScreenNavigator _navigator;
        private ITransitionOverlay _overlay;
        private Button _skipButton;
        private Button _continueButton;
        private Coroutine _bindRoutine;
        private Coroutine _exitRoutine;

        private void OnEnable()
        {
            _navigator = GetComponent<IScreenNavigator>();
            _overlay = GetComponent<ITransitionOverlay>();
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_exitRoutine != null)
            {
                StopCoroutine(_exitRoutine);
                _exitRoutine = null;
            }

            if (_skipButton != null)
                _skipButton.clicked -= OnExitClicked;
            if (_continueButton != null)
                _continueButton.clicked -= OnExitClicked;
            _skipButton = null;
            _continueButton = null;

            _navigator = null;
            _overlay = null;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[LoreExitController] UIDocument root unavailable; Lore exit not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;
            _skipButton = root.Q<Button>(SkipButtonName);
            _continueButton = root.Q<Button>(ContinueButtonName);

            if (_skipButton == null || _continueButton == null)
            {
                Debug.LogError("[LoreExitController] Lore skip/continue buttons missing; not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _skipButton.clicked += OnExitClicked;
            _continueButton.clicked += OnExitClicked;

            _bindRoutine = null;
        }

        private void OnExitClicked()
        {
            if (_exitRoutine != null || _navigator == null)
                return;

            _exitRoutine = StartCoroutine(ExitRoutine());
        }

        /// <summary>
        /// True two-phase transition. Phase A: Lore darkens to black — by the
        /// end of this phase the screen must be fully black. While still fully
        /// black, Main Menu is activated (its background video is already
        /// prepared — see IShellPreloader/BackgroundMediaController — so it
        /// begins playing immediately, underneath the opaque overlay, with no
        /// blank-frame/initialization flash by the time it becomes visible).
        /// Phase B: the overlay fades out, gradually revealing Main Menu out
        /// of black rather than an instant bright reveal.
        /// </summary>
        private IEnumerator ExitRoutine()
        {
            // Phase A: Lore -> full black.
            if (_overlay != null)
                yield return StartCoroutine(_overlay.FadeToBlack(FadeOutSeconds));

            // Still fully black: activate Main Menu (and let its already-prepared
            // video start) before any reveal begins.
            yield return new WaitForSecondsRealtime(BlackHoldSeconds);
            _navigator.Show(NextScreenId);

            // Phase B: full black -> Main Menu revealed.
            if (_overlay != null)
                yield return StartCoroutine(_overlay.FadeFromBlack(FadeInSeconds));

            _exitRoutine = null;
        }
    }
}
