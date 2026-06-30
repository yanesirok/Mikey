using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the screen-flow for Mikey's UI Toolkit frontend.
///
/// Convention (mirrors the mockup's data-go pattern):
///   - A screen is any element with the USS class "screen"; its element name
///     is the screen id (e.g. "title", "menu").
///   - A navigator is any element named "go-&lt;screenId&gt;". Clicking it shows
///     that screen. Buttons and plain elements (e.g. "tap to begin") both work.
///
/// To add a screen later: add a VisualElement with class "screen" and a unique
/// name in MikeyApp.uxml, then add buttons named "go-&lt;thatName&gt;". No code or
/// Inspector wiring needed.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ScreenManager : MonoBehaviour, IScreenNavigator
{
    [Tooltip("Screen shown first when the app starts.")]
    [SerializeField] private string startScreen = "title";

    private const string NavPrefix = "go-";
    private const int MaxRootResolveFrames = 30;

    private readonly List<VisualElement> _screens = new List<VisualElement>();
    private readonly List<NavigatorBinding> _navigatorBindings = new List<NavigatorBinding>();

    private Coroutine _initRoutine;
    private bool _initialized;

    /// <summary>
    /// Raised when <see cref="Show"/> actually changes the shown screen, carrying the
    /// new screen id. Fired only on a genuine change, so a re-request of the current
    /// screen does not re-notify (and controllers don't reset while already active).
    /// </summary>
    public event Action<string> ScreenChanged;

    /// <summary>The id of the screen currently shown (null before the first show).</summary>
    public string CurrentScreen { get; private set; }

    private void OnEnable()
    {
        if (_initialized)
            return;
        _initRoutine = StartCoroutine(InitializeWhenReady());
    }

    private IEnumerator InitializeWhenReady()
    {
        UIDocument doc = GetComponent<UIDocument>();

        int frames = 0;
        while (doc.rootVisualElement == null)
        {
            if (++frames > MaxRootResolveFrames)
            {
                Debug.LogError("[ScreenManager] UIDocument root unavailable; screens cannot be wired.", this);
                _initRoutine = null;
                yield break;
            }
            yield return null;
        }

        VisualElement root = doc.rootVisualElement;

        // Collect every screen (root-level panels carry the "screen" class).
        _screens.Clear();
        root.Query<VisualElement>(className: "screen").ForEach(screen => _screens.Add(screen));

        // Wire every navigator: an element named "go-<screenId>" jumps to that screen.
        root.Query<VisualElement>().ForEach(element =>
        {
            if (string.IsNullOrEmpty(element.name) || !element.name.StartsWith(NavPrefix))
                return;

            string target = element.name.Substring(NavPrefix.Length);

            if (element is Button button)
            {
                Action callback = () => Show(target);
                button.clicked += callback;
                _navigatorBindings.Add(NavigatorBinding.ForButton(button, callback));
            }
            else
            {
                EventCallback<ClickEvent> callback = _ => Show(target);
                element.RegisterCallback(callback);
                _navigatorBindings.Add(NavigatorBinding.ForElement(element, callback));
            }
        });

        _initialized = true;
        _initRoutine = null;
        Show(startScreen);
    }

    private void OnDisable()
    {
        if (_initRoutine != null)
        {
            StopCoroutine(_initRoutine);
            _initRoutine = null;
        }

        for (int i = 0; i < _navigatorBindings.Count; i++)
            _navigatorBindings[i].Unbind();

        _navigatorBindings.Clear();
        _screens.Clear();
        _initialized = false;
    }

    /// <summary>
    /// Shows the screen with the given id and hides all others. Raises
    /// <see cref="ScreenChanged"/> only when the shown screen actually changes, so
    /// listeners get exactly one entry signal per genuine navigation.
    /// </summary>
    public void Show(string screenId)
    {
        foreach (VisualElement screen in _screens)
            screen.style.display = screen.name == screenId ? DisplayStyle.Flex : DisplayStyle.None;

        if (CurrentScreen == screenId)
            return;

        CurrentScreen = screenId;
        ScreenChanged?.Invoke(screenId);
    }

    private readonly struct NavigatorBinding
    {
        private readonly Button _button;
        private readonly Action _buttonCallback;
        private readonly VisualElement _element;
        private readonly EventCallback<ClickEvent> _elementCallback;

        private NavigatorBinding(
            Button button,
            Action buttonCallback,
            VisualElement element,
            EventCallback<ClickEvent> elementCallback)
        {
            _button = button;
            _buttonCallback = buttonCallback;
            _element = element;
            _elementCallback = elementCallback;
        }

        public static NavigatorBinding ForButton(Button button, Action callback) =>
            new NavigatorBinding(button, callback, null, null);

        public static NavigatorBinding ForElement(VisualElement element, EventCallback<ClickEvent> callback) =>
            new NavigatorBinding(null, null, element, callback);

        public void Unbind()
        {
            if (_button != null && _buttonCallback != null)
                _button.clicked -= _buttonCallback;
            if (_element != null && _elementCallback != null)
                _element.UnregisterCallback(_elementCallback);
        }
    }
}
