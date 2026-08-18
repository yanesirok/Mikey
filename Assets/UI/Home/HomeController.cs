using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Home
{
    /// <summary>
    /// Drives the Main Menu ("menu") screen — the cinematic PLAY / VOW / SETTINGS
    /// / QUIT navigation shown over the full-bleed menu video. PLAY is a plain
    /// ScreenManager screen-navigator button targeting the Map screen (no gating,
    /// so it needs no controller code of its own); SETTINGS opens the one shared
    /// Settings modal (see Mikey.UI.Settings.SettingsModalController, which finds
    /// and wires "menu-settings-open" itself — this controller no longer knows
    /// anything about Settings). This controller owns only the local VOW
    /// membership overlay (shown on top of the menu without leaving the screen —
    /// the menu video/music underneath is untouched by BackgroundMediaController/
    /// AudioController, which both key off the screen id alone; formerly a small
    /// "Plans / Coming soon" placeholder, now a full presentation-only membership
    /// choice — no payment/backend, no persistence) and QUIT's platform-specific
    /// behavior. Formerly the old Home dashboard's controller (dynamic CTA +
    /// Map/Techniques dock locking); that entire old design is retired with
    /// this rebuild.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HomeController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller reacts to (Main Menu's own entry resets any open modal).</summary>
        public const string ScreenId = "menu";

        private const string VowSelectedClass = "vow-option--selected";
        private const string VowMessageVisibleClass = "vow-inline-message--visible";
        private const string VowEnrollmentMessage = "Enrollment will open soon.";

        private VisualElement _vowModal;
        private Button _vowOpenButton;
        private Button _vowCloseButton;
        private Button _vowOptionInitiate;
        private Button _vowOptionDisciple;
        private Button _vowOptionMaster;
        private Label _vowInlineMessage;
        private Button _quitButton;

        private IScreenNavigator _navigator;

        private readonly List<ButtonBinding> _buttonBindings = new List<ButtonBinding>();

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
                for (int i = 0; i < _buttonBindings.Count; i++)
                    _buttonBindings[i].Unbind();
                _buttonBindings.Clear();
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenEntered;
                _navigator = null;
            }

            _vowModal = null;
            _vowOpenButton = null;
            _vowCloseButton = null;
            _vowOptionInitiate = null;
            _vowOptionDisciple = null;
            _vowOptionMaster = null;
            _vowInlineMessage = null;
            _quitButton = null;
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
                    Debug.LogError("[HomeController] UIDocument root unavailable; Main Menu not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _vowModal = root.Q<VisualElement>("menu-vow-modal");
            _vowOpenButton = root.Q<Button>("menu-vow-open");
            _vowCloseButton = root.Q<Button>("menu-vow-close");
            _vowOptionInitiate = root.Q<Button>("vow-option-initiate");
            _vowOptionDisciple = root.Q<Button>("vow-option-disciple");
            _vowOptionMaster = root.Q<Button>("vow-option-master");
            _vowInlineMessage = root.Q<Label>("vow-inline-message");
            _quitButton = root.Q<Button>("menu-quit");

            if (_vowModal == null || _vowOpenButton == null || _vowCloseButton == null
                || _vowOptionInitiate == null || _vowOptionDisciple == null || _vowOptionMaster == null
                || _vowInlineMessage == null || _quitButton == null)
            {
                Debug.LogError("[HomeController] Main Menu elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            BindButton(_vowOpenButton, OnVowOpened);
            BindButton(_vowCloseButton, () => HideModal(_vowModal));
            BindButton(_vowOptionInitiate, () => SelectVow(_vowOptionInitiate, showEnrollmentMessage: false));
            BindButton(_vowOptionDisciple, () => SelectVow(_vowOptionDisciple, showEnrollmentMessage: true));
            BindButton(_vowOptionMaster, () => SelectVow(_vowOptionMaster, showEnrollmentMessage: true));
            BindButton(_quitButton, OnQuitClicked);

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            HideModal(_vowModal);
            ResetVowSelection();

            _bound = true;
            _bindRoutine = null;
        }

        private void BindButton(Button button, Action onClick)
        {
            button.clicked += onClick;
            _buttonBindings.Add(new ButtonBinding(button, onClick));
        }

        private static void ShowModal(VisualElement modal)
        {
            if (modal != null)
                modal.style.display = DisplayStyle.Flex;
        }

        private static void HideModal(VisualElement modal)
        {
            if (modal != null)
                modal.style.display = DisplayStyle.None;
        }

        /// <summary>Main Menu is Mobile-first (Android) but QUIT is never platform-hidden — only its behavior differs.</summary>
        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            Debug.Log("[HomeController] Quit requested — no-op in the Editor.");
#else
            Application.Quit();
#endif
        }

        private void OnVowOpened()
        {
            ShowModal(_vowModal);
            ResetVowSelection();
        }

        /// <summary>Disciple (Recommended) is the default selection every time the Vow overlay opens; no enrollment message on a plain open — only an explicit press of a paid option shows it.</summary>
        private void ResetVowSelection() => SelectVow(_vowOptionDisciple, showEnrollmentMessage: false);

        /// <summary>
        /// Visual-only selection: exactly one of the three Vow options is marked
        /// selected at a time. Pressing Disciple or Master's "Choose Vow" also
        /// surfaces a small inline notice that enrollment isn't live yet — this
        /// is frontend presentation only, so it never fakes a successful
        /// activation, never opens another modal, and never saves the
        /// choice anywhere — no persistence of any kind.
        /// </summary>
        private void SelectVow(Button option, bool showEnrollmentMessage)
        {
            _vowOptionInitiate.RemoveFromClassList(VowSelectedClass);
            _vowOptionDisciple.RemoveFromClassList(VowSelectedClass);
            _vowOptionMaster.RemoveFromClassList(VowSelectedClass);
            option.AddToClassList(VowSelectedClass);

            if (showEnrollmentMessage)
            {
                _vowInlineMessage.text = VowEnrollmentMessage;
                _vowInlineMessage.AddToClassList(VowMessageVisibleClass);
            }
            else
            {
                _vowInlineMessage.text = string.Empty;
                _vowInlineMessage.RemoveFromClassList(VowMessageVisibleClass);
            }
        }

        /// <summary>Re-entering Main Menu always resets the Vow overlay left open on a previous visit (the shared Settings modal manages its own state — see SettingsModalController).</summary>
        private void OnScreenEntered(string screenId)
        {
            if (screenId != ScreenId)
                return;

            HideModal(_vowModal);
            ResetVowSelection();
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
