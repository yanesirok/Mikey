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

        /// <summary>Only the Japan world map screen has an opening "focus on the player's current chapter" camera — see PlayIntroZoomAnimation. Other map screens (Okinawa's own) keep the existing centered opening view.</summary>
        private const string JapanWorldScreenId = "map";

        /// <summary>Where the current chapter marker lands vertically when the Japan world map opens focused on it — slightly below dead center (0.5) so the marker is never crowded under the topbar at the top of the screen. Horizontal framing stays dead center (0.5); the topbar doesn't constrain that axis.</summary>
        private const float ChapterFocusTargetNormalizedY = 0.56f;

        // ---------- double tap: elastic zoom overshoot (see PlayDoubleTapZoom) ----------

        /// <summary>Max gap between the two taps of a double tap. Longer than this, the second tap starts a fresh potential pair instead.</summary>
        private const float DoubleTapMaxSeconds = 0.3f;

        /// <summary>Max screen-space drift between the two taps of a double tap — keeps an accidental two-finger-ish double touch from being read as one.</summary>
        private const float DoubleTapMaxDistancePixels = 40f;

        /// <summary>How much closer a double tap zooms in, as a multiplier on the CURRENT zoom (not a fixed target) — so repeated double taps keep zooming further, up to MapPanZoomMath.MaxZoom.</summary>
        private const float DoubleTapZoomFactor = 1.6f;

        /// <summary>How long the double-tap zoom's elastic overshoot-and-settle takes (see MapPanZoomMath.EaseOutBack).</summary>
        private const float DoubleTapDurationSeconds = 0.34f;

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
        private float _ambientPanX;
        private float _ambientPanY;
        private float _ambientZoomMultiplier = 1f;
        private float _lastInputTime;

        private bool _pointerDown;
        private bool _dragging;
        private int _activePointerId = -1;
        private Vector2 _downPosition;
        private Vector2 _dragStartPan;

        private bool _isPinching;
        private float _pinchStartDistance;
        private float _pinchStartZoom;

        private float _velocityX;
        private float _velocityY;
        private Vector2 _lastMovePosition;
        private float _lastMoveTime;
        private bool _inertiaActive;

        private float _rubberBandX;
        private float _rubberBandY;
        private Coroutine _rubberBandRoutine;

        private Coroutine _bindRoutine;
        private Coroutine _introZoomRoutine;
        private bool _bound;

        private float _lastTapTime = -1f;
        private Vector2 _lastTapPosition;
        private Coroutine _doubleTapRoutine;

        /// <summary>This instance's configured screen id — exposed read-only so MapCloudTransitionController can tell apart the Japan ("map") and Okinawa ("mapOkinawa") sibling instances when transferring view state across a chapter transition.</summary>
        public string ScreenId => screenId;

        /// <summary>Current zoom level, for capturing view state before a Japan&lt;-&gt;Okinawa cloud transition (see MapCloudTransitionController).</summary>
        public float CurrentZoom => _zoom;

        /// <summary>Текущий горизонтальный пан в пикселях — читается ambient-слоем для параллакса облаков (см. MapAmbientController).</summary>
        public float CurrentPanX => _panX;

        /// <summary>Текущий вертикальный пан в пикселях — см. <see cref="CurrentPanX"/>.</summary>
        public float CurrentPanY => _panY;

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
            StopRubberBand();

            if (_doubleTapRoutine != null)
            {
                StopCoroutine(_doubleTapRoutine);
                _doubleTapRoutine = null;
            }

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

            // Канвас — единственный элемент карты, которому трансформация
            // пишется КАЖДЫЙ кадр (пан, инерция, резинка, двойной тап, вводный
            // зум, дыхание бумаги, Ken Burns). Без этой подсказки движок
            // запекает трансформацию в вершины, то есть пересобирает геометрию
            // всего поддерева канваса — арт, скрим, слой облаков и каждую
            // кнопку-маркер с её подписью — на каждом таком кадре. Весь
            // расчёт стоимости в дизайне анимаций карты условен на ней.
            _canvas.usageHints = UsageHints.DynamicTransform;

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
            if (!_bound || MapCloudTransitionController.IsTransitioning)
                return;

            // Инерция — доезд уже отпущенного жеста, а не новый ввод: не
            // зовём MarkInput() отсюда, иначе часы простоя (SecondsSinceLastInput)
            // считали бы от остановки карты, а не от отпускания пальца.
            if (_inertiaActive && !_pointerDown && !_isPinching)
            {
                float inertiaDt = Time.unscaledDeltaTime;
                SetPan(_panX + _velocityX * inertiaDt, _panY + _velocityY * inertiaDt);
                _velocityX = MapAmbientMath.DecayVelocity(_velocityX, inertiaDt);
                _velocityY = MapAmbientMath.DecayVelocity(_velocityY, inertiaDt);
                if (MapAmbientMath.IsInertiaFinished(_velocityX, _velocityY))
                    StopInertia();
            }

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
                MarkInput();
                _pinchStartDistance = currentDistance;
                _pinchStartZoom = _zoom;
                EndDrag(); // a second finger landing mid-drag means this is a pinch, not a pan.
                // EndDrag() above may have just armed inertia from the drag
                // that preceded this second finger — a pinch start is itself
                // a fresh gesture, so that stale, undecayed velocity must not
                // survive to resume the pan once the pinch ends.
                StopInertia();
                StopRubberBand();
                CancelIntroZoomAnimation(); // the player is taking control — don't fight the opening animation.
                return;
            }

            if (_pinchStartDistance > 0.01f)
            {
                float ratio = currentDistance / _pinchStartDistance;
                MarkInput();
                SetZoom(_pinchStartZoom * ratio);
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            MarkInput();
            if (_pointerDown || _isPinching || MapCloudTransitionController.IsTransitioning)
                return;

            _pointerDown = true;
            _dragging = false;
            _activePointerId = evt.pointerId;
            _downPosition = evt.position;
            _dragStartPan = new Vector2(_panX, _panY);

            // Новое касание мгновенно перехватывает управление и гасит доезд.
            StopInertia();
            StopRubberBand();
            _lastMovePosition = evt.position;
            _lastMoveTime = Time.unscaledTime;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_pointerDown || evt.pointerId != _activePointerId || _isPinching)
                return;

            // Безусловно на каждом кадре перетаскивания, а не только при
            // пересечении порога — иначе долгий пан пересёк бы порог
            // простоя прямо под пальцем.
            MarkInput();

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

            // За границей карта продолжает идти за пальцем, но с
            // сопротивлением (см. SetPanWithRubberBand) — оттяжка не
            // трогает _panX/_panY, поэтому клампы ниже остаются законными.
            SetPanWithRubberBand(_dragStartPan.x + delta.x, _dragStartPan.y + delta.y);

            // Копим скорость пальца для инерции после отпускания — сглажена
            // через BlendVelocity, чтобы одиночный дёрганый замер не стал
            // целиком скоростью броска.
            float now = Time.unscaledTime;
            float dt = now - _lastMoveTime;
            if (dt > 0.001f && dt < 0.25f)
            {
                Vector2 step = current - _lastMovePosition;
                _velocityX = MapAmbientMath.BlendVelocity(_velocityX, step.x / dt);
                _velocityY = MapAmbientMath.BlendVelocity(_velocityY, step.y / dt);
            }
            _lastMovePosition = current;
            _lastMoveTime = now;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _activePointerId)
                return;

            // Двойной тап распознаём только по фону канваса, а не по
            // маркерам: указательные события всплывают от Button к
            // _viewport, так что без проверки evt.target быстрый выбор
            // двух соседних маркеров (или повторный тап по одному и тому
            // же) читался бы как жест "зум по двойному тапу". Маркеры —
            // единственные интерактивные Button внутри вьюпорта (см.
            // JapanMapController/OkinawaMapController), поэтому evt.target
            // — дешёвый и точный признак "это тап по фону, не по кнопке".
            //
            // Тап по кнопке при этом обязан ОБНУЛИТЬ зависшее ожидание, а
            // не просто быть пропущенным: иначе фон -> маркер -> фон за
            // DoubleTapMaxSeconds/DoubleTapMaxDistancePixels спарит третий
            // тап с первым (тап по маркеру для автомата невидим), и наезд
            // запустится ровно там, где игрок открыл панель, а не там, где
            // просил зум.
            if (evt.target is Button)
            {
                _lastTapTime = -1f;
            }
            else if (!_dragging)
            {
                Vector2 position = evt.position;
                bool isDoubleTap = _lastTapTime > 0f
                    && Time.unscaledTime - _lastTapTime <= DoubleTapMaxSeconds
                    && Vector2.Distance(position, _lastTapPosition) <= DoubleTapMaxDistancePixels;

                if (isDoubleTap)
                {
                    _lastTapTime = -1f;
                    if (_doubleTapRoutine != null)
                        StopCoroutine(_doubleTapRoutine);
                    _doubleTapRoutine = StartCoroutine(PlayDoubleTapZoom());
                }
                else
                {
                    _lastTapTime = Time.unscaledTime;
                    _lastTapPosition = position;
                }
            }

            EndDrag();
        }

        private void OnWheel(WheelEvent evt)
        {
            MarkInput();
            if (MapCloudTransitionController.IsTransitioning)
            {
                evt.StopPropagation();
                return;
            }

            CancelIntroZoomAnimation(); // the player is taking control — don't fight the opening animation.
            StopInertia(); // a wheel zoom is real input too — it must break any in-flight coast.
            StopRubberBand();

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
            if (_dragging && !MapAmbientMath.IsInertiaFinished(_velocityX, _velocityY))
                _inertiaActive = true;

            if (_dragging && _viewport != null && _viewport.HasPointerCapture(_activePointerId))
                _viewport.ReleasePointer(_activePointerId);

            _pointerDown = false;
            _dragging = false;
            _activePointerId = -1;

            // Отпустили палец, а карта всё ещё оттянута за границу — отдать
            // резинку обратно. Живёт отдельно от инерции: инерция (см.
            // Update()) продолжает уже законно заклампленный пан, резинка
            // лишь стирает свою собственную визуальную добавку к нему.
            if (_rubberBandX != 0f || _rubberBandY != 0f)
            {
                if (_rubberBandRoutine != null)
                    StopCoroutine(_rubberBandRoutine);
                _rubberBandRoutine = StartCoroutine(ReleaseRubberBand());
            }
        }

        /// <summary>
        /// Гасит инерцию НАСМЕРТЬ. Вызывается отовсюду, где камера ставится
        /// заново дискретно, а не движется пальцем: новое касание, старт пинча,
        /// сброс при входе на экран и перенос вида при межэкранном переходе.
        ///
        /// <para>
        /// Проверки в Update() инерцию только ПРИОСТАНАВЛИВАЮТ, а не гасят.
        /// Поэтому бросок карты прямо перед переходом оставил бы скорость
        /// взведённой, и на первом же кадре после перехода камера поехала бы
        /// поверх свежепоставленной рамки — движение, которого игрок не просил.
        /// </para>
        /// </summary>
        private void StopInertia()
        {
            _velocityX = 0f;
            _velocityY = 0f;
            _inertiaActive = false;
        }

        /// <summary>
        /// Обрывает возврат резинки насмерть и обнуляет оттяжку — тот же
        /// принцип, что и <see cref="StopInertia"/>, для того же набора мест:
        /// если камера ставится заново дискретно, пока возврат ещё не
        /// доехал, недоехавшая оттяжка не должна тащиться поверх новой
        /// рамки (свежего жеста, зума колесом, входа на экран или переноса
        /// вида между экранами).
        /// </summary>
        private void StopRubberBand()
        {
            if (_rubberBandRoutine != null)
            {
                StopCoroutine(_rubberBandRoutine);
                _rubberBandRoutine = null;
            }
            _rubberBandX = 0f;
            _rubberBandY = 0f;
        }

        /// <summary>How long the rubber band takes to ease back to zero once the finger releases at (or past) the border.</summary>
        private const float RubberBandReleaseSeconds = 0.28f;

        /// <summary>
        /// Eases the rubber band offset back to zero after EndDrag() arms it
        /// — the finger let go while the map was pulled past its edge.
        /// Started/stopped only via EndDrag()/StopRubberBand(), never
        /// StartCoroutine'd elsewhere.
        /// </summary>
        private IEnumerator ReleaseRubberBand()
        {
            float startX = _rubberBandX;
            float startY = _rubberBandY;
            float elapsed = 0f;

            while (elapsed < RubberBandReleaseSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = MapPanZoomMath.EaseOutCubic(elapsed / RubberBandReleaseSeconds);
                _rubberBandX = Mathf.LerpUnclamped(startX, 0f, t);
                _rubberBandY = Mathf.LerpUnclamped(startY, 0f, t);
                ApplyCanvasTransform();
                yield return null;
            }

            // Естественное завершение маршрутизируется через тот же
            // помощник, что и досрочный обрыв — StopCoroutine на
            // собственном, ещё не расчищенном дескрипторе здесь безопасный
            // no-op (корутина и так последней строкой сама себя завершает).
            StopRubberBand();
            ApplyCanvasTransform();
        }

        private void OnScreenChanged(string changedScreenId)
        {
            if (changedScreenId != screenId)
                return;

            // A Japan<->Okinawa cloud transition already positions this
            // screen's view itself (see SetViewToSourceFocalPoint, called by
            // MapCloudTransitionController while fully hidden under cloud
            // cover) — the normal "fresh entry" reset must not fight that
            // transferred, spatially-continuous view. IsTransitioning stays
            // true for the whole close-hold-reveal sequence, which safely
            // spans the Show() call that fires this event.
            if (MapCloudTransitionController.IsTransitioning)
                return;

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
            StopInertia();
            StopRubberBand();
            MarkInput();
            _zoom = MapPanZoomMath.MinZoom;
            SetPan(0f, 0f);
            ApplyZoom();
            _introZoomRoutine = StartCoroutine(PlayIntroZoomAnimation());
        }

        /// <summary>
        /// Short, decelerating zoom-in from MinZoom to DefaultZoom, pivoting
        /// around the canvas's own center. On the Japan world map only
        /// (screenId == "map"), this also pans toward the player's current
        /// chapter (see MapMarkerLayout.TryGetCurrentChapterFocalPoint) as
        /// zoom increases, so the reveal reads as "world map appears, glides
        /// toward where the player's story currently is, settles" rather
        /// than a generic center zoom — the chapter marker lands at a
        /// comfortable off-center composition (see
        /// ChapterFocusTargetNormalizedY), never under the topbar. Every
        /// other screen (Okinawa's own) keeps the original plain-center
        /// behavior: pan stays at its reset 0,0 the whole time. Any real
        /// input (drag past threshold, wheel, pinch start) cancels this
        /// immediately via CancelIntroZoomAnimation and is never queued
        /// afterward.
        /// </summary>
        private IEnumerator PlayIntroZoomAnimation()
        {
            float startZoom = MapPanZoomMath.MinZoom;
            float targetZoom = MapPanZoomMath.DefaultZoom;
            float elapsed = 0f;

            float focusSourceX = 0f, focusSourceY = 0f;
            bool hasChapterFocus = screenId == JapanWorldScreenId
                && MapMarkerLayout.TryGetCurrentChapterFocalPoint(out focusSourceX, out focusSourceY);

            while (elapsed < IntroZoomDurationSeconds)
            {
                elapsed += Time.deltaTime;
                float t = MapPanZoomMath.EaseOutCubic(elapsed / IntroZoomDurationSeconds);
                if (hasChapterFocus)
                    ApplyZoomTowardChapterFocus(Mathf.LerpUnclamped(startZoom, targetZoom, t), focusSourceX, focusSourceY);
                else
                    SetZoom(Mathf.LerpUnclamped(startZoom, targetZoom, t));
                yield return null;
            }

            if (hasChapterFocus)
                ApplyZoomTowardChapterFocus(targetZoom, focusSourceX, focusSourceY);
            else
                SetZoom(targetZoom);
            _introZoomRoutine = null;
        }

        /// <summary>
        /// Sets zoom and pans so the given SOURCE-image-normalized point
        /// (the current chapter's marker — never a stored marker coordinate
        /// mutation, purely a display-time camera target) lands at
        /// <see cref="ChapterFocusTargetNormalizedY"/> vertically and dead
        /// center horizontally, at the given zoom. Called every frame of the
        /// opening animation so the chapter marker stays pinned at that
        /// framing throughout the zoom-in, not just at the end.
        /// </summary>
        private void ApplyZoomTowardChapterFocus(float zoom, float focusSourceX, float focusSourceY)
        {
            _zoom = MapPanZoomMath.ClampZoom(zoom);
            ApplyZoom();

            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;
            if (viewportWidth <= 0f || viewportHeight <= 0f)
            {
                SetPan(_panX, _panY);
                return;
            }

            MapCoordinateMapping.SourceToViewport(
                focusSourceX, focusSourceY,
                MapMarkerLayout.SourceImageWidth, MapMarkerLayout.SourceImageHeight,
                viewportWidth, viewportHeight,
                out float canvasNormalizedX, out float canvasNormalizedY);

            float panX = MapPanZoomMath.PanForTarget(canvasNormalizedX, 0.5f, _zoom, viewportWidth);
            float panY = MapPanZoomMath.PanForTarget(canvasNormalizedY, ChapterFocusTargetNormalizedY, _zoom, viewportHeight);
            SetPan(panX, panY);
        }

        /// <summary>
        /// The source-image-normalized point currently sitting at the
        /// viewport's exact center, derived by inverting the pan/zoom
        /// transform and the cover-fit crop (see
        /// MapCoordinateMapping.ViewportToSource) — used only by
        /// MapCloudTransitionController to capture "what is the player
        /// currently looking at" before a Japan&lt;-&gt;Okinawa chapter
        /// transition, so the destination map can reconstruct the same
        /// framing (see <see cref="SetViewToSourceFocalPoint"/>). False
        /// while not yet bound/laid out.
        /// </summary>
        public bool TryGetCurrentSourceFocalPoint(out float sourceNormalizedX, out float sourceNormalizedY)
        {
            sourceNormalizedX = 0f;
            sourceNormalizedY = 0f;

            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;
            if (!_bound || viewportWidth <= 0f || viewportHeight <= 0f)
                return false;

            float canvasNormalizedX = MapPanZoomMath.CanvasNormalizedAtViewportCenter(_panX, _zoom, viewportWidth);
            float canvasNormalizedY = MapPanZoomMath.CanvasNormalizedAtViewportCenter(_panY, _zoom, viewportHeight);

            MapCoordinateMapping.ViewportToSource(
                canvasNormalizedX, canvasNormalizedY,
                MapMarkerLayout.SourceImageWidth, MapMarkerLayout.SourceImageHeight,
                viewportWidth, viewportHeight,
                out sourceNormalizedX, out sourceNormalizedY);
            return true;
        }

        /// <summary>
        /// Immediately (no animation) positions this canvas so the given
        /// source-image-normalized point sits at the viewport's exact
        /// center, at the given zoom — used only by
        /// MapCloudTransitionController to reconstruct spatial continuity on
        /// the destination screen WHILE FULLY HIDDEN under cloud cover,
        /// mirroring how it snaps the destination's clouds to their own
        /// closed layout before the swap. Bypasses the normal opening-zoom
        /// animation and any chapter-focus default entirely — the
        /// transferred view always wins during a cloud transition (see
        /// OnScreenChanged's IsTransitioning guard).
        /// </summary>
        public void SetViewToSourceFocalPoint(float sourceNormalizedX, float sourceNormalizedY, float zoom)
        {
            if (_viewport == null || _canvas == null)
                return;

            CancelIntroZoomAnimation();
            StopInertia();
            StopRubberBand();
            // Переход сам ТОЛЬКО ЧТО выставил кадр этой камеры, и уходить с
            // него Ken Burns обязан не раньше положенных IdleDelaySeconds
            // простоя. Без этого часы простоя продолжали бы идти с последнего
            // касания на ИСХОДНОМ экране (OnScreenChanged на переходе
            // пропускает ResetTransform — единственный другой MarkInput на
            // этом пути), и камера начинала бы плыть на первом же видимом
            // кадре нового экрана.
            MarkInput();

            _zoom = MapPanZoomMath.ClampZoom(zoom);
            ApplyZoom();

            float viewportWidth = _viewport.resolvedStyle.width;
            float viewportHeight = _viewport.resolvedStyle.height;
            if (viewportWidth <= 0f || viewportHeight <= 0f)
            {
                SetPan(0f, 0f);
                return;
            }

            MapCoordinateMapping.SourceToViewport(
                sourceNormalizedX, sourceNormalizedY,
                MapMarkerLayout.SourceImageWidth, MapMarkerLayout.SourceImageHeight,
                viewportWidth, viewportHeight,
                out float canvasNormalizedX, out float canvasNormalizedY);

            float panX = MapPanZoomMath.PanForTarget(canvasNormalizedX, 0.5f, _zoom, viewportWidth);
            float panY = MapPanZoomMath.PanForTarget(canvasNormalizedY, 0.5f, _zoom, viewportHeight);
            SetPan(panX, panY);
        }

        /// <summary>
        /// Smoothly animates this canvas's pan/zoom from its CURRENT view
        /// toward the given source-image-normalized focal point and zoom,
        /// over <paramref name="durationSeconds"/>, eased via
        /// MapPanZoomMath.EaseInOutCubic — the Japan&lt;-&gt;Okinawa chapter
        /// transition's approach/settle camera motion (see
        /// MapCloudTransitionController.PlayJapanToOkinawa/PlayOkinawaToJapan),
        /// never the plain per-screen opening animation (PlayIntroZoomAnimation
        /// is a separate animation that always starts from a dead stop at
        /// MinZoom). Both pan AND zoom are interpolated directly from their
        /// CURRENT values to the values needed to land the given focal point
        /// at the viewport's exact center at the target zoom — computed once
        /// up front, not re-targeted every frame — so this is a genuine
        /// smooth move from wherever the camera already is, not a snap. The
        /// caller owns the coroutine via <c>yield return</c> (this method
        /// only returns the enumerator, never calls StartCoroutine itself),
        /// so MapCloudTransitionController can drive both screens' cameras
        /// from its own single coordinating coroutine without either screen
        /// owning a duplicate camera system.
        /// </summary>
        public IEnumerator AnimateViewToSourceFocalPoint(float targetSourceX, float targetSourceY, float targetZoom, float durationSeconds)
        {
            CancelIntroZoomAnimation();
            StopInertia();
            // NB: no StopRubberBand() here — this coroutine only ever writes
            // _panX/_panY, never _rubberBandX/_rubberBandY, so there's no
            // field to race with. Cutting the rubber band short here would
            // instead SNAP the still-visible source screen (this approach
            // phase runs before the screen swap) by up to 12% of the
            // viewport in one frame. Left alone, it just eases to zero on
            // its own schedule while the pan animates — no conflict.
            // (SetViewToSourceFocalPoint, by contrast, DOES call
            // StopRubberBand() — it plants the destination screen's view
            // while still hidden under cloud cover, so the same snap is
            // invisible there.)

            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;

            float startPanX = _panX;
            float startPanY = _panY;
            float startZoom = _zoom;
            float clampedTargetZoom = MapPanZoomMath.ClampZoom(targetZoom);

            float targetPanX = startPanX;
            float targetPanY = startPanY;
            if (viewportWidth > 0f && viewportHeight > 0f)
            {
                MapCoordinateMapping.SourceToViewport(
                    targetSourceX, targetSourceY,
                    MapMarkerLayout.SourceImageWidth, MapMarkerLayout.SourceImageHeight,
                    viewportWidth, viewportHeight,
                    out float canvasNormalizedX, out float canvasNormalizedY);
                targetPanX = MapPanZoomMath.PanForTarget(canvasNormalizedX, 0.5f, clampedTargetZoom, viewportWidth);
                targetPanY = MapPanZoomMath.PanForTarget(canvasNormalizedY, 0.5f, clampedTargetZoom, viewportHeight);
            }

            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                float t = MapPanZoomMath.EaseInOutCubic(elapsed / durationSeconds);
                _zoom = MapPanZoomMath.ClampZoom(Mathf.LerpUnclamped(startZoom, clampedTargetZoom, t));
                ApplyZoom();
                SetPan(Mathf.LerpUnclamped(startPanX, targetPanX, t), Mathf.LerpUnclamped(startPanY, targetPanY, t));
                // Каждый кадр, а не один раз на старте: это движение идёт во
                // времени, и отметка только в начале дала бы неполную льготу —
                // к моменту, когда камера ВСТАЛА, простоя уже насчиталось бы
                // durationSeconds. Тот же довод, по которому MarkInput стоит на
                // продолжении пана и пинча, а не только на их начале.
                MarkInput();
                yield return null;
            }

            _zoom = clampedTargetZoom;
            ApplyZoom();
            SetPan(targetPanX, targetPanY);
            MarkInput();
        }

        private void CancelIntroZoomAnimation()
        {
            if (_introZoomRoutine == null)
                return;
            StopCoroutine(_introZoomRoutine);
            _introZoomRoutine = null;
        }

        /// <summary>
        /// Зум по двойному тапу: доезжает чуть дальше цели и возвращается
        /// (см. MapPanZoomMath.EaseOutBack). Пивот — центр канваса, тот же,
        /// что у колеса и пинча: зум «в точку тапа» здесь не нужен, карта и
        /// так центрируется на интересном объекте паном.
        ///
        /// <para>
        /// Не гасит инерцию/оттяжку сама — это уже сделано OnPointerDown()
        /// для КАЖДОГО касания (см. её StopInertia()/StopRubberBand()),
        /// включая оба тапа этого жеста, ещё до того как OnPointerUp вообще
        /// распознаёт двойной тап: к моменту запуска этой корутины
        /// _velocityX/_velocityY и _rubberBandX/_rubberBandY уже нулевые.
        /// Повторный вызов здесь был бы мёртвым кодом. CancelIntroZoomAnimation()
        /// — другое дело: одиночный тап НЕ отменяет вводный зум (тот
        /// прерывается только реальным жестом — драгом, колесом, пинчем), так
        /// что без явной отмены здесь наезд дрался бы за _zoom с ещё идущей
        /// PlayIntroZoomAnimation.
        /// </para>
        /// </summary>
        private IEnumerator PlayDoubleTapZoom()
        {
            CancelIntroZoomAnimation();
            MarkInput();

            float startZoom = _zoom;
            float targetZoom = MapPanZoomMath.ClampZoom(startZoom * DoubleTapZoomFactor);
            if (Mathf.Approximately(startZoom, targetZoom))
            {
                // Уже на MaxZoom (или ClampZoom иначе не даёт сдвинуться) —
                // сыграть тут нечего, а лерп между двумя равными значениями
                // всё равно дёрнул бы картинку через EaseOutBack.
                _doubleTapRoutine = null;
                yield break;
            }

            // У потолка зума пружине не во что упираться: EaseOutBack всё
            // равно проскакивает цель примерно на 10%, а SetZoom тут же
            // срезает превышение клампом — вместо упругой отдачи читался бы
            // удар в стену. Именно этот случай самый частый на практике
            // (второй двойной тап подряд), так что у потолка доводим без
            // перелёта.
            bool targetIsAtCeiling = Mathf.Approximately(targetZoom, MapPanZoomMath.MaxZoom);

            float elapsed = 0f;
            while (elapsed < DoubleTapDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = elapsed / DoubleTapDurationSeconds;
                float t = targetIsAtCeiling
                    ? MapPanZoomMath.EaseOutCubic(progress)
                    : MapPanZoomMath.EaseOutBack(progress);
                SetZoom(Mathf.LerpUnclamped(startZoom, targetZoom, t));
                yield return null;
            }

            SetZoom(targetZoom);
            _doubleTapRoutine = null;
        }

        private void SetPan(float x, float y)
        {
            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;

            _panX = MapPanZoomMath.ClampPan(x, _zoom, viewportWidth);
            _panY = MapPanZoomMath.ClampPan(y, _zoom, viewportHeight);

            ApplyCanvasTransform();
        }

        /// <summary>
        /// Пан во время прямого перетаскивания: за границей карта продолжает
        /// идти, но с сопротивлением. Оттяжка живёт ОТДЕЛЬНО от логического
        /// пана (_panX/_panY остаются законно заклампленными через SetPan),
        /// поэтому ни зум, ни переход между экранами, ни инерция не
        /// наследуют «нелегальную» позицию — см. ApplyCanvasTransform.
        /// </summary>
        private void SetPanWithRubberBand(float x, float y)
        {
            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;

            float maxX = MapPanZoomMath.MaxPanForZoom(_zoom, viewportWidth);
            float maxY = MapPanZoomMath.MaxPanForZoom(_zoom, viewportHeight);

            _rubberBandX = MapPanZoomMath.RubberBand(Overshoot(x, maxX), viewportWidth);
            _rubberBandY = MapPanZoomMath.RubberBand(Overshoot(y, maxY), viewportHeight);

            SetPan(x, y);
        }

        private static float Overshoot(float value, float limit)
        {
            if (value > limit)
                return value - limit;
            if (value < -limit)
                return value + limit;
            return 0f;
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

        private void ApplyZoom() => ApplyCanvasTransform();

        /// <summary>
        /// ЕДИНСТВЕННОЕ место, которое пишет трансформацию канваса. Логический
        /// пан/зум (управление игрока) и ambient-добавка (дыхание бумаги, Ken
        /// Burns) складываются здесь, а не спорят за transform.
        ///
        /// <para>
        /// Сумма клампится ЗДЕСЬ ещё раз, против ФАКТИЧЕСКОГО масштаба. SetPan
        /// клампит только логический пан и только против <c>_zoom</c>, поэтому
        /// на границе карты (а это и есть состояние покоя при первом входе:
        /// вводный зум ведёт камеру к текущей главе и упирается в кламп, если
        /// та у края) любая добавка ambient по определению уже за краем —
        /// «амплитуда мала» там не аргумент, запас ровно нулевой. Клампится
        /// именно сумма, а не <c>_panX</c>: логический пан остаётся
        /// нетронутым, поэтому дыхание не «съедает» позицию камеры понемногу
        /// каждый цикл, а лишь модулирует показанное смещение внутри запаса.
        /// </para>
        ///
        /// <para>
        /// Масштаб не опускается ниже <see cref="MapPanZoomMath.MinZoom"/>:
        /// множитель ambient уходит под единицу (Ken Burns вычитает до 1.5 %),
        /// и на полностью отдалённой карте (<c>_zoom == MinZoom</c>) это
        /// открыло бы полосу фона сразу по всем четырём краям, чего никаким
        /// клампом пана не исправить. Порезан при этом ровно тот случай, где
        /// отдаляться и так запрещено; на любом зуме выше 1.015 волна Ken
        /// Burns проходит целиком.
        /// </para>
        /// </summary>
        private void ApplyCanvasTransform()
        {
            if (_canvas == null)
                return;

            float scale = Mathf.Max(_zoom * _ambientZoomMultiplier, MapPanZoomMath.MinZoom);
            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;

            // Резинка прибавляется ПОСЛЕ клампа намеренно: она и существует
            // затем, чтобы карта заходила за свой край с сопротивлением.
            _canvas.transform.position = new Vector3(
                MapPanZoomMath.ClampPan(_panX + _ambientPanX, scale, viewportWidth) + _rubberBandX,
                MapPanZoomMath.ClampPan(_panY + _ambientPanY, scale, viewportHeight) + _rubberBandY,
                0f);
            _canvas.transform.scale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// Ambient-добавка к камере: смещение в пикселях и множитель зума.
        /// Вызывается MapAmbientController на каждом его тике; логический
        /// пан/зум при этом не меняется, поэтому ни клампы, ни сохранённая
        /// рамка перехода между экранами не съезжают.
        /// </summary>
        public void SetAmbientOffset(float panX, float panY, float zoomMultiplier)
        {
            _ambientPanX = IsFinite(panX) ? panX : 0f;
            _ambientPanY = IsFinite(panY) ? panY : 0f;
            _ambientZoomMultiplier = IsFinite(zoomMultiplier) && zoomMultiplier > 0f ? zoomMultiplier : 1f;
            ApplyCanvasTransform();
        }

        /// <summary>
        /// Отмечает «игрок только что действовал». Вызывается со ВСЕХ путей
        /// реального ввода, включая продолжение уже идущего жеста — а не только
        /// его начало. Иначе непрерывный пан или пинч длиннее IdleDelaySeconds
        /// пересёк бы порог простоя прямо под пальцем, и Ken Burns начал бы
        /// уводить камеру поверх активного жеста игрока.
        /// </summary>
        private void MarkInput() => _lastInputTime = Time.unscaledTime;

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Сколько секунд прошло с последнего действия игрока — ambient включает Ken Burns только в простое.</summary>
        public float SecondsSinceLastInput => Time.unscaledTime - _lastInputTime;
    }
}
