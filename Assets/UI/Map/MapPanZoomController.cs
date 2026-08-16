using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives drag-to-pan and zoom on a Map screen's pannable "*-canvas" layer
    /// (the map artwork + its marker buttons, which move and scale together
    /// since they're all children of the same transformed element). One
    /// instance is configured per map screen (Japan world map, Okinawa chapter
    /// map) via <see cref="screenId"/>/<see cref="viewportElementName"/>/
    /// <see cref="canvasElementName"/>, so the "UI" GameObject carries two
    /// sibling instances. Touch-first: a single pointer (mouse or one finger)
    /// pans via UI Toolkit's pointer events, which work uniformly for mouse and
    /// touch. Zoom has two independent sources — a mouse wheel (desktop/Editor)
    /// and two-finger pinch (device), read directly off <see cref="Touchscreen"/>
    /// since this project's active input handler is the Input System package,
    /// not the legacy UnityEngine.Input class. Deliberately simple (see
    /// <see cref="MapPanZoomMath"/>): zoom always pivots around the canvas's own
    /// center, and a short drag threshold distinguishes a tap on a marker from
    /// an intentional pan, so pinch/drag never swallows a marker's click. On
    /// every fresh entry to its map screen, pan/zoom resets to centered at
    /// MinZoom and plays a short opening zoom animation in to DefaultZoom (see
    /// ResetTransform/PlayIntroZoomAnimation) — real input cancels it instantly.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapPanZoomController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const float DragThresholdPixels = 8f;

        /// <summary>
        /// Zoom change applied per wheel event, as a fixed step rather than a
        /// multiplier on the event's raw delta. UI Toolkit's WheelEvent.delta
        /// magnitude for "one notch" varies a lot by mouse/OS/trackpad — scaling
        /// off it directly makes sensitivity unpredictable across machines. Using
        /// only the delta's sign and a fixed step makes two ordinary wheel
        /// notches reliably move from MinZoom to roughly DefaultZoom
        /// (2 * WheelZoomStep ~= DefaultZoom - MinZoom), on any device.
        /// </summary>
        private const float WheelZoomStep = 0.2f;

        /// <summary>How long the opening zoom (MinZoom -> DefaultZoom) takes on a fresh entry to this map screen.</summary>
        private const float IntroZoomDurationSeconds = 0.4f;

        [Tooltip("The ScreenManager screen id this instance belongs to (e.g. 'map' or 'mapOkinawa'). Pan/zoom resets whenever this screen becomes active.")]
        [SerializeField] private string screenId = "map";

        [Tooltip("Name of the overflow:hidden viewport VisualElement that clips the pannable canvas.")]
        [SerializeField] private string viewportElementName = "map-stage";

        [Tooltip("Name of the pannable/zoomable VisualElement whose transform this controller drives.")]
        [SerializeField] private string canvasElementName = "map-canvas";

        private VisualElement _viewport;
        private VisualElement _canvas;
        private IScreenNavigator _navigator;

        private EventCallback<PointerDownEvent> _onPointerDown;
        private EventCallback<PointerMoveEvent> _onPointerMove;
        private EventCallback<PointerUpEvent> _onPointerUp;
        private EventCallback<PointerCaptureOutEvent> _onPointerCaptureOut;
        private EventCallback<WheelEvent> _onWheel;

        private float _panX;
        private float _panY;
        private float _zoom = MapPanZoomMath.DefaultZoom;

        private bool _pointerDown;
        private bool _dragging;
        private int _activePointerId = -1;
        private Vector2 _downPosition;
        private Vector2 _dragStartPan;

        private bool _isPinching;
        private float _pinchStartDistance;
        private float _pinchStartZoom;

        private Coroutine _bindRoutine;
        private Coroutine _introZoomRoutine;
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
            CancelIntroZoomAnimation();

            if (_bound && _viewport != null)
            {
                _viewport.UnregisterCallback(_onPointerDown);
                _viewport.UnregisterCallback(_onPointerMove);
                _viewport.UnregisterCallback(_onPointerUp);
                _viewport.UnregisterCallback(_onPointerCaptureOut);
                _viewport.UnregisterCallback(_onWheel);
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            _viewport = null;
            _canvas = null;
            _pointerDown = false;
            _dragging = false;
            _isPinching = false;
            _activePointerId = -1;
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
                    Debug.LogError("[MapPanZoomController] UIDocument root unavailable; pan/zoom not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;
            _viewport = root.Q<VisualElement>(viewportElementName);
            _canvas = root.Q<VisualElement>(canvasElementName);

            if (_viewport == null || _canvas == null)
            {
                Debug.LogError($"[MapPanZoomController] '{viewportElementName}'/'{canvasElementName}' elements missing; pan/zoom not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _onPointerDown = OnPointerDown;
            _onPointerMove = OnPointerMove;
            _onPointerUp = OnPointerUp;
            _onPointerCaptureOut = _ => EndDrag();
            _onWheel = OnWheel;

            _viewport.RegisterCallback(_onPointerDown);
            _viewport.RegisterCallback(_onPointerMove);
            _viewport.RegisterCallback(_onPointerUp);
            _viewport.RegisterCallback(_onPointerCaptureOut);
            _viewport.RegisterCallback(_onWheel);

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            ResetTransform();

            _bound = true;
            _bindRoutine = null;
        }

        private void Update()
        {
            if (!_bound)
                return;

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                _isPinching = false;
                return;
            }

            Vector2 posA = default;
            Vector2 posB = default;
            int pressedCount = 0;
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count && pressedCount < 2; i++)
            {
                if (!touches[i].press.isPressed)
                    continue;
                if (pressedCount == 0)
                    posA = touches[i].position.ReadValue();
                else
                    posB = touches[i].position.ReadValue();
                pressedCount++;
            }

            if (pressedCount < 2)
            {
                _isPinching = false;
                return;
            }

            float currentDistance = Vector2.Distance(posA, posB);
            if (!_isPinching)
            {
                _isPinching = true;
                _pinchStartDistance = currentDistance;
                _pinchStartZoom = _zoom;
                EndDrag(); // a second finger landing mid-drag means this is a pinch, not a pan.
                CancelIntroZoomAnimation(); // the player is taking control — don't fight the opening animation.
                return;
            }

            if (_pinchStartDistance > 0.01f)
            {
                float ratio = currentDistance / _pinchStartDistance;
                SetZoom(_pinchStartZoom * ratio);
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_pointerDown || _isPinching)
                return;

            _pointerDown = true;
            _dragging = false;
            _activePointerId = evt.pointerId;
            _downPosition = evt.position;
            _dragStartPan = new Vector2(_panX, _panY);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_pointerDown || evt.pointerId != _activePointerId || _isPinching)
                return;

            Vector2 current = evt.position;
            Vector2 delta = current - _downPosition;

            if (!_dragging)
            {
                if (delta.sqrMagnitude < DragThresholdPixels * DragThresholdPixels)
                    return;
                _dragging = true;
                _viewport.CapturePointer(_activePointerId);
                CancelIntroZoomAnimation(); // the player is taking control — don't fight the opening animation.
            }

            SetPan(_dragStartPan.x + delta.x, _dragStartPan.y + delta.y);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _activePointerId)
                return;
            EndDrag();
        }

        private void OnWheel(WheelEvent evt)
        {
            CancelIntroZoomAnimation(); // the player is taking control — don't fight the opening animation.

            // Only the direction of one wheel event is used, not its raw
            // magnitude (see WheelZoomStep) — this is what keeps the step
            // predictable across mice/trackpads/OS scroll settings.
            if (!Mathf.Approximately(evt.delta.y, 0f))
            {
                float direction = evt.delta.y < 0f ? 1f : -1f;
                SetZoom(_zoom + direction * WheelZoomStep);
            }

            evt.StopPropagation();
        }

        private void EndDrag()
        {
            if (_dragging && _viewport != null && _viewport.HasPointerCapture(_activePointerId))
                _viewport.ReleasePointer(_activePointerId);

            _pointerDown = false;
            _dragging = false;
            _activePointerId = -1;
        }

        private void OnScreenChanged(string changedScreenId)
        {
            if (changedScreenId == screenId)
                ResetTransform();
        }

        /// <summary>
        /// Snaps to the legal fully-zoomed-out cover scale, centered, then
        /// smoothly animates in to the intended default zoom (see
        /// PlayIntroZoomAnimation) — only called on a fresh entry/reset of this
        /// map screen (BindWhenReady, OnScreenChanged), never from in-screen
        /// interactions like marker/popup selection or Settings.
        /// </summary>
        private void ResetTransform()
        {
            CancelIntroZoomAnimation();
            _zoom = MapPanZoomMath.MinZoom;
            SetPan(0f, 0f);
            ApplyZoom();
            _introZoomRoutine = StartCoroutine(PlayIntroZoomAnimation());
        }

        /// <summary>
        /// Short, decelerating zoom-in from MinZoom to DefaultZoom, pivoting
        /// around the canvas's own center (pan stays at its reset 0,0 the whole
        /// time) — "world map appears, glides inward, settles". Any real input
        /// (drag past threshold, wheel, pinch start) cancels this immediately
        /// via CancelIntroZoomAnimation and is never queued afterward.
        /// </summary>
        private IEnumerator PlayIntroZoomAnimation()
        {
            float startZoom = MapPanZoomMath.MinZoom;
            float targetZoom = MapPanZoomMath.DefaultZoom;
            float elapsed = 0f;

            while (elapsed < IntroZoomDurationSeconds)
            {
                elapsed += Time.deltaTime;
                float t = MapPanZoomMath.EaseOutCubic(elapsed / IntroZoomDurationSeconds);
                SetZoom(Mathf.LerpUnclamped(startZoom, targetZoom, t));
                yield return null;
            }

            SetZoom(targetZoom);
            _introZoomRoutine = null;
        }

        private void CancelIntroZoomAnimation()
        {
            if (_introZoomRoutine == null)
                return;
            StopCoroutine(_introZoomRoutine);
            _introZoomRoutine = null;
        }

        private void SetPan(float x, float y)
        {
            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;

            _panX = MapPanZoomMath.ClampPan(x, _zoom, viewportWidth);
            _panY = MapPanZoomMath.ClampPan(y, _zoom, viewportHeight);

            if (_canvas != null)
                _canvas.transform.position = new Vector3(_panX, _panY, 0f);
        }

        private void SetZoom(float zoom)
        {
            _zoom = MapPanZoomMath.ClampZoom(zoom);
            ApplyZoom();
            // The allowed pan range shrinks/grows with zoom (see
            // MapPanZoomMath.MaxPanForZoom) — re-clamp the existing pan against
            // the new zoom so zooming out never leaves a stale, now-too-large
            // pan offset exposing background at the canvas's edge.
            SetPan(_panX, _panY);
        }

        private void ApplyZoom()
        {
            if (_canvas != null)
                _canvas.transform.scale = new Vector3(_zoom, _zoom, 1f);
        }
    }
}
