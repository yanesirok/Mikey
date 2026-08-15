using System;
using System.Collections;
using Mikey.UI.Audio;
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
    /// ink-fade transition into the Okinawa chapter map. Replaces the old
    /// MVP's MapLevelPreviewController (flattened single map image, always-
    /// open Okinawa panel, auto-selection on entry). Mirrors the bind/entry/
    /// unsubscribe pattern used throughout this app (see HomeController,
    /// MapPanZoomController).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class JapanMapController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "map";
        private const string OkinawaChapterId = "okinawa";
        private const string TohokuChapterId = "tohoku";

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
        private const float TransitionSeconds = 0.35f;

        [Tooltip("Inline looping preview clip shown in the Okinawa chapter panel.")]
        [SerializeField] private VideoClip okinawaPreviewClip;

        private VisualElement _root;
        private Button _okinawaNode;
        private Button _tohokuNode;
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

        private Button _topbarSettings;
        private Button _topbarMap;
        private Button _topbarTechniques;
        private Button _topbarStats;
        private VisualElement _settingsModal;
        private Button _settingsClose;

        private VideoPlayer _okinawaPlayer;
        private RenderTexture _okinawaRenderTexture;

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;
        private Action _unbindSettingsModal;

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
                _okinawaNode.clicked -= OnOkinawaClicked;
                _tohokuNode.clicked -= OnTohokuClicked;
                _outsideCatcher.UnregisterCallback<PointerDownEvent>(OnOutsideCatcherPointerDown);
                _panelCta.clicked -= OnEnterChapterClicked;
                _topbarMap.clicked -= OnTopbarMapClicked;
                _topbarTechniques.clicked -= OnTopbarTechniquesClicked;
                _topbarStats.clicked -= OnTopbarStatsClicked;
                _unbindSettingsModal?.Invoke();
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

            _unbindSettingsModal = null;
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
            _tohokuNode = _root.Q<Button>("chapter-node-tohoku");
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

            _topbarSettings = _root.Q<Button>("map-topbar-settings");
            _topbarMap = _root.Q<Button>("map-topbar-map");
            _topbarTechniques = _root.Q<Button>("map-topbar-techniques");
            _topbarStats = _root.Q<Button>("map-topbar-stats");
            _settingsModal = _root.Q<VisualElement>("map-settings-modal");
            _settingsClose = _root.Q<Button>("map-settings-close");

            if (_okinawaNode == null || _tohokuNode == null || _panel == null || _panelCta == null)
            {
                Debug.LogError("[JapanMapController] Japan map elements missing; screen not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            _okinawaNode.clicked += OnOkinawaClicked;
            _tohokuNode.clicked += OnTohokuClicked;
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

            var audioSettings = GetComponent<IAudioSettings>();
            _unbindSettingsModal = MapSettingsModalBinder.Bind(
                _settingsModal, _topbarSettings, _settingsClose,
                _root.Q<Slider>("map-settings-music"), _root.Q<Slider>("map-settings-sfx"), _root.Q<Slider>("map-settings-trainer"),
                audioSettings);

            ResetToDefaultState();

            _bound = true;
            _bindRoutine = null;
        }

        private void OnOkinawaClicked() => ToggleChapter(OkinawaChapterId, _okinawaNode);

        private void OnTohokuClicked() => ToggleChapter(TohokuChapterId, _tohokuNode);

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
            else
                ShowTohokuPanel();

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

        private void ShowTohokuPanel()
        {
            StopOkinawaPreview();
            _panelVideo.style.display = DisplayStyle.None;
            HideFallback();

            _panelEyebrow.text = "CHAPTER 1";
            _panelTitle.text = "TOHOKU";
            _panelDesc.text = "Complete Okinawa to unlock the next chapter of your journey.";
            _panelMeta.text = string.Empty;
            _panelCtaText.text = "COMPLETE OKINAWA";
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
            else if (_selectedChapter == TohokuChapterId)
                _tohokuNode.RemoveFromClassList(SelectedNodeClass);
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

        private IEnumerator PlayTransitionThenEnterOkinawa()
        {
            _transitioning = true;
            _transitionOverlay?.AddToClassList(TransitionVisibleClass);

            yield return new WaitForSeconds(TransitionSeconds);

            _navigator?.Show(OkinawaChapterScreenId);
            _transitioning = false;
            _transitionRoutine = null;
        }

        private void OnTopbarMapClicked()
        {
            // Already on Japan: Show() is a safe no-op if nothing changed, but
            // the reset must still happen explicitly here since ScreenChanged
            // only fires on a genuine screen change.
            ResetToDefaultState();
            _navigator?.Show(ScreenId);
        }

        private void OnTopbarTechniquesClicked()
        {
            if (_progress != null && !TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))
                return;
            _navigator?.Show(TechniquesScreenId);
        }

        private void OnTopbarStatsClicked() => _navigator?.Show(StatsScreenId);

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
            MapSettingsModalBinder.Close(_settingsModal);
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
