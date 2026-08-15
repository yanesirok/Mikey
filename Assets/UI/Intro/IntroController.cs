using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;

namespace Mikey.UI.Intro
{
    /// <summary>
    /// Marks tutorial progression's <see cref="TutorialProgressState.IntroCompleted"/>
    /// the moment the player leaves the Intro screen through its normal flow (Skip or
    /// the primary CTA) — both are plain "go-menu" ScreenManager navigators with no
    /// controller of their own, so nothing previously recorded this transition.
    /// Purely a progression side-effect: navigation itself is untouched, still
    /// driven by ScreenManager exactly as before. Uses the forward-only
    /// <see cref="ITutorialProgress.Advance"/>, so a player already beyond
    /// IntroCompleted is never regressed by this controller.
    /// </summary>
    public sealed class IntroController : MonoBehaviour
    {
        /// <summary>The screen id whose exit marks Intro complete.</summary>
        public const string ScreenId = "intro";

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;
        private string _previousScreen;

        private void OnEnable()
        {
            _navigator = GetComponent<IScreenNavigator>();
            _progress = GetComponent<ITutorialProgress>();

            _previousScreen = _navigator?.CurrentScreen;

            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;
        }

        private void OnDisable()
        {
            if (_navigator != null)
                _navigator.ScreenChanged -= OnScreenChanged;

            _navigator = null;
            _progress = null;
            _previousScreen = null;
        }

        /// <summary>Fires on every genuine screen change; advances progress on a genuine Intro exit.</summary>
        private void OnScreenChanged(string screenId)
        {
            if (IsIntroExit(_previousScreen, screenId))
                _progress?.Advance(TutorialProgressState.IntroCompleted);

            _previousScreen = screenId;
        }

        /// <summary>
        /// Pure decision: true only when the previous screen was Intro and the new
        /// screen is something else — i.e. Intro was just completed or skipped
        /// through its normal flow. Unit-tested.
        /// </summary>
        public static bool IsIntroExit(string previousScreenId, string newScreenId) =>
            previousScreenId == ScreenId && newScreenId != ScreenId;
    }
}
