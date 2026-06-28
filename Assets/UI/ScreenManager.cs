using System.Collections.Generic;
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
public class ScreenManager : MonoBehaviour
{
    [Tooltip("Screen shown first when the app starts.")]
    [SerializeField] private string startScreen = "title";

    private const string NavPrefix = "go-";

    private readonly List<VisualElement> _screens = new List<VisualElement>();

    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

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
                button.clicked += () => Show(target);
            else
                element.RegisterCallback<ClickEvent>(_ => Show(target));
        });

        Show(startScreen);
    }

    /// <summary>Shows the screen with the given id and hides all others.</summary>
    public void Show(string screenId)
    {
        foreach (VisualElement screen in _screens)
            screen.style.display = screen.name == screenId ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
