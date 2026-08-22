using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.Backend
{
    /// <summary>
    /// Блок аккаунта на экране profileDetails. Повторяет приём привязки,
    /// принятый в проекте: ждать rootVisualElement корутиной и подписываться на
    /// вход на экран через <see cref="IScreenNavigator"/>, потому что общий
    /// GameObject "UI" всегда включён и OnEnable здесь ничего не значит.
    ///
    /// Когда вход недоступен (редактор), блок скрывается целиком: показывать
    /// кнопку, которая заведомо ничего не сделает, — хуже, чем не показывать.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AccountPanelController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "profileDetails";

        private VisualElement _panel;
        private Label _status;
        private Button _signIn;
        private Button _signOut;
        private Button _delete;

        private SyncService _sync;
        private IScreenNavigator _navigator;
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
                if (_signIn != null) _signIn.clicked -= OnSignIn;
                if (_signOut != null) _signOut.clicked -= OnSignOut;
                if (_delete != null) _delete.clicked -= OnDelete;
                if (_sync != null) _sync.Changed -= Render;
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            _panel = null;
            _status = null;
            _signIn = null;
            _signOut = null;
            _delete = null;
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
                    Debug.LogError("[AccountPanelController] UIDocument root недоступен.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;
            _panel = root.Q<VisualElement>("account-panel");
            _status = root.Q<Label>("account-status");
            _signIn = root.Q<Button>("account-sign-in");
            _signOut = root.Q<Button>("account-sign-out");
            _delete = root.Q<Button>("account-delete");

            if (_panel == null)
            {
                Debug.LogError("[AccountPanelController] блок аккаунта не найден в вёрстке.", this);
                _bindRoutine = null;
                yield break;
            }

            _sync = GetComponent<SyncService>();

            if (_signIn != null) _signIn.clicked += OnSignIn;
            if (_signOut != null) _signOut.clicked += OnSignOut;
            if (_delete != null) _delete.clicked += OnDelete;
            if (_sync != null) _sync.Changed += Render;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            _bound = true;
            _bindRoutine = null;
            Render();
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
                Render();
        }

        private void OnSignIn() => _sync?.SignIn();

        private void OnSignOut() => _sync?.SignOut();

        private void OnDelete() => _sync?.DeleteAccount();

        private void Render()
        {
            if (_panel == null)
                return;

            bool available = _sync != null && _sync.CanSignIn;
            _panel.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            if (!available)
                return;

            bool signedIn = _sync.IsSignedIn;

            if (_status != null)
            {
                _status.text = signedIn
                    ? "Прогресс сохраняется в аккаунте Google"
                    : "Прогресс хранится только на этом телефоне";
            }

            if (_signIn != null)
                _signIn.style.display = signedIn ? DisplayStyle.None : DisplayStyle.Flex;
            if (_signOut != null)
                _signOut.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            if (_delete != null)
                _delete.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
