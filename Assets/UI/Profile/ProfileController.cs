using System.Collections;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Gates Profile's dock Map/Techniques tabs behind the same Level1Unlocked
    /// progression check Home already uses (see <see cref="TutorialProgressPresenter"/>),
    /// so a production player can no longer bypass Home's lock by reaching
    /// Map/Techniques from Profile before completing Combine (LVL0) — mirrors
    /// HomeController's home-nav-map/home-nav-techniques gating. The dock's
    /// remaining tabs (Home, the active Profile tab) are untouched, static
    /// "go-" navigators. Profile itself always stays reachable; this is a pure
    /// navigation-gating fix with no visual/markup redesign.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProfileController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        private VisualElement _navMap;
        private VisualElement _navTechniques;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

        private EventCallback<ClickEvent> _navMapClickCallback;
        private EventCallback<ClickEvent> _navTechniquesClickCallback;

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
                if (_navMap != null && _navMapClickCallback != null)
                    _navMap.UnregisterCallback(_navMapClickCallback);
                if (_navTechniques != null && _navTechniquesClickCallback != null)
                    _navTechniques.UnregisterCallback(_navTechniquesClickCallback);
            }

            _navigator = null;
            _progress = null;
            _navMap = null;
            _navTechniques = null;
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
                    Debug.LogError("[ProfileController] UIDocument root unavailable; Profile navigation gating not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;

            _navMap = root.Q<VisualElement>("profile-nav-map");
            _navTechniques = root.Q<VisualElement>("profile-nav-techniques");

            if (_navMap == null || _navTechniques == null)
            {
                Debug.LogError("[ProfileController] Profile dock elements missing; navigation gating not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _navigator = GetComponent<IScreenNavigator>();
            _progress = GetComponent<ITutorialProgress>();

            _navMapClickCallback = _ => OnNavMapClicked();
            _navMap.RegisterCallback(_navMapClickCallback);

            _navTechniquesClickCallback = _ => OnNavTechniquesClicked();
            _navTechniques.RegisterCallback(_navTechniquesClickCallback);

            _bound = true;
            _bindRoutine = null;
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
    }
}
