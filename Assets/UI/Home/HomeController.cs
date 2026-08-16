using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Home
{
    /// <summary>
    /// Drives the Main Menu ("menu") screen — the cinematic PLAY / PLANS / SETTINGS
    /// / QUIT navigation shown over the full-bleed menu video. PLAY is a plain
    /// ScreenManager screen-navigator button targeting the Map screen (no gating,
    /// so it needs no controller code of its own); SETTINGS opens the one shared
    /// Settings modal (see Mikey.UI.Settings.SettingsModalController, which finds
    /// and wires "menu-settings-open" itself — this controller no longer knows
    /// anything about Settings). This controller owns only the local Plans
    /// overlay (shown on top of the menu without leaving the screen — the menu
    /// video/music underneath is untouched by BackgroundMediaController/
    /// AudioController, which both key off the screen id alone) and QUIT's
    /// platform-specific behavior. Formerly the old Home dashboard's controller
    /// (dynamic CTA + Map/Techniques dock locking); that entire old design is
    /// retired with this rebuild.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HomeController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller reacts to (Main Menu's own entry resets any open modal).</summary>
        public const string ScreenId = "menu";

        private VisualElement _plansModal;
        private Button _plansOpenButton;
        private Button _plansCloseButton;
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

            _plansModal = null;
            _plansOpenButton = null;
            _plansCloseButton = null;
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

            _plansModal = root.Q<VisualElement>("menu-plans-modal");
            _plansOpenButton = root.Q<Button>("menu-plans-open");
            _plansCloseButton = root.Q<Button>("menu-plans-close");
            _quitButton = root.Q<Button>("menu-quit");

            if (_plansModal == null || _plansOpenButton == null || _plansCloseButton == null || _quitButton == null)
            {
                Debug.LogError("[HomeController] Main Menu elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            BindButton(_plansOpenButton, () => ShowModal(_plansModal));
            BindButton(_plansCloseButton, () => HideModal(_plansModal));
            BindButton(_quitButton, OnQuitClicked);

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            HideModal(_plansModal);

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

        /// <summary>Re-entering Main Menu always resets the Plans overlay left open on a previous visit (the shared Settings modal manages its own state — see SettingsModalController).</summary>
        private void OnScreenEntered(string screenId)
        {
            if (screenId != ScreenId)
                return;

            HideModal(_plansModal);
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
