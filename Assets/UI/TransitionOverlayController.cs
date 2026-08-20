using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Concrete implementation of the launch shell's one shared transition
/// overlay: a full-bleed black VisualElement ("transition-overlay" in
/// MikeyApp.uxml) declared last in the document, after every screen and the
/// shared Settings modal, so it always paints above whichever screen
/// ScreenManager currently has visible — the same "declared last, self-
/// managed, never touched by ScreenManager" pattern as shared-settings-modal.
/// Starts fully transparent and click-through; TitleController/LoreExitController
/// drive it through <see cref="ITransitionOverlay"/> to cover an instant
/// ScreenManager.Show swap so it reads as a smooth fade instead of a hard cut.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class TransitionOverlayController : MonoBehaviour, ITransitionOverlay
{
    private const string OverlayElementName = "transition-overlay";
    private const int MaxRootResolveFrames = 30;

    private VisualElement _overlay;
    private Coroutine _bindRoutine;

    private void OnEnable()
    {
        _bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (_bindRoutine != null)
        {
            StopCoroutine(_bindRoutine);
            _bindRoutine = null;
        }
        _overlay = null;
    }

    private IEnumerator BindWhenReady()
    {
        var document = GetComponent<UIDocument>();

        int frames = 0;
        while (document.rootVisualElement == null)
        {
            if (++frames > MaxRootResolveFrames)
            {
                Debug.LogError("[TransitionOverlayController] UIDocument root unavailable; overlay not bound.", this);
                _bindRoutine = null;
                yield break;
            }
            yield return null;
        }

        _overlay = document.rootVisualElement.Q<VisualElement>(OverlayElementName);
        if (_overlay == null)
            Debug.LogError("[TransitionOverlayController] 'transition-overlay' element missing; not bound.", this);

        _bindRoutine = null;
    }

    /// <inheritdoc />
    public IEnumerator FadeToBlack(float seconds) => Fade(1f, seconds);

    /// <inheritdoc />
    public IEnumerator FadeFromBlack(float seconds) => Fade(0f, seconds);

    /// <summary>
    /// Animates opacity toward <paramref name="targetOpacity"/> with a smoothstep
    /// ease — no bounce, restrained per the launch shell's design brief. The
    /// overlay only blocks input while covering the screen (mid-fade or fully
    /// opaque); once fully transparent it goes click-through so it never
    /// interferes with normal UI once a transition completes.
    /// </summary>
    private IEnumerator Fade(float targetOpacity, float seconds)
    {
        if (_overlay == null)
            yield break;

        _overlay.pickingMode = PickingMode.Position;
        float start = _overlay.resolvedStyle.opacity;

        float elapsed = 0f;
        while (seconds > 0f && elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            t = t * t * (3f - 2f * t); // smoothstep
            _overlay.style.opacity = Mathf.Lerp(start, targetOpacity, t);
            yield return null;
        }

        _overlay.style.opacity = targetOpacity;
        _overlay.pickingMode = targetOpacity <= 0f ? PickingMode.Ignore : PickingMode.Position;
    }
}
