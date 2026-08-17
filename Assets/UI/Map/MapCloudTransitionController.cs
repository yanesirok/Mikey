using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives the Japan world map &lt;-&gt; Okinawa chapter map transition (Map
    /// Pass 3D, cleanup): a smooth CAMERA pan/zoom move, never a cloud
    /// animation. The 4 decorative clouds on each screen are painted
    /// atmospheric parts of the map now — they sit at their fixed
    /// <see cref="MapCloudLayout.JapanRest"/>/<see cref="MapCloudLayout.OkinawaRest"/>
    /// composition at all times and only ever appear to move because they
    /// are children of the SAME transformed ".pan-canvas" the camera
    /// animates (see Map.uss ".map-cloud-layer") — exactly like the map art
    /// and markers. This controller therefore has two separate
    /// responsibilities that happen to share one component rather than
    /// warranting two: (1) reapplying each screen's cloud rest composition
    /// on canvas resize (mirrors JapanMapController/OkinawaMapController's
    /// marker resize reprojection — clouds need the identical treatment
    /// since they go through the same cover-fit reprojection), and (2)
    /// orchestrating the cross-screen camera transition itself, because a
    /// single transition has to coordinate BOTH screens' MapPanZoomController
    /// instances plus the screen swap plus input lock together.
    ///
    /// <para>
    /// <b>Transition shape</b> (see <see cref="PlayJapanToOkinawa"/>/
    /// <see cref="PlayOkinawaToJapan"/>): APPROACH — the source screen's
    /// camera smoothly pans/zooms (see
    /// MapPanZoomController.AnimateViewToSourceFocalPoint,
    /// MapPanZoomMath.EaseInOutCubic) toward a target view. SWAP — the
    /// source screen's now-current view (whatever the approach actually
    /// landed on, read back via TryGetCurrentSourceFocalPoint/CurrentZoom
    /// rather than assumed, so any pan/zoom clamping is respected) is
    /// applied INSTANTLY to the destination screen
    /// (SetViewToSourceFocalPoint) immediately before <c>Show()</c>, so the
    /// destination is already correctly framed the moment it becomes
    /// visible — no crossfade is used (the swap already reads clean without
    /// one: the destination is never shown mid-transition or at a
    /// mismatched framing). SETTLE — the destination screen's camera
    /// continues smoothly toward its own settle target, so nothing ever
    /// snaps. Since Japan and Okinawa share the exact same source image
    /// dimensions (MapMarkerLayout.SourceImageWidth/Height), a captured
    /// source-normalized focal point is directly meaningful on either
    /// screen with no remapping — the two maps read as physically layered.
    /// </para>
    ///
    /// <para>
    /// <b>Japan -&gt; Okinawa</b>: approach dives toward the OKINAWA CHAPTER'S
    /// OWN marker location on the Japan map (not wherever the player
    /// happened to be looking), a "diving into the chapter" cinematic
    /// read; settle continues zooming in further on Okinawa's side ("zoomed
    /// deeper into the same physical location").
    /// </para>
    ///
    /// <para>
    /// <b>Okinawa -&gt; Japan</b>: reverses the feeling — approach captures
    /// wherever the player currently is on Okinawa and zooms OUT (in place,
    /// no pan) toward the same shared pre-swap zoom; settle continues
    /// zooming out further toward Japan's own DefaultZoom, so the player
    /// re-emerges at the corresponding point on the world map, never reset
    /// to its generic center.
    /// </para>
    ///
    /// Input lock: each screen's cloud-layer container's own picking-mode is
    /// the map-content-area input blocker during a transition (Position
    /// while locked, Ignore at rest — mirrors the existing
    /// ".map-outside-catcher" pattern), which blocks marker taps and
    /// outside-tap panel close by z-order/hit-testing alone — this doesn't
    /// depend on the clouds animating, only on the cloud layer already
    /// being the last (topmost) child of the canvas. Pan/wheel-zoom/
    /// pinch-zoom are NOT routed through picking-mode at all (pinch reads
    /// Touchscreen directly, bypassing the visual tree entirely) — see
    /// MapPanZoomController, which checks <see cref="IsTransitioning"/>
    /// itself before acting on any input source, and whose OnScreenChanged
    /// separately skips its own normal "fresh entry" reset while
    /// <see cref="IsTransitioning"/> so it never fights the transferred
    /// view. It does NOT reach the shared topbar (a sibling of
    /// ".pan-stage", outside the transformed layer) or an already-open
    /// detail panel's own CTA, which is why JapanMapController/
    /// OkinawaMapController separately check <see cref="IsTransitioning"/>
    /// before acting on topbar navigation.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapCloudTransitionController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>How long the approach phase (source screen's camera move, before the swap) takes. Within the approved 0.75-0.95s range.</summary>
        private const float ApproachDurationSeconds = 0.85f;

        /// <summary>How long the settle phase (destination screen's camera move, after the swap) takes. Within the approved 0.55-0.75s range.</summary>
        private const float SettleDurationSeconds = 0.65f;

        /// <summary>
        /// Shared pre-swap zoom both directions animate the source screen's
        /// camera toward during the approach phase — a moderate "diving in"
        /// (Japan -&gt; Okinawa) or "pulling back" (Okinawa -&gt; Japan) zoom,
        /// comfortably between MapPanZoomMath.DefaultZoom (1.4) and MaxZoom
        /// (2.5). Never changes MinZoom/DefaultZoom/MaxZoom themselves —
        /// purely a temporary transition target.
        /// </summary>
        private const float TransitionApproachZoom = 1.6f;

        /// <summary>Okinawa's post-swap settle target zoom (Japan -&gt; Okinawa only) — higher than <see cref="TransitionApproachZoom"/> so the settle phase reads as continuing to zoom deeper into the same location, never a snap.</summary>
        private const float OkinawaSettleZoom = 2.0f;

        /// <summary>True for the whole approach-swap-settle sequence. Static/session-scoped like MapNavigationState: other Map controllers (including MapPanZoomController) check this to ignore input mid-transition and to prevent a transition starting twice.</summary>
        public static bool IsTransitioning { get; private set; }

        private VisualElement _root;
        private IScreenNavigator _navigator;
        private MapPanZoomController _japanPanZoom;
        private MapPanZoomController _okinawaPanZoom;

        private VisualElement _japanCanvas;
        private VisualElement _japanCloudLayer;
        private VisualElement _japanLeft1;
        private VisualElement _japanLeft2;
        private VisualElement _japanRight1;
        private VisualElement _japanBottom1;
        private float _lastJapanCanvasWidth;
        private float _lastJapanCanvasHeight;

        private VisualElement _okinawaCanvas;
        private VisualElement _okinawaCloudLayer;
        private VisualElement _okinawaLeft1;
        private VisualElement _okinawaLeft2;
        private VisualElement _okinawaRight1;
        private VisualElement _okinawaBottom1;
        private float _lastOkinawaCanvasWidth;
        private float _lastOkinawaCanvasHeight;

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
                _japanCanvas?.UnregisterCallback<GeometryChangedEvent>(OnJapanCanvasGeometryChanged);
                _okinawaCanvas?.UnregisterCallback<GeometryChangedEvent>(OnOkinawaCanvasGeometryChanged);
            }

            _root = null;
            _navigator = null;
            _japanPanZoom = null;
            _okinawaPanZoom = null;
            _bound = false;
            IsTransitioning = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[MapCloudTransitionController] UIDocument root unavailable; chapter transitions not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;

            _japanCanvas = _root.Q<VisualElement>("map-canvas");
            _japanCloudLayer = _root.Q<VisualElement>("map-cloud-layer");
            _japanLeft1 = _root.Q<VisualElement>("map-cloud-left-01");
            _japanLeft2 = _root.Q<VisualElement>("map-cloud-left-02");
            _japanRight1 = _root.Q<VisualElement>("map-cloud-right-01");
            _japanBottom1 = _root.Q<VisualElement>("map-cloud-bottom-01");

            _okinawaCanvas = _root.Q<VisualElement>("okinawa-canvas");
            _okinawaCloudLayer = _root.Q<VisualElement>("okinawa-cloud-layer");
            _okinawaLeft1 = _root.Q<VisualElement>("okinawa-cloud-left-01");
            _okinawaLeft2 = _root.Q<VisualElement>("okinawa-cloud-left-02");
            _okinawaRight1 = _root.Q<VisualElement>("okinawa-cloud-right-01");
            _okinawaBottom1 = _root.Q<VisualElement>("okinawa-cloud-bottom-01");

            if (_japanCanvas == null || _japanCloudLayer == null || _japanLeft1 == null || _japanLeft2 == null || _japanRight1 == null || _japanBottom1 == null
                || _okinawaCanvas == null || _okinawaCloudLayer == null || _okinawaLeft1 == null || _okinawaLeft2 == null || _okinawaRight1 == null || _okinawaBottom1 == null)
            {
                Debug.LogError("[MapCloudTransitionController] Cloud layer elements missing; chapter transitions not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _navigator = GetComponent<IScreenNavigator>();

            foreach (var controller in GetComponents<MapPanZoomController>())
            {
                if (controller.ScreenId == "map")
                    _japanPanZoom = controller;
                else if (controller.ScreenId == "mapOkinawa")
                    _okinawaPanZoom = controller;
            }

            // Clouds rest at their fixed composition immediately, always —
            // there is no close/open sequence, on plain screen entry or on
            // a chapter transition (see PlayJapanToOkinawa/PlayOkinawaToJapan
            // below, which only ever move the CAMERA).
            ApplyJapanRest();
            ApplyOkinawaRest();
            SetInputLock(_japanCloudLayer, false);
            SetInputLock(_okinawaCloudLayer, false);

            _japanCanvas.RegisterCallback<GeometryChangedEvent>(OnJapanCanvasGeometryChanged);
            _okinawaCanvas.RegisterCallback<GeometryChangedEvent>(OnOkinawaCanvasGeometryChanged);

            _bound = true;
            _bindRoutine = null;
        }

        /// <summary>Change-gated on the Japan canvas's own resolved size (mirrors SafeAreaController/marker resize reprojection) — reapplies JapanRest only while no transition is running, so resting clouds keep their composition on Game View resize/orientation change without fighting an in-flight camera transition.</summary>
        private void OnJapanCanvasGeometryChanged(GeometryChangedEvent evt)
        {
            float width = _japanCanvas?.resolvedStyle.width ?? 0f;
            float height = _japanCanvas?.resolvedStyle.height ?? 0f;
            if (width == _lastJapanCanvasWidth && height == _lastJapanCanvasHeight)
                return;
            if (IsTransitioning)
                return;
            ApplyJapanRest();
        }

        private void OnOkinawaCanvasGeometryChanged(GeometryChangedEvent evt)
        {
            float width = _okinawaCanvas?.resolvedStyle.width ?? 0f;
            float height = _okinawaCanvas?.resolvedStyle.height ?? 0f;
            if (width == _lastOkinawaCanvasWidth && height == _lastOkinawaCanvasHeight)
                return;
            if (IsTransitioning)
                return;
            ApplyOkinawaRest();
        }

        private void ApplyJapanRest()
        {
            float width = _japanCanvas?.resolvedStyle.width ?? 0f;
            float height = _japanCanvas?.resolvedStyle.height ?? 0f;
            MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest, width, height);
            _lastJapanCanvasWidth = width;
            _lastJapanCanvasHeight = height;
        }

        private void ApplyOkinawaRest()
        {
            float width = _okinawaCanvas?.resolvedStyle.width ?? 0f;
            float height = _okinawaCanvas?.resolvedStyle.height ?? 0f;
            MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest, width, height);
            _lastOkinawaCanvasWidth = width;
            _lastOkinawaCanvasHeight = height;
        }

        /// <summary>
        /// Japan world map -&gt; Okinawa chapter map, triggered by the Japan
        /// chapter panel's "Enter Chapter" action. Approach dives the Japan
        /// camera toward the Okinawa chapter marker's own location; the swap
        /// carries that exact framing onto Okinawa's canvas; settle
        /// continues zooming in further.
        /// </summary>
        public IEnumerator PlayJapanToOkinawa()
        {
            if (IsTransitioning || !_bound)
                yield break;
            IsTransitioning = true;
            SetInputLock(_japanCloudLayer, true);

            if (_japanPanZoom != null && MapMarkerLayout.TryGetChapterFocalPoint(MapMarkerLayout.OkinawaChapterId, out float okinawaMarkerX, out float okinawaMarkerY))
                yield return _japanPanZoom.AnimateViewToSourceFocalPoint(okinawaMarkerX, okinawaMarkerY, TransitionApproachZoom, ApproachDurationSeconds);

            // Read back whatever the approach actually landed on (respects
            // any pan/zoom clamping) rather than assuming the exact target
            // was reached, then transfer it to Okinawa's canvas instantly,
            // before the swap — Okinawa is already correctly framed the
            // moment it becomes visible, no crossfade needed.
            float focusX = 0f, focusY = 0f;
            bool hasView = _japanPanZoom != null && _japanPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY);
            float capturedZoom = _japanPanZoom != null ? _japanPanZoom.CurrentZoom : TransitionApproachZoom;

            SetInputLock(_okinawaCloudLayer, true);
            if (hasView)
                _okinawaPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);

            _navigator?.Show("mapOkinawa");

            if (hasView && _okinawaPanZoom != null)
                yield return _okinawaPanZoom.AnimateViewToSourceFocalPoint(focusX, focusY, OkinawaSettleZoom, SettleDurationSeconds);

            SetInputLock(_japanCloudLayer, false);
            SetInputLock(_okinawaCloudLayer, false);
            IsTransitioning = false;
        }

        /// <summary>
        /// Okinawa chapter map -&gt; Japan world map, triggered by Okinawa's
        /// top-bar "Map" action. Approach captures wherever the player
        /// currently is on Okinawa and zooms out in place (no pan); the swap
        /// carries that exact point onto Japan's canvas; settle continues
        /// zooming out toward Japan's own DefaultZoom, so the player
        /// re-emerges at the corresponding place on the world map.
        /// </summary>
        public IEnumerator PlayOkinawaToJapan()
        {
            if (IsTransitioning || !_bound)
                yield break;
            IsTransitioning = true;
            SetInputLock(_okinawaCloudLayer, true);

            float startFocusX = 0f, startFocusY = 0f;
            bool hasStartView = _okinawaPanZoom != null && _okinawaPanZoom.TryGetCurrentSourceFocalPoint(out startFocusX, out startFocusY);
            if (hasStartView)
                yield return _okinawaPanZoom.AnimateViewToSourceFocalPoint(startFocusX, startFocusY, TransitionApproachZoom, ApproachDurationSeconds);

            float focusX = 0f, focusY = 0f;
            bool hasView = _okinawaPanZoom != null && _okinawaPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY);
            float capturedZoom = _okinawaPanZoom != null ? _okinawaPanZoom.CurrentZoom : TransitionApproachZoom;

            SetInputLock(_japanCloudLayer, true);
            if (hasView)
                _japanPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);

            // The explicit "return to world map" context reset — replicates
            // what OnTopbarMapClicked used to do directly before Map Pass
            // 3B; JapanMapController.OnScreenChanged does not reset this on
            // its own for a generic screen-changed-to-Japan event.
            MapNavigationState.Current = MapContext.JapanWorld;
            _navigator?.Show("map");

            if (hasView && _japanPanZoom != null)
                yield return _japanPanZoom.AnimateViewToSourceFocalPoint(focusX, focusY, MapPanZoomMath.DefaultZoom, SettleDurationSeconds);

            SetInputLock(_okinawaCloudLayer, false);
            SetInputLock(_japanCloudLayer, false);
            IsTransitioning = false;
        }

        /// <summary>The cloud layer's own picking-mode is the map-content-area input blocker — Position while locked (transitioning), Ignore at rest (mirrors ".map-outside-catcher"). The four decorative cloud sprites underneath always stay non-picking.</summary>
        private static void SetInputLock(VisualElement cloudLayer, bool locked)
        {
            if (cloudLayer == null)
                return;
            cloudLayer.pickingMode = locked ? PickingMode.Position : PickingMode.Ignore;
        }
    }
}
