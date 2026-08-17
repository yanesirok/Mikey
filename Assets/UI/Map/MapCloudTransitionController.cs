using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives the sumi-e cloud transition between the Japan world map and the
    /// Okinawa chapter map (Map Pass 3B): Phase A closes the SOURCE screen's
    /// clouds from its rest preset to the shared <see cref="MapCloudLayout.Cover"/>
    /// composition (fully hiding the map), Phase B swaps the active screen
    /// while covered (the DESTINATION screen's clouds are set to Cover
    /// instantly, invisibly, just before the swap), Phase C reveals the
    /// DESTINATION screen's clouds outward from Cover to its own rest preset.
    /// One instance owns both screens' cloud elements since a single
    /// transition must coordinate across them.
    ///
    /// Cloud elements are plain percentage-positioned children of a stable
    /// overlay sibling of ".pan-stage" (".map-cloud-layer"/".okinawa-cloud-layer")
    /// — never children of the transformed ".pan-canvas" — so they never
    /// pan/zoom with the map art, and (unlike marker positions, which are
    /// converted through MapCoordinateMapping's cover-fit crop) never need
    /// explicit resize-driven recomputation: UI Toolkit's layout engine
    /// already re-resolves percentage styles against the current parent size
    /// on every layout pass, including mid-transition.
    ///
    /// Input lock: each screen's cloud-layer container's own picking-mode is
    /// the map-content-area input blocker during a transition (Position while
    /// locked, Ignore at rest — mirrors the existing ".map-outside-catcher"
    /// pattern) rather than any of the four decorative cloud sprites
    /// themselves, which always stay non-picking. This blocks marker taps,
    /// pan/zoom, and outside-tap panel close (all nested inside ".pan-stage",
    /// which sits below the cloud layer) — it does NOT reach the shared
    /// topbar (a later, higher sibling) or an already-open detail panel's own
    /// CTA, which is why JapanMapController/OkinawaMapController separately
    /// check <see cref="IsTransitioning"/> before acting on topbar navigation.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapCloudTransitionController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>Per-cloud start delay within a phase — Left1 first, then Right1, then Left2, then Bottom1 (staggered so the motion reads as organic mist, not four synchronized panels).</summary>
        private const float Left1StartDelaySeconds = 0.00f;
        private const float Right1StartDelaySeconds = 0.05f;
        private const float Left2StartDelaySeconds = 0.10f;
        private const float Bottom1StartDelaySeconds = 0.14f;

        /// <summary>How long each individual cloud takes to move once its own start delay has elapsed (close and reveal both use this).</summary>
        private const float CloudMoveDurationSeconds = 0.65f;

        /// <summary>Brief hold at full cover so the (invisible) screen swap never feels rushed or clipped.</summary>
        private const float FullCoverHoldSeconds = 0.08f;

        /// <summary>True for the whole close-hold-reveal sequence, from Phase A start to Phase C end. Static/session-scoped like MapNavigationState: other Map controllers check this to ignore topbar navigation mid-transition and to prevent a transition starting twice.</summary>
        public static bool IsTransitioning { get; private set; }

        private VisualElement _root;
        private IScreenNavigator _navigator;

        private VisualElement _japanCloudLayer;
        private VisualElement _japanLeft1;
        private VisualElement _japanLeft2;
        private VisualElement _japanRight1;
        private VisualElement _japanBottom1;

        private VisualElement _okinawaCloudLayer;
        private VisualElement _okinawaLeft1;
        private VisualElement _okinawaLeft2;
        private VisualElement _okinawaRight1;
        private VisualElement _okinawaBottom1;

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

            _root = null;
            _navigator = null;
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

            _japanCloudLayer = _root.Q<VisualElement>("map-cloud-layer");
            _japanLeft1 = _root.Q<VisualElement>("map-cloud-left-01");
            _japanLeft2 = _root.Q<VisualElement>("map-cloud-left-02");
            _japanRight1 = _root.Q<VisualElement>("map-cloud-right-01");
            _japanBottom1 = _root.Q<VisualElement>("map-cloud-bottom-01");

            _okinawaCloudLayer = _root.Q<VisualElement>("okinawa-cloud-layer");
            _okinawaLeft1 = _root.Q<VisualElement>("okinawa-cloud-left-01");
            _okinawaLeft2 = _root.Q<VisualElement>("okinawa-cloud-left-02");
            _okinawaRight1 = _root.Q<VisualElement>("okinawa-cloud-right-01");
            _okinawaBottom1 = _root.Q<VisualElement>("okinawa-cloud-bottom-01");

            if (_japanCloudLayer == null || _japanLeft1 == null || _japanLeft2 == null || _japanRight1 == null || _japanBottom1 == null
                || _okinawaCloudLayer == null || _okinawaLeft1 == null || _okinawaLeft2 == null || _okinawaRight1 == null || _okinawaBottom1 == null)
            {
                Debug.LogError("[MapCloudTransitionController] Cloud layer elements missing; cloud transitions not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _navigator = GetComponent<IScreenNavigator>();

            // First entry to either screen (from Main Menu, or reopening Map
            // in whichever context MapNavigationState already holds) shows
            // clouds already resting — never a close/open sequence on plain
            // screen entry, only on an explicit chapter transition below.
            MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest);
            MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest);
            SetInputLock(_japanCloudLayer, false);
            SetInputLock(_okinawaCloudLayer, false);

            _bound = true;
            _bindRoutine = null;
        }

        /// <summary>Japan world map -> Okinawa chapter map, triggered by the Japan chapter panel's "Enter Chapter" action.</summary>
        public IEnumerator PlayJapanToOkinawa()
        {
            if (IsTransitioning || !_bound)
                yield break;
            IsTransitioning = true;

            SetInputLock(_japanCloudLayer, true);
            yield return AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.JapanRest, MapCloudLayout.Cover);

            yield return new WaitForSeconds(FullCoverHoldSeconds);

            // Okinawa's clouds aren't visible yet (the "map" screen is still
            // shown) — snapping them to Cover here, before the swap, means
            // the swap itself is never visible: both screens read as
            // identically "fully covered" at the instant it happens.
            MapCloudLayout.ApplyPreset(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.Cover);
            SetInputLock(_okinawaCloudLayer, true);

            _navigator?.Show("mapOkinawa");

            yield return AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.Cover, MapCloudLayout.OkinawaRest);

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

            SetInputLock(_okinawaCloudLayer, true);
            yield return AnimateCloudSet(_okinawaLeft1, _okinawaLeft2, _okinawaRight1, _okinawaBottom1, MapCloudLayout.OkinawaRest, MapCloudLayout.Cover);

            yield return new WaitForSeconds(FullCoverHoldSeconds);

            MapCloudLayout.ApplyPreset(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.Cover);
            SetInputLock(_japanCloudLayer, true);

            // The explicit "return to world map" context reset — replicates
            // what OnTopbarMapClicked used to do directly before this pass;
            // JapanMapController.OnScreenChanged does not reset this on its
            // own for a generic screen-changed-to-Japan event.
            MapNavigationState.Current = MapContext.JapanWorld;
            _navigator?.Show("map");

            yield return AnimateCloudSet(_japanLeft1, _japanLeft2, _japanRight1, _japanBottom1, MapCloudLayout.Cover, MapCloudLayout.JapanRest);

            SetInputLock(_okinawaCloudLayer, false);
            SetInputLock(_japanCloudLayer, false);
            IsTransitioning = false;
        }

        /// <summary>Animates all 4 clouds of one screen from <paramref name="from"/> to <paramref name="to"/>, staggered, then snaps exactly to <paramref name="to"/> so no rounding ever leaves a cloud a fraction short of its destination.</summary>
        private static IEnumerator AnimateCloudSet(VisualElement left1, VisualElement left2, VisualElement right1, VisualElement bottom1, MapCloudPreset from, MapCloudPreset to)
        {
            float totalPhaseDuration = Bottom1StartDelaySeconds + CloudMoveDurationSeconds;
            float elapsed = 0f;
            while (elapsed < totalPhaseDuration)
            {
                elapsed += Time.deltaTime;
                ApplyFrame(left1, from.Left1, to.Left1, elapsed, Left1StartDelaySeconds);
                ApplyFrame(right1, from.Right1, to.Right1, elapsed, Right1StartDelaySeconds);
                ApplyFrame(left2, from.Left2, to.Left2, elapsed, Left2StartDelaySeconds);
                ApplyFrame(bottom1, from.Bottom1, to.Bottom1, elapsed, Bottom1StartDelaySeconds);
                yield return null;
            }

            MapCloudLayout.ApplyPreset(left1, left2, right1, bottom1, to);
        }

        private static void ApplyFrame(VisualElement element, CloudLayout from, CloudLayout to, float elapsed, float startDelaySeconds)
        {
            float t = MapCloudMath.LocalProgress(elapsed, startDelaySeconds, CloudMoveDurationSeconds);
            MapCloudLayout.Apply(element, MapCloudMath.Lerp(from, to, t));
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
