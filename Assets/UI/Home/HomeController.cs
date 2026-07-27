using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Home
{
    /// <summary>
    /// Drives the Home ("menu") screen's progression-aware UI: the single dynamic
    /// primary CTA (label + destination change with <see cref="TutorialProgressState"/>,
    /// see <see cref="TutorialProgressPresenter"/>) and the Map/Techniques dock
    /// tabs' locked state (dimmed, non-navigating, with a "COMPLETE LVL 0" badge on
    /// Map) before Level 1 unlocks. The CTA and the two gated tabs are deliberately
    /// NOT "go-" navigators — ScreenManager must not statically wire them, since
    /// their behavior depends on progression state — mirroring how
    /// MapLevelPreviewController/PracticeController own their own local,
    /// controller-bound actions. The Profile tab stays a plain, always-available
    /// "go-profile" navigator, unaffected by this controller.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HomeController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller reacts to (Home's own entry re-renders the CTA/lock state).</summary>
        public const string ScreenId = "menu";

        private const string LockedClass = "home-tab--locked";

        private Button _cta;
        private Label _ctaText;
        private VisualElement _navMap;
        private VisualElement _navTechniques;
        private VisualElement _devBar;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

        private EventCallback<ClickEvent> _navMapClickCallback;
        private EventCallback<ClickEvent> _navTechniquesClickCallback;
        private readonly List<ButtonBinding> _devButtonBindings = new List<ButtonBinding>();

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
                if (_cta != null)
                    _cta.clicked -= OnCtaClicked;
                if (_navMap != null && _navMapClickCallback != null)
                    _navMap.UnregisterCallback(_navMapClickCallback);
                if (_navTechniques != null && _navTechniquesClickCallback != null)
                    _navTechniques.UnregisterCallback(_navTechniquesClickCallback);

                for (int i = 0; i < _devButtonBindings.Count; i++)
                    _devButtonBindings[i].Unbind();
                _devButtonBindings.Clear();
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenEntered;
                _navigator = null;
            }

            if (_progress != null)
            {
                _progress.Changed -= Render;
                _progress = null;
            }

            _cta = null;
            _ctaText = null;
            _navMap = null;
            _navTechniques = null;
            _devBar = null;
            _navMapClickCallback = null;
            _navTechniquesClickCallback = null;
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
                    Debug.LogError("[HomeController] UIDocument root unavailable; Home screen not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _cta = root.Q<Button>("home-cta");
            _navMap = root.Q<VisualElement>("home-nav-map");
            _navTechniques = root.Q<VisualElement>("home-nav-techniques");

            if (_cta == null || _navMap == null || _navTechniques == null)
            {
                Debug.LogError("[HomeController] Home screen elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _ctaText = _cta.Q<Label>(className: "home-cta__text");

            _cta.clicked += OnCtaClicked;

            _navMapClickCallback = _ => OnNavMapClicked();
            _navMap.RegisterCallback(_navMapClickCallback);

            _navTechniquesClickCallback = _ => OnNavTechniquesClicked();
            _navTechniques.RegisterCallback(_navTechniquesClickCallback);

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenEntered;

            _progress = GetComponent<ITutorialProgress>();
            if (_progress != null)
                _progress.Changed += Render;

            _devBar = root.Q<VisualElement>("home-devbar");
            WireDevControls(root);

            _bound = true;
            _bindRoutine = null;

            Render();
        }

        /// <summary>
        /// Compact, visually separate tutorial-progression state switcher, visible
        /// only in the Editor or a Development Build (absent from production
        /// builds) — mirrors CombineScreenController's dev-switcher gate. Covers
        /// only the currently reachable states; no 30-day Vow simulation yet.
        /// </summary>
        private void WireDevControls(VisualElement root)
        {
            bool allowed = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            allowed = true;
#endif
            if (_devBar != null)
                _devBar.style.display = allowed ? DisplayStyle.Flex : DisplayStyle.None;

            if (!allowed || _progress == null)
                return;

            BindDevButton(root, "home-dev-reset", () => _progress.Reset());
            BindDevButton(root, "home-dev-new-player", () => _progress.SetState(TutorialProgressState.NewPlayer));
            BindDevButton(root, "home-dev-combine-started", () => _progress.SetState(TutorialProgressState.CombineStarted));
            BindDevButton(root, "home-dev-level1-unlocked", () => _progress.SetState(TutorialProgressState.Level1Unlocked));
            BindDevButton(root, "home-dev-lesson-started", () => _progress.SetState(TutorialProgressState.LessonStarted));
            BindDevButton(root, "home-dev-lesson-completed", () => _progress.SetState(TutorialProgressState.LessonCompleted));
        }

        private void BindDevButton(VisualElement root, string name, Action onClick)
        {
            var button = root.Q<Button>(name);
            if (button == null)
                return;

            button.clicked += onClick;
            _devButtonBindings.Add(new ButtonBinding(button, onClick));
        }

        private void OnCtaClicked()
        {
            if (_progress == null || _navigator == null)
                return;

            HomeCtaSpec spec = TutorialProgressPresenter.HomeCtaFor(_progress.State);
            _navigator.Show(spec.Destination);
        }

        private void OnNavMapClicked()
        {
            if (_progress == null || _navigator == null)
                return;
            if (!TutorialProgressPresenter.IsMapUnlocked(_progress.State))
                return; // Locked: Combine (LVL0) must be completed first.

            _navigator.Show("map");
        }

        private void OnNavTechniquesClicked()
        {
            if (_progress == null || _navigator == null)
                return;
            if (!TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))
                return; // Locked: Combine (LVL0) must be completed first.

            _navigator.Show("techniques");
        }

        /// <summary>Re-renders whenever Home is (re)entered, so a state change made elsewhere is always reflected.</summary>
        private void OnScreenEntered(string screenId)
        {
            if (screenId == ScreenId)
                Render();
        }

        private void Render()
        {
            if (_progress == null)
                return;

            TutorialProgressState state = _progress.State;

            if (_ctaText != null)
                _ctaText.text = TutorialProgressPresenter.HomeCtaFor(state).Label;

            SetLocked(_navMap, !TutorialProgressPresenter.IsMapUnlocked(state));
            SetLocked(_navTechniques, !TutorialProgressPresenter.IsTechniquesUnlocked(state));
        }

        private static void SetLocked(VisualElement tab, bool locked)
        {
            if (tab == null)
                return;

            if (locked)
                tab.AddToClassList(LockedClass);
            else
                tab.RemoveFromClassList(LockedClass);

            // The "COMPLETE LVL 0" badge (Map only) mirrors the lock state; Techniques
            // has no badge element, so this is a safe no-op there.
            Label badge = tab.Q<Label>(className: "home-tab__badge");
            if (badge != null)
                badge.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
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
