using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea
{
    /// <summary>
    /// Applies <see cref="Screen.safeArea"/> as padding to every
    /// ".safe-area-content" element in the attached UIDocument, so readable and
    /// interactive foreground content stays inside the device safe area while
    /// full-bleed siblings (decorative / camera layers) are left untouched.
    ///
    /// Recalculates on geometry changes and via a throttled poll (the poll catches
    /// the Landscape-Left to Landscape-Right flip, which does not change panel size
    /// and so never raises a GeometryChangedEvent). All work is change-gated by a
    /// five-input cache, and padding is assigned from a zero baseline so it can
    /// never accumulate.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SafeAreaController : MonoBehaviour
    {
        private const string TargetClass = "safe-area-content";
        private const long PollIntervalMs = 250;
        private const int MaxRootResolveFrames = 30;
        private const float GeometryGraceSeconds = 2f;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private readonly List<VisualElement> _targets = new List<VisualElement>();

        private EventCallback<GeometryChangedEvent> _onGeometry;
        private IVisualElementScheduledItem _poll;
        private Coroutine _initializationCoroutine;

        private bool _registered;
        private bool _initializing;

        // Five-input recalculation cache.
        private bool _hasApplied;
        private Rect _lastSafeArea;
        private float _lastScreenW;
        private float _lastScreenH;
        private float _lastPanelW;
        private float _lastPanelH;

        // Permanent-failure logging state.
        private float _initTime;
        private bool _geometryErrorLogged;

        private void OnEnable()
        {
            // Guard against duplicate initialization / registration.
            if (_registered || _initializing)
                return;

            _initializing = true;
            _initializationCoroutine = StartCoroutine(InitializeWhenReady());

            // If the coroutine finished synchronously (root already available), it has
            // cleared _initializing; drop the stale handle so OnDisable does not touch
            // a completed coroutine and a future OnEnable is not blocked.
            if (!_initializing)
                _initializationCoroutine = null;
        }

        private void OnDisable()
        {
            if (_initializationCoroutine != null)
            {
                StopCoroutine(_initializationCoroutine);
                _initializationCoroutine = null;
            }
            _initializing = false;

            if (_root != null && _onGeometry != null)
                _root.UnregisterCallback(_onGeometry);

            _poll?.Pause();
            _poll = null;

            _root = null;
            _targets.Clear();

            _registered = false;
            _hasApplied = false;
            _geometryErrorLogged = false;
            _lastSafeArea = default;
            _lastScreenW = 0f;
            _lastScreenH = 0f;
            _lastPanelW = 0f;
            _lastPanelH = 0f;
        }

        private IEnumerator InitializeWhenReady()
        {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null)
            {
                Debug.LogError("[SafeAreaController] No UIDocument on this GameObject; safe-area insets will not be applied.", this);
                _initializing = false;
                _initializationCoroutine = null;
                yield break;
            }

            // rootVisualElement can legitimately be null for a frame or two at startup.
            int frames = 0;
            while (_uiDocument.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[SafeAreaController] UIDocument root visual element unavailable; safe-area insets will not be applied.", this);
                    _initializing = false;
                    _initializationCoroutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = _uiDocument.rootVisualElement;

            _targets.Clear();
            root.Query<VisualElement>(className: TargetClass).ForEach(_targets.Add);
            if (_targets.Count == 0)
            {
                Debug.LogError($"[SafeAreaController] No '.{TargetClass}' elements found in the UIDocument; safe-area insets will not be applied.", this);
                _initializing = false;
                _initializationCoroutine = null;
                yield break;
            }

            _root = root;
            _onGeometry = OnGeometryChanged;
            _root.RegisterCallback(_onGeometry);
            _poll = _root.schedule.Execute(PollSafeArea).Every(PollIntervalMs);

            _initTime = Time.realtimeSinceStartup;
            _geometryErrorLogged = false;
            _registered = true;
            _initializing = false;
            _initializationCoroutine = null;
            // First calculation is deferred to the initial GeometryChangedEvent, when
            // panel geometry is valid.
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateSafeArea();
        }

        private void PollSafeArea()
        {
            UpdateSafeArea();
        }

        private void UpdateSafeArea()
        {
            if (_root == null)
                return;

            IPanel panel = _root.panel;
            float panelW = _root.resolvedStyle.width;
            float panelH = _root.resolvedStyle.height;
            float screenW = Screen.width;
            float screenH = Screen.height;

            bool geometryValid = panel != null && panelW > 0f && panelH > 0f && screenW > 0f && screenH > 0f;
            if (!geometryValid)
            {
                MaybeLogGeometryFailure(panel, screenW, screenH, panelW, panelH);
                return;
            }

            Rect safeArea = Screen.safeArea;

            // Change-gate on all five inputs (a panel resize alone must still trigger).
            if (_hasApplied
                && safeArea == _lastSafeArea
                && screenW == _lastScreenW
                && screenH == _lastScreenH
                && panelW == _lastPanelW
                && panelH == _lastPanelH)
            {
                return;
            }

            Insets insets = SafeAreaInsets.Compute(
                safeArea,
                new Vector2(screenW, screenH),
                new Vector2(panelW, panelH),
                p => RuntimePanelUtils.ScreenToPanel(panel, p));

            if (!insets.Valid)
            {
                MaybeLogGeometryFailure(panel, screenW, screenH, panelW, panelH);
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                IStyle style = _targets[i].style;
                style.paddingLeft = new Length(insets.Left, LengthUnit.Pixel);
                style.paddingTop = new Length(insets.Top, LengthUnit.Pixel);
                style.paddingRight = new Length(insets.Right, LengthUnit.Pixel);
                style.paddingBottom = new Length(insets.Bottom, LengthUnit.Pixel);
            }

            _lastSafeArea = safeArea;
            _lastScreenW = screenW;
            _lastScreenH = screenH;
            _lastPanelW = panelW;
            _lastPanelH = panelH;
            _hasApplied = true;
            _geometryErrorLogged = false;
        }

        private void MaybeLogGeometryFailure(IPanel panel, float screenW, float screenH, float panelW, float panelH)
        {
            if (_geometryErrorLogged)
                return;
            if (Time.realtimeSinceStartup - _initTime < GeometryGraceSeconds)
                return; // Still within the startup grace window; retry quietly.

            Debug.LogError(
                $"[SafeAreaController] Panel geometry still invalid after {GeometryGraceSeconds:0.#}s; " +
                $"safe-area insets not applied. screen=({screenW}x{screenH}) panel=({panelW}x{panelH}) " +
                $"panelAvailable={panel != null}. Continuing to retry quietly.", this);
            _geometryErrorLogged = true;
        }
    }
}
