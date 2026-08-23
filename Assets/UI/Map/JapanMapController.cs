using System.Collections;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives the Japan world map screen ("map"): chapter/city marker
    /// selection, the shared chapter detail overlay panel (never a
    /// permanently-reserved column, never auto-selected on entry), the
    /// Okinawa preview video, the top quick-access bar, and the short
    /// ink-fade transition into the Okinawa chapter map. The top bar's
    /// Settings button opens the one shared Settings modal — this controller
    /// doesn't wire it at all (see Mikey.UI.Settings.SettingsModalController,
    /// which finds "map-topbar-settings" itself). Replaces the old MVP's
    /// MapLevelPreviewController (flattened single map image, always-open
    /// Okinawa panel, auto-selection on entry). Mirrors the bind/entry/
    /// unsubscribe pattern used throughout this app (see HomeController,
    /// MapPanZoomController).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class JapanMapController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "map";
        private const string OkinawaChapterId = MapMarkerLayout.OkinawaChapterId;
        private const string FukuokaChapterId = MapMarkerLayout.FukuokaChapterId;
        private const string HiroshimaChapterId = MapMarkerLayout.HiroshimaChapterId;

        /// <summary>Where LVL 0-5 gameplay actually lives once a chapter is entered.</summary>
        public const string OkinawaChapterScreenId = "mapOkinawa";

        /// <summary>Where the top bar's Techniques button routes once unlocked.</summary>
        public const string TechniquesScreenId = "techniques";

        /// <summary>Where the top bar's Stats button routes (no gate).</summary>
        public const string StatsScreenId = "profile";

        private const string SelectedNodeClass = "chapter-node--selected";
        private const string LockedNodeClass = "chapter-node--locked";
        private const string PanelOpenClass = "detail-panel--open";
        private const string FallbackVisibleClass = "detail-panel__video-fallback--visible";
        private const string LockedCtaClass = "detail-panel__cta--locked";
        private const string TransitionVisibleClass = "map-transition-overlay--visible";
        private const string NavLockedClass = "map-topbar__nav-btn--locked";

        [Tooltip("Inline looping preview clip shown in the Okinawa chapter panel.")]
        [SerializeField] private VideoClip okinawaPreviewClip;

        private VisualElement _root;
        private VisualElement _canvas;
        private float _lastCanvasWidth;
        private float _lastCanvasHeight;
        private Button _okinawaNode;
        private Button _fukuokaNode;
        private Button _hiroshimaNode;
        private VisualElement _outsideCatcher;
        private VisualElement _panel;
        private VisualElement _panelVideo;
        private VisualElement _panelVideoFallback;
        private Label _panelEyebrow;
        private Label _panelTitle;
        private Label _panelDesc;
        private Label _panelMeta;
        private Button _panelCta;
        private Label _panelCtaText;
        private VisualElement _transitionOverlay;

        private Button _topbarMap;
        private Button _topbarTechniques;
        private Button _topbarStats;

        private VideoPlayer _okinawaPlayer;
        private RenderTexture _okinawaRenderTexture;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;

        private string _selectedChapter;
        private bool _transitioning;
        private Coroutine _bindRoutine;
        private Coroutine _transitionRoutine;
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
            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }

            if (_bound)
            {
                _canvas?.UnregisterCallback<GeometryChangedEvent>(OnCanvasGeometryChanged);
                _okinawaNode.clicked -= OnOkinawaClicked;
                _fukuokaNode.clicked -= OnFukuokaClicked;
                _hiroshimaNode.clicked -= OnHiroshimaClicked;
                _outsideCatcher.UnregisterCallback<PointerDownEvent>(OnOutsideCatcherPointerDown);
                _panelCta.clicked -= OnEnterChapterClicked;
                _topbarMap.clicked -= OnTopbarMapClicked;
                _topbarTechniques.clicked -= OnTopbarTechniquesClicked;
                _topbarStats.clicked -= OnTopbarStatsClicked;
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            if (_progress != null)
            {
                _progress.Changed -= RefreshTechniquesGate;
                _progress = null;
            }

            if (_okinawaPlayer != null)
            {
                _okinawaPlayer.prepareCompleted -= OnPrepareCompleted;
                _okinawaPlayer.errorReceived -= OnErrorReceived;
                _okinawaPlayer.Stop();
                Destroy(_okinawaPlayer.gameObject);
                _okinawaPlayer = null;
            }
            if (_okinawaRenderTexture != null)
            {
                _okinawaRenderTexture.Release();
                Destroy(_okinawaRenderTexture);
                _okinawaRenderTexture = null;
            }

            _canvas = null;
            _lastCanvasWidth = 0f;
            _lastCanvasHeight = 0f;
            _selectedChapter = null;
            _transitioning = false;
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
                    Debug.LogError("[JapanMapController] UIDocument root unavailable; Japan map not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;

            _okinawaNode = _root.Q<Button>("chapter-node-okinawa");
            _fukuokaNode = _root.Q<Button>("chapter-node-fukuoka");
            _hiroshimaNode = _root.Q<Button>("chapter-node-hiroshima");
            _outsideCatcher = _root.Q<VisualElement>("map-outside-catcher");
            _panel = _root.Q<VisualElement>("chapter-panel");
            _panelVideo = _root.Q<VisualElement>("chapter-panel-video");
            _panelVideoFallback = _root.Q<VisualElement>("chapter-panel-video-fallback");
            _panelEyebrow = _root.Q<Label>("chapter-panel-eyebrow");
            _panelTitle = _root.Q<Label>("chapter-panel-title");
            _panelDesc = _root.Q<Label>("chapter-panel-desc");
            _panelMeta = _root.Q<Label>("chapter-panel-meta");
            _panelCta = _root.Q<Button>("chapter-panel-cta");
            _panelCtaText = _root.Q<Label>("chapter-panel-cta-text");
            _transitionOverlay = _root.Q<VisualElement>("map-transition-overlay");

            _topbarMap = _root.Q<Button>("map-topbar-map");
            _topbarTechniques = _root.Q<Button>("map-topbar-techniques");
            _topbarStats = _root.Q<Button>("map-topbar-stats");

            if (_okinawaNode == null || _fukuokaNode == null || _hiroshimaNode == null || _panel == null || _panelCta == null)
            {
                Debug.LogError("[JapanMapController] Japan map elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            // Marker positions are stored as SOURCE-IMAGE-normalized
            // coordinates (see MapMarkerLayout) and must be converted
            // through the CURRENT canvas size to land correctly under the
            // map art's cover-fit crop — never applied as a raw percentage.
            // That canvas size can change after bind (Game View
            // maximize/restore, aspect change, device rotation), so this is
            // reapplied on every genuine GeometryChangedEvent, not just once
            // here — see OnCanvasGeometryChanged.
            _canvas = _root.Q<VisualElement>("map-canvas");
            ApplyAllChapterPositions();
            _canvas?.RegisterCallback<GeometryChangedEvent>(OnCanvasGeometryChanged);

            _okinawaNode.clicked += OnOkinawaClicked;
            _fukuokaNode.clicked += OnFukuokaClicked;
            _hiroshimaNode.clicked += OnHiroshimaClicked;
            _outsideCatcher?.RegisterCallback<PointerDownEvent>(OnOutsideCatcherPointerDown);
            _panelCta.clicked += OnEnterChapterClicked;
            if (_topbarMap != null)
                _topbarMap.clicked += OnTopbarMapClicked;
            if (_topbarTechniques != null)
                _topbarTechniques.clicked += OnTopbarTechniquesClicked;
            if (_topbarStats != null)
                _topbarStats.clicked += OnTopbarStatsClicked;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            _progress = GetComponent<ITutorialProgress>();
            if (_progress != null)
                _progress.Changed += RefreshTechniquesGate;
            RefreshTechniquesGate();

            ResetToDefaultState();

            _bound = true;
            _bindRoutine = null;
        }

        /// <summary>
        /// Re-reads the canvas's current resolved size and reapplies every
        /// chapter marker's position from it — called once at bind time and
        /// again whenever <see cref="OnCanvasGeometryChanged"/> detects the
        /// canvas actually changed size, so a marker stays attached to the
        /// same geographical source-image point across Game View
        /// maximize/restore, aspect changes, and device rotation.
        /// </summary>
        private void ApplyAllChapterPositions()
        {
            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;
            ApplyChapterPosition(_okinawaNode, OkinawaChapterId, width, height);
            ApplyChapterPosition(_fukuokaNode, FukuokaChapterId, width, height);
            ApplyChapterPosition(_hiroshimaNode, HiroshimaChapterId, width, height);
            _lastCanvasWidth = width;
            _lastCanvasHeight = height;
        }

        /// <summary>
        /// Change-gated on the canvas's own resolved size (mirrors
        /// SafeAreaController's cache-and-compare pattern) so a spurious
        /// geometry event that didn't actually change the size is a cheap
        /// no-op — repositioning markers (absolute-positioned children)
        /// never changes the canvas's own size in turn, so there is no
        /// feedback loop, but the cache still avoids redundant work.
        /// </summary>
        private void OnCanvasGeometryChanged(GeometryChangedEvent evt)
        {
            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;
            if (width == _lastCanvasWidth && height == _lastCanvasHeight)
                return;

            ApplyAllChapterPositions();
        }

        /// <summary>
        /// Applies this chapter's source-image-normalized position from
        /// MapMarkerLayout — the only place its coordinates live — converted
        /// through the current viewport size (see
        /// MapMarkerLayout.ApplySourceCoordinate/MapCoordinateMapping).
        /// </summary>
        private static void ApplyChapterPosition(VisualElement node, string chapterId, float viewportWidth, float viewportHeight)
        {
            foreach (var chapter in MapMarkerLayout.Chapters)
            {
                if (chapter.Id != chapterId)
                    continue;
                MapMarkerLayout.ApplySourceCoordinate(node, chapter.NormalizedX, chapter.NormalizedY, viewportWidth, viewportHeight);
                return;
            }
        }

        private void OnOkinawaClicked() => ToggleChapter(OkinawaChapterId, _okinawaNode);

        private void OnFukuokaClicked() => ToggleChapter(FukuokaChapterId, _fukuokaNode);

        private void OnHiroshimaClicked() => ToggleChapter(HiroshimaChapterId, _hiroshimaNode);

        private void ToggleChapter(string chapterId, Button node)
        {
            if (_selectedChapter == chapterId)
            {
                ClosePanel();
                return;
            }

            SelectChapter(chapterId, node);
        }

        private void SelectChapter(string chapterId, Button node)
        {
            DeselectCurrentNode();

            _selectedChapter = chapterId;
            node.AddToClassList(SelectedNodeClass);
            SetOutsideCatcherActive(true);

            if (chapterId == OkinawaChapterId)
                ShowOkinawaPanel();
            else if (chapterId == FukuokaChapterId)
                ShowLockedChapterPanel("FUKUOKA", chapterNumber: 1);
            else
                ShowLockedChapterPanel("HIROSHIMA", chapterNumber: 2);

            _panel.AddToClassList(PanelOpenClass);
            _panel.pickingMode = PickingMode.Position;
        }

        private void ShowOkinawaPanel()
        {
            _panelEyebrow.text = "CHAPTER 0";
            _panelTitle.text = "OKINAWA";
            _panelDesc.text = "Your journey begins here. Establish your baseline, learn the foundations, and unlock the path ahead.";
            _panelMeta.text = "6 LEVELS";
            _panelCtaText.text = "ENTER CHAPTER";
            _panelCta.SetEnabled(true);
            _panelCta.RemoveFromClassList(LockedCtaClass);

            _panelVideo.style.display = DisplayStyle.Flex;
            PlayOkinawaPreview();
        }

        private void ShowLockedChapterPanel(string displayName, int chapterNumber)
        {
            StopOkinawaPreview();
            _panelVideo.style.display = DisplayStyle.None;
            HideFallback();

            _panelEyebrow.text = $"CHAPTER {chapterNumber}";
            _panelTitle.text = displayName;
            _panelDesc.text = "Locked for now. Complete the earlier chapters to continue your journey.";
            _panelMeta.text = string.Empty;
            _panelCtaText.text = "LOCKED";
            _panelCta.SetEnabled(false);
            _panelCta.AddToClassList(LockedCtaClass);
        }

        private void ClosePanel()
        {
            _panel.RemoveFromClassList(PanelOpenClass);
            _panel.pickingMode = PickingMode.Ignore;
            SetOutsideCatcherActive(false);
            StopOkinawaPreview();
            DeselectCurrentNode();
            _selectedChapter = null;
        }

        private void DeselectCurrentNode()
        {
            if (_selectedChapter == OkinawaChapterId)
                _okinawaNode.RemoveFromClassList(SelectedNodeClass);
            else if (_selectedChapter == FukuokaChapterId)
                _fukuokaNode.RemoveFromClassList(SelectedNodeClass);
            else if (_selectedChapter == HiroshimaChapterId)
                _hiroshimaNode.RemoveFromClassList(SelectedNodeClass);
        }

        private void SetOutsideCatcherActive(bool active)
        {
            if (_outsideCatcher != null)
                _outsideCatcher.pickingMode = active ? PickingMode.Position : PickingMode.Ignore;
        }

        private void OnOutsideCatcherPointerDown(PointerDownEvent evt) => ClosePanel();

        private void OnEnterChapterClicked()
        {
            if (_selectedChapter != OkinawaChapterId || _transitioning)
                return;

            _transitionRoutine = StartCoroutine(PlayTransitionThenEnterOkinawa());
        }

        /// <summary>
        /// Replaces the old instantaneous ink-fade swap with the Map Pass 3B
        /// cloud transition (see MapCloudTransitionController) — falls back
        /// to an immediate Show() if that controller is somehow missing from
        /// the scene rather than leaving the player stuck.
        /// </summary>
        private IEnumerator PlayTransitionThenEnterOkinawa()
        {
            _transitioning = true;

            var cloudTransition = GetComponent<MapCloudTransitionController>();
            if (cloudTransition != null)
                yield return cloudTransition.PlayJapanToOkinawa();
            else
                _navigator?.Show(OkinawaChapterScreenId);

            _transitioning = false;
            _transitionRoutine = null;
        }

        private void OnTopbarMapClicked()
        {
            // Ignored mid cloud-transition — see MapCloudTransitionController;
            // navigating away here while Phase A/B/C is still driving the
            // canvas/screen swap would leave the transition's own Show() call
            // fighting this one.
            if (MapCloudTransitionController.IsTransitioning)
                return;

            // The explicit "return to world map" action: always resets context
            // to Japan, even if it was already Japan. Already on Japan: Show()
            // is a safe no-op if nothing changed, but the reset must still
            // happen explicitly here since ScreenChanged only fires on a
            // genuine screen change.
            MapNavigationState.Current = MapContext.JapanWorld;
            ResetToDefaultState();
            _navigator?.Show(ScreenId);
        }

        private void OnTopbarTechniquesClicked()
        {
            if (MapCloudTransitionController.IsTransitioning)
                return;
            if (_progress != null && !TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))
                return;
            _navigator?.Show(TechniquesScreenId);
        }

        private void OnTopbarStatsClicked()
        {
            if (MapCloudTransitionController.IsTransitioning)
                return;
            _navigator?.Show(StatsScreenId);
        }

        private void RefreshTechniquesGate()
        {
            if (_topbarTechniques == null)
                return;

            bool unlocked = _progress == null || TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State);
            if (unlocked)
                _topbarTechniques.RemoveFromClassList(NavLockedClass);
            else
                _topbarTechniques.AddToClassList(NavLockedClass);
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
            {
                // Returning to Map from another screen (Stats, Techniques,
                // Settings...) while the player was last in the Okinawa chapter
                // must restore Okinawa, not throw them back to the Japan world
                // map. Context only resets to Japan via an explicit world-map
                // action (OnTopbarMapClicked) or Okinawa's own "MAP" button.
                if (MapNavigationState.Current == MapContext.OkinawaChapter)
                {
                    _navigator?.Show(OkinawaChapterScreenId);
                    return;
                }

                ResetToDefaultState();
                RefreshTechniquesGate();
            }
            else
            {
                StopOkinawaPreview();
            }
        }

        /// <summary>No selection, panel closed, preview stopped, transition overlay cleared — the required default state on every fresh entry to the Japan map.</summary>
        private void ResetToDefaultState()
        {
            ClosePanel();
            _transitionOverlay?.RemoveFromClassList(TransitionVisibleClass);
        }

        private void PlayOkinawaPreview()
        {
            if (okinawaPreviewClip == null)
            {
                ShowFallback();
                return;
            }

            HideFallback();
            VideoPlayer player = GetOrCreateOkinawaPlayer();
            if (player.isPrepared)
            {
                ApplyTexture();
                player.Play();
            }
            else
            {
                player.Prepare();
            }
        }

        private void StopOkinawaPreview()
        {
            if (_okinawaPlayer != null && (_okinawaPlayer.isPlaying || _okinawaPlayer.isPaused))
                _okinawaPlayer.Pause();
        }

        private VideoPlayer GetOrCreateOkinawaPlayer()
        {
            if (_okinawaPlayer != null)
                return _okinawaPlayer;

            _okinawaRenderTexture = new RenderTexture((int)okinawaPreviewClip.width, (int)okinawaPreviewClip.height, 0)
            {
                name = "OkinawaPreviewRT"
            };

            var playerGo = new GameObject("OkinawaPreviewVideoPlayer");
            playerGo.transform.SetParent(transform, false);
            _okinawaPlayer = playerGo.AddComponent<VideoPlayer>();
            _okinawaPlayer.playOnAwake = false;
            _okinawaPlayer.isLooping = true;
            _okinawaPlayer.renderMode = VideoRenderMode.RenderTexture;
            _okinawaPlayer.targetTexture = _okinawaRenderTexture;
            _okinawaPlayer.source = VideoSource.VideoClip;
            _okinawaPlayer.clip = okinawaPreviewClip;
            _okinawaPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _okinawaPlayer.prepareCompleted += OnPrepareCompleted;
            _okinawaPlayer.errorReceived += OnErrorReceived;

            return _okinawaPlayer;
        }

        private void OnPrepareCompleted(VideoPlayer source)
        {
            if (_selectedChapter != OkinawaChapterId)
                return;
            ApplyTexture();
            source.Play();
        }

        private void OnErrorReceived(VideoPlayer source, string message)
        {
            Debug.LogWarning($"[JapanMapController] Okinawa preview video error: {message}. Falling back to the static placeholder.");
            if (_selectedChapter == OkinawaChapterId)
                ShowFallback();
        }

        private void ApplyTexture()
        {
            if (_panelVideo != null && _okinawaRenderTexture != null)
                _panelVideo.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_okinawaRenderTexture));
        }

        private void ShowFallback()
        {
            if (_panelVideo != null)
                _panelVideo.style.backgroundImage = StyleKeyword.Null;
            _panelVideoFallback?.AddToClassList(FallbackVisibleClass);
        }

        private void HideFallback() => _panelVideoFallback?.RemoveFromClassList(FallbackVisibleClass);
    }
}
