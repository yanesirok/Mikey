using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives the sumi-e cloud transition between the Japan world map and the
    /// Okinawa chapter map (Map Pass 3B/3C, corrected architecture + camera
    /// refinement pass): Phase A EXPANDS the SOURCE screen's clouds from
    /// their rest layout toward a per-cloud computed closed layout (see
    /// MapCloudMath.ComputeClosedLayout — each cloud grows from its own
    /// resting rectangle while one edge/corner stays fixed, never a slide
    /// toward the center) while ramping opacity to fully opaque, fully
    /// hiding the map. Phase B swaps the active screen while covered (the
    /// DESTINATION screen's clouds are set to ITS OWN computed closed layout
    /// instantly, invisibly, just before the swap — and, in the same
    /// invisible instant, the destination's pan/zoom camera is set to
    /// reproduce the SOURCE screen's exact spatial framing (see
    /// <see cref="PlayJapanToOkinawa"/>/<see cref="PlayOkinawaToJapan"/>'s
    /// use of MapPanZoomController.TryGetCurrentSourceFocalPoint/
    /// SetViewToSourceFocalPoint), so the swap never reads as a camera
    /// teleport underneath the clouds). Phase C contracts the DESTINATION
    /// screen's clouds from closed back down to its own rest layout, ramping
    /// opacity back to each cloud's resting value. One instance owns both
    /// screens' cloud AND pan/zoom elements since a single transition must
    /// coordinate across them.
    ///
    /// Clouds are part of the painted map composition, not screen-fixed
    /// atmospheric HUD: they are children of the SAME transformed
    /// ".pan-canvas" as the map art and markers (see Map.uss
    /// ".map-cloud-layer"), positioned as SOURCE-IMAGE-normalized rectangles
    /// converted through the same cover-fit crop as marker coordinates (see
    /// MapCoordinateMapping.SourceRectToViewport / MapCloudLayout.Apply) —
    /// so they pan/zoom together with the map art, exactly like markers.
    /// Because of that shared dependency on the canvas's current resolved
    /// size, this controller reapplies the CURRENT rest preset on canvas
    /// resize while no transition is running (mirrors
    /// JapanMapController/OkinawaMapController's marker resize reprojection).
    ///
    /// Spatial continuity: since Japan and Okinawa share the exact same
    /// source image dimensions (MapMarkerLayout.SourceImageWidth/Height),
    /// "what source-image point is under the viewport center right now" (see
    /// MapPanZoomController.TryGetCurrentSourceFocalPoint) is directly
    /// meaningful on either screen with no remapping — captured from the
    /// SOURCE screen at the very start of the transition (its camera never
    /// moves during the close/hold/reveal sequence, only its clouds do) and
    /// applied to the DESTINATION screen instantly while hidden (see
    /// <see cref="MapPanZoomController.SetViewToSourceFocalPoint"/>), zoom
    /// included. MapPanZoomController.OnScreenChanged separately guards
    /// against its own normal "fresh entry" reset fighting this transferred
    /// view (checks <see cref="IsTransitioning"/> itself).
    ///
    /// Cloud motion quality: close and reveal use DIFFERENT eased curves
    /// (MapCloudMath.EaseInOutQuart for close — a steeper, more dramatic
    /// "gather and spread" — MapCloudMath.EaseInOutCubic for reveal — a
    /// gentler "settle"), and the 4 clouds start at very slightly different
    /// offsets within each phase (see the per-cloud *StaggerSeconds
    /// constants) so all 4 never move in obvious lockstep, without
    /// reintroducing the old "four blobs sliding" bug (motion is still pure
    /// anchor-preserving expansion, never a slide).
    ///
    /// Input lock: each screen's cloud-layer container's own picking-mode is
    /// the map-content-area input blocker during a transition (Position
    /// while locked, Ignore at rest — mirrors the existing
    /// ".map-outside-catcher" pattern), which blocks marker taps and
    /// outside-tap panel close by z-order/hit-testing alone. Pan/wheel-zoom/
    /// pinch-zoom are NOT routed through picking-mode at all (pinch reads
    /// Touchscreen directly, bypassing the visual tree entirely) — see
    /// MapPanZoomController, which checks <see cref="IsTransitioning"/>
    /// itself before acting on any input source. It does NOT reach the
    /// shared topbar (a sibling of ".pan-stage", outside the transformed
    /// layer) or an already-open detail panel's own CTA, which is why
    /// JapanMapController/OkinawaMapController separately check
    /// <see cref="IsTransitioning"/> before acting on topbar navigation.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapCloudTransitionController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>How long the close phase (rest -> full cover) takes, before any per-cloud stagger. Slower than the reveal (see class remarks: close should read as a more deliberate "gathering").</summary>
        private const float CloudCloseDurationSeconds = 1.05f;

        /// <summary>How long the reveal phase (full cover -> destination rest) takes, before any per-cloud stagger.</summary>
        private const float CloudRevealDurationSeconds = 0.95f;

        /// <summary>Brief hold at full cover so the (invisible) screen swap and camera transfer never feel rushed or clipped.</summary>
        private const float FullCoverHoldSeconds = 0.12f;

        /// <summary>Every cloud reads as fully opaque at full cover, regardless of its own rest opacity, so the map is completely hidden.</summary>
        private const float FullCoverOpacity = 1.0f;

        /// <summary>
        /// Small per-cloud start-delay stagger, applied identically to both
        /// close and reveal, so the 4 clouds never move in obvious lockstep
        /// (see class remarks). Deliberately tiny relative to the phase
        /// durations above — this is a subtle desynchronization, not a
        /// sequential reveal. Follows the shared paint order (Right1, Left1,
        /// Left2, Bottom1 — see MikeyApp.uxml/Map.uss).
        /// </summary>
        private const float Right1StaggerSeconds = 0f;
        private const float Left1StaggerSeconds = 0.025f;
        private const float Left2StaggerSeconds = 0.05f;
        private const float Bottom1StaggerSeconds = 0.075f;
        private const float MaxCloudStaggerSeconds = Bottom1StaggerSeconds;

        /// <summary>True for the whole close-hold-reveal sequence, from Phase A start to Phase C end. Static/session-scoped like MapNavigationState: other Map controllers (including MapPanZoomController) check this to ignore input mid-transition and to prevent a transition starting twice.</summary>
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
                    Debug.LogError("[MapCloudTransitionController] UIDocument root unavailable; cloud transitions not bound.", this);
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
                Debug.LogError("[MapCloudTransitionController] Cloud layer elements missing; cloud transitions not bound.", this);
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

            // First entry to either screen (from Main Menu, or reopening Map
            // in whichever context MapNavigationState already holds) shows
            // clouds already resting — never a close/open sequence on plain
            // screen entry, only on an explicit chapter transition below.
            ApplyJapanRest();
            ApplyOkinawaRest();
            SetInputLock(_japanCloudLayer, false);
            SetInputLock(_okinawaCloudLayer, false);

            _japanCanvas.RegisterCallback<GeometryChangedEvent>(OnJapanCanvasGeometryChanged);
            _okinawaCanvas.RegisterCallback<GeometryChangedEvent>(OnOkinawaCanvasGeometryChanged);

            _bound = true;
            _bindRoutine = null;
        }

        /// <summary>Change-gated on the Japan canvas's own resolved size (mirrors SafeAreaController/marker resize reprojection) — reapplies JapanRest only while no transition is running, so resting clouds keep their composition on Game View resize/orientation change without fighting an in-flight animation.</summary>
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

        /// <summary>Japan world map -> Okinawa chapter map, triggered by the Japan chapter panel's "Enter Chapter" action.</summary>
        public IEnumerator PlayJapanToOkinawa()
        {
            if (IsTransitioning || !_bound)
                yield break;
            IsTransitioning = true;

            // Captured before anything closes: the source screen's own
            // camera never moves during the close/hold/reveal sequence
            // (only its clouds do), so the view right now IS the view the
            // player leaves Japan looking at.
            float focusX = 0f, focusY = 0f;
            bool hasView = _japanPanZoom != null && _japanPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY);
            float capturedZoom = _japanPanZoom != null ? _japanPanZoom.CurrentZoom : MapPanZoomMath.DefaultZoom;

            SetInputLock(_japanCloudLayer, true);
            float japanWidth = _japanCanvas.resolvedStyle.width;
            float japanHeight = _japanCanvas.resolvedStyle.height;
            MapCloudPreset japanClosed = ComputeClosedPreset(MapCloudLayout.JapanRest);
            yield return AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest, japanClosed, japanWidth, japanHeight, CloudCloseDurationSeconds, MapCloudMath.EaseInOutQuart);

            yield return new WaitForSeconds(FullCoverHoldSeconds);

            // Okinawa's clouds aren't visible yet (the "map" screen is still
            // shown) — snapping them to THEIR OWN computed closed layout
            // here, before the swap, means the swap itself is never visible:
            // both screens read as identically "fully covered" at the
            // instant it happens. The camera transfer happens in this same
            // invisible instant, so Okinawa is already framed at Japan's
            // captured view before a single pixel of it is ever shown —
            // never a teleport underneath the clouds.
            float okinawaWidth = _okinawaCanvas.resolvedStyle.width;
            float okinawaHeight = _okinawaCanvas.resolvedStyle.height;
            MapCloudPreset okinawaClosed = ComputeClosedPreset(MapCloudLayout.OkinawaRest);
            MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, okinawaWidth, okinawaHeight);
            SetInputLock(_okinawaCloudLayer, true);
            if (hasView)
                _okinawaPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);

            _navigator?.Show("mapOkinawa");

            yield return AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, okinawaClosed, MapCloudLayout.OkinawaRest, okinawaWidth, okinawaHeight, CloudRevealDurationSeconds, MapCloudMath.EaseInOutCubic);

            SetInputLock(_japanCloudLayer, false);
            SetInputLock(_okinawaCloudLayer, false);
            IsTransitioning = false;
        }

        /// <summary>Okinawa chapter map -> Japan world map, triggered by Okinawa's top-bar "Map" action.</summary>
        public IEnumerator PlayOkinawaToJapan()
        {
            if (IsTransitioning || !_bound)
                yield break;
            IsTransitioning = true;

            float focusX = 0f, focusY = 0f;
            bool hasView = _okinawaPanZoom != null && _okinawaPanZoom.TryGetCurrentSourceFocalPoint(out focusX, out focusY);
            float capturedZoom = _okinawaPanZoom != null ? _okinawaPanZoom.CurrentZoom : MapPanZoomMath.DefaultZoom;

            SetInputLock(_okinawaCloudLayer, true);
            float okinawaWidth = _okinawaCanvas.resolvedStyle.width;
            float okinawaHeight = _okinawaCanvas.resolvedStyle.height;
            MapCloudPreset okinawaClosed = ComputeClosedPreset(MapCloudLayout.OkinawaRest);
            yield return AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest, okinawaClosed, okinawaWidth, okinawaHeight, CloudCloseDurationSeconds, MapCloudMath.EaseInOutQuart);

            yield return new WaitForSeconds(FullCoverHoldSeconds);

            float japanWidth = _japanCanvas.resolvedStyle.width;
            float japanHeight = _japanCanvas.resolvedStyle.height;
            MapCloudPreset japanClosed = ComputeClosedPreset(MapCloudLayout.JapanRest);
            MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, japanClosed, japanWidth, japanHeight);
            SetInputLock(_japanCloudLayer, true);
            if (hasView)
                _japanPanZoom?.SetViewToSourceFocalPoint(focusX, focusY, capturedZoom);

            // The explicit "return to world map" context reset — replicates
            // what OnTopbarMapClicked used to do directly before this pass;
            // JapanMapController.OnScreenChanged does not reset this on its
            // own for a generic screen-changed-to-Japan event.
            MapNavigationState.Current = MapContext.JapanWorld;
            _navigator?.Show("map");

            yield return AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, japanClosed, MapCloudLayout.JapanRest, japanWidth, japanHeight, CloudRevealDurationSeconds, MapCloudMath.EaseInOutCubic);

            SetInputLock(_okinawaCloudLayer, false);
            SetInputLock(_japanCloudLayer, false);
            IsTransitioning = false;
        }

        /// <summary>
        /// Derives the fully-closed composition for one screen's 4 clouds
        /// from its rest preset — each cloud expands from its own resting
        /// rectangle with its own fixed anchor edge/corner (see
        /// MapCloudLayout.Left1Anchor etc.), and reads fully opaque at
        /// closed regardless of its rest opacity.
        /// </summary>
        private static MapCloudPreset ComputeClosedPreset(MapCloudPreset rest)
        {
            return new MapCloudPreset(
                left1: MapCloudMath.ComputeClosedLayout(rest.Left1, MapCloudLayout.Left1Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity),
                left2: MapCloudMath.ComputeClosedLayout(rest.Left2, MapCloudLayout.Left2Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity),
                right1: MapCloudMath.ComputeClosedLayout(rest.Right1, MapCloudLayout.Right1Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity),
                bottom1: MapCloudMath.ComputeClosedLayout(rest.Bottom1, MapCloudLayout.Bottom1Anchor, MapCloudLayout.CloseExpansionFactor, FullCoverOpacity));
        }

        /// <summary>
        /// Animates all 4 clouds of one screen from <paramref name="from"/>
        /// to <paramref name="to"/> over <paramref name="phaseDurationSeconds"/>,
        /// eased via <paramref name="easing"/> (steeper EaseInOutQuart for
        /// close, gentler EaseInOutCubic for reveal — see class remarks),
        /// with each cloud starting at its own small fixed stagger offset
        /// (see the *StaggerSeconds constants) so all 4 never move in
        /// obvious lockstep — each cloud's OWN local duration is shortened
        /// by the largest stagger so every cloud still finishes exactly at
        /// <paramref name="phaseDurationSeconds"/>. Snaps exactly to
        /// <paramref name="to"/> afterward so no rounding ever leaves a
        /// cloud a fraction short of its destination.
        /// </summary>
        private static IEnumerator AnimateCloudSet(VisualElement left1, VisualElement left2, VisualElement right1, VisualElement bottom1, MapCloudPreset from, MapCloudPreset to, float viewportWidth, float viewportHeight, float phaseDurationSeconds, System.Func<float, float> easing)
        {
            float cloudDurationSeconds = phaseDurationSeconds - MaxCloudStaggerSeconds;
            float elapsed = 0f;
            while (elapsed < phaseDurationSeconds)
            {
                elapsed += Time.deltaTime;
                ApplyStaggeredFrame(left1, from.Left1, to.Left1, elapsed, Left1StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);
                ApplyStaggeredFrame(left2, from.Left2, to.Left2, elapsed, Left2StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);
                ApplyStaggeredFrame(right1, from.Right1, to.Right1, elapsed, Right1StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);
                ApplyStaggeredFrame(bottom1, from.Bottom1, to.Bottom1, elapsed, Bottom1StaggerSeconds, cloudDurationSeconds, easing, viewportWidth, viewportHeight);
                yield return null;
            }

            MapCloudLayout.ApplyPreset(left1, left2, right1, bottom1, to, viewportWidth, viewportHeight);
        }

        private static void ApplyStaggeredFrame(VisualElement element, CloudLayout from, CloudLayout to, float elapsed, float staggerSeconds, float cloudDurationSeconds, System.Func<float, float> easing, float viewportWidth, float viewportHeight)
        {
            float t = MapCloudMath.LocalProgress(elapsed, staggerSeconds, cloudDurationSeconds);
            MapCloudLayout.Apply(element, MapCloudMath.LerpWithEasing(from, to, t, easing), viewportWidth, viewportHeight);
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
