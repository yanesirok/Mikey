using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Title
{
    /// <summary>
    /// Drives the Logo Intro ("title") screen — the app's very first screen: shows
    /// the static Mikey logo for a short beat, then opens Lore ("intro"), either
    /// automatically after <see cref="autoAdvanceSeconds"/> or immediately on a
    /// tap/click anywhere on the screen. A single <see cref="_navigated"/> guard
    /// ensures only one of those two triggers ever actually navigates, so a tap
    /// landing right as the timer fires can never double-navigate. Unlike every
    /// other screen's action, this is not a ScreenManager "go-" navigator — Title
    /// has no button, so it drives <see cref="IScreenNavigator.Show"/> itself.
    /// Swapping the static logo for a dedicated logo-animation video later only
    /// touches the UXML/USS content inside the "title" screen; this timer/tap
    /// behavior does not need to change.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TitleController : MonoBehaviour
    {
        /// <summary>The screen id this controller drives.</summary>
        public const string ScreenId = "title";

        /// <summary>Where Logo Intro always advances to, whether by timer or tap.</summary>
        public const string NextScreenId = "intro";

        private const int MaxRootResolveFrames = 30;

        [SerializeField]
        [Tooltip("Seconds before the Logo screen auto-advances to Lore (spec: ~2.5-3s).")]
        [Range(2.5f, 3f)]
        private float autoAdvanceSeconds = 2.75f;

        private IScreenNavigator _navigator;
        private VisualElement _titleScreen;
        private EventCallback<ClickEvent> _tapCallback;
        private Coroutine _bindRoutine;
        private Coroutine _timerRoutine;
        private bool _navigated;

        private void OnEnable()
        {
            _navigated = false;
            _navigator = GetComponent<IScreenNavigator>();
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_timerRoutine != null)
            {
                StopCoroutine(_timerRoutine);
                _timerRoutine = null;
            }

            if (_titleScreen != null && _tapCallback != null)
                _titleScreen.UnregisterCallback(_tapCallback);

            _titleScreen = null;
            _tapCallback = null;
            _navigator = null;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[TitleController] UIDocument root unavailable; Logo screen not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _titleScreen = document.rootVisualElement.Q<VisualElement>(ScreenId);
            if (_titleScreen == null)
            {
                Debug.LogError("[TitleController] 'title' screen element missing; not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _tapCallback = _ => Advance();
            _titleScreen.RegisterCallback(_tapCallback);

            _timerRoutine = StartCoroutine(AutoAdvanceAfterDelay());
            _bindRoutine = null;
        }

        private IEnumerator AutoAdvanceAfterDelay()
        {
            yield return new WaitForSeconds(autoAdvanceSeconds);
            _timerRoutine = null;
            Advance();
        }

        /// <summary>Navigates to Lore exactly once, however it was triggered (timer or tap).</summary>
        private void Advance()
        {
            if (_navigated || _navigator == null)
                return;
            _navigated = true;

            if (_timerRoutine != null)
            {
                StopCoroutine(_timerRoutine);
                _timerRoutine = null;
            }
            if (_titleScreen != null && _tapCallback != null)
            {
                _titleScreen.UnregisterCallback(_tapCallback);
                _tapCallback = null;
            }

            _navigator.Show(NextScreenId);
        }
    }
}
