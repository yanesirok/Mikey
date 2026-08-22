using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.Backend;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Home
{
    /// <summary>
    /// Drives the Sign In ("menu") screen — the app's one authentication gate,
    /// shown over the full-bleed cinematic that used to back the PLAY / VOW /
    /// SETTINGS / QUIT menu. That worded menu is retired: the screen now carries
    /// exactly two round, icon-only actions (the Google mark and a guest glyph)
    /// plus one inline status line, the shape mobile games like PUBG Mobile use.
    ///
    /// Neither action is a ScreenManager "go-" navigator — this controller owns
    /// both, because where they lead depends on state, not on markup:
    /// <see cref="NextScreenAfterAuth"/> opens Lore ("intro") on a genuine first
    /// launch and the Map hub ("map") on every launch after that, keyed off the
    /// same <see cref="TutorialProgressState.IntroCompleted"/> that
    /// Mikey.UI.Intro.IntroController already sets when Lore is finished or
    /// skipped. Nothing in the app navigates back here.
    ///
    /// Bound through <see cref="IScreenNavigator.ScreenChanged"/> rather than
    /// MonoBehaviour OnEnable/OnDisable, which only fire once for the shared,
    /// always-enabled "UI" GameObject.
    ///
    /// Exactly one thing skips this screen: an existing session. A returning
    /// player is handed straight through with a plain synchronous
    /// <see cref="IScreenNavigator.Show"/> and NO transition of its own, because
    /// TitleController is still mid-transition at that moment (it swaps to this
    /// screen while fully covered by the shared overlay, then fades in) — starting
    /// a second fade here would fight it and flash the gate. Only a real button
    /// press, where the player is already looking at this screen, gets its own
    /// cinematic fade.
    ///
    /// Everyone else sees the gate, including on platforms where Google sign-in
    /// is impossible (the Editor — Credential Manager does not exist there, see
    /// Mikey.Backend.AndroidGoogleAuth). The guest action always works, so there
    /// is always a way through; pressing Google where it cannot work says so on
    /// the status line instead of leaving a dead button.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HomeController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller drives.</summary>
        public const string ScreenId = "menu";

        /// <summary>Where a first-launch player goes after the gate.</summary>
        public const string LoreScreenId = "intro";

        /// <summary>Where a returning player goes after the gate — the app's hub from here on.</summary>
        public const string MapScreenId = "map";

        private const string SignInButtonName = "menu-google-signin";
        private const string GuestButtonName = "menu-guest-continue";
        private const string StatusLabelName = "menu-auth-status";

        private const string SigningInMessage = "Signing in...";
        private const string SignInFailedMessage = "Sign-in did not complete. Tap to try again.";
        private const string SignInUnavailableMessage = "Google sign-in is not available on this device.";

        /// <summary>How long the gate darkens to black before the next screen is swapped in.</summary>
        private const float FadeToBlackSeconds = 0.45f;

        /// <summary>How long the screen holds on full black while the next screen is activated underneath.</summary>
        private const float BlackHoldSeconds = 0.12f;

        /// <summary>How long the next screen fades in from black.</summary>
        private const float FadeInSeconds = 0.7f;

        private Button _signInButton;
        private Button _guestButton;
        private Label _status;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;
        private ITransitionOverlay _overlay;
        private SyncService _sync;

        private readonly List<ButtonBinding> _buttonBindings = new List<ButtonBinding>();

        private Coroutine _bindRoutine;
        private Coroutine _leaveRoutine;
        private bool _bound;
        private bool _leaving;

        /// <summary>
        /// Pure decision: Lore is a first-launch-only screen, so anything at or
        /// past <see cref="TutorialProgressState.IntroCompleted"/> goes straight
        /// to the Map. Unit-tested.
        /// </summary>
        public static string NextScreenAfterAuth(TutorialProgressState state) =>
            state >= TutorialProgressState.IntroCompleted ? MapScreenId : LoreScreenId;

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
            if (_leaveRoutine != null)
            {
                StopCoroutine(_leaveRoutine);
                _leaveRoutine = null;
            }

            for (int i = 0; i < _buttonBindings.Count; i++)
                _buttonBindings[i].Unbind();
            _buttonBindings.Clear();

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenEntered;
                _navigator = null;
            }

            if (_sync != null)
            {
                _sync.Changed -= Render;
                _sync = null;
            }

            _signInButton = null;
            _guestButton = null;
            _status = null;
            _progress = null;
            _overlay = null;
            _bound = false;
            _leaving = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[HomeController] UIDocument root unavailable; Sign In not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;
            _signInButton = root.Q<Button>(SignInButtonName);
            _guestButton = root.Q<Button>(GuestButtonName);
            _status = root.Q<Label>(StatusLabelName);

            if (_signInButton == null || _guestButton == null || _status == null)
            {
                Debug.LogError("[HomeController] Sign In elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            BindButton(_signInButton, OnSignInClicked);
            BindButton(_guestButton, OnGuestClicked);

            _progress = GetComponent<ITutorialProgress>();
            _overlay = GetComponent<ITransitionOverlay>();

            _sync = GetComponent<SyncService>();
            if (_sync != null)
                _sync.Changed += Render;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenEntered;
                if (_navigator.CurrentScreen == ScreenId)
                    OnScreenEntered(ScreenId);
            }

            _bound = true;
            _bindRoutine = null;
        }

        private void BindButton(Button button, Action onClick)
        {
            button.clicked += onClick;
            _buttonBindings.Add(new ButtonBinding(button, onClick));
        }

        /// <summary>
        /// Entering the gate either hands straight through (nothing to ask) or
        /// resets it to its idle state. The pass-through is deliberately a plain
        /// Show with no transition — see the class summary.
        /// </summary>
        private void OnScreenEntered(string screenId)
        {
            if (screenId != ScreenId)
                return;

            _leaving = false;

            // The one thing that skips the gate: a session already exists.
            if (_sync != null && _sync.IsSignedIn)
            {
                _navigator?.Show(NextScreen());
                return;
            }

            SetStatus(string.Empty);
            SetInteractable(true);
        }

        private void OnSignInClicked()
        {
            if (_leaving)
                return;

            // Say so rather than going dead: the guest action next to it still works.
            if (_sync == null || !_sync.CanSignIn)
            {
                SetStatus(SignInUnavailableMessage);
                return;
            }

            SetStatus(SigningInMessage);
            SetInteractable(false);
            _sync.SignIn();
        }

        private void OnGuestClicked()
        {
            if (_leaving)
                return;
            Leave();
        }

        /// <summary>
        /// Re-rendered on every SyncService state change while the gate is up: a
        /// session appearing is the success signal, and an attempt ending without
        /// one is an honest failure the player can retry — never a silent dead
        /// button.
        /// </summary>
        private void Render()
        {
            if (!_bound || _leaving || _sync == null)
                return;
            if (_navigator != null && _navigator.CurrentScreen != ScreenId)
                return;

            if (_sync.IsSignedIn)
            {
                Leave();
                return;
            }

            if (_sync.IsSigningIn)
            {
                SetStatus(SigningInMessage);
                SetInteractable(false);
                return;
            }

            SetStatus(SignInFailedMessage);
            SetInteractable(true);
        }

        private string NextScreen() =>
            NextScreenAfterAuth(_progress?.State ?? TutorialProgressState.NewPlayer);

        /// <summary>Leaves the gate exactly once, through the shared cinematic fade.</summary>
        private void Leave()
        {
            if (_leaving || _navigator == null)
                return;
            _leaving = true;

            SetInteractable(false);
            _leaveRoutine = StartCoroutine(LeaveRoutine());
        }

        /// <summary>
        /// Two-phase transition, the same shape LoreExitController uses: darken to
        /// full black, swap the screen while fully covered, then reveal out of
        /// black — never a hard cut.
        /// </summary>
        private IEnumerator LeaveRoutine()
        {
            if (_overlay != null)
                yield return StartCoroutine(_overlay.FadeToBlack(FadeToBlackSeconds));

            yield return new WaitForSecondsRealtime(BlackHoldSeconds);

            _navigator.Show(NextScreen());

            if (_overlay != null)
                yield return StartCoroutine(_overlay.FadeFromBlack(FadeInSeconds));

            _leaveRoutine = null;
        }

        private void SetStatus(string message)
        {
            if (_status != null)
                _status.text = message;
        }

        private void SetInteractable(bool interactable)
        {
            if (_signInButton != null)
                _signInButton.SetEnabled(interactable);
            if (_guestButton != null)
                _guestButton.SetEnabled(interactable);
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
