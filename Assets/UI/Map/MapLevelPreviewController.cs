using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.Audio;
using Mikey.UI.Progression;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives the mobile Map screen's LVL0/LVL1 hotspot markers, their shared
    /// detail panel (hidden by default; tapping a hotspot opens it, tapping the
    /// same hotspot again or tapping outside closes it), the top quick-access
    /// bar's progression-gated Techniques tab and Settings overlay, and the
    /// Level/XP readout. LVL0 (the Combine assessment) is always unlocked; LVL1
    /// is visible but its CTA stays locked until <see cref="TutorialProgressState.Level1Unlocked"/>
    /// (mirrors the Techniques tab's own gate — both reuse
    /// <see cref="TutorialProgressPresenter.IsTechniquesUnlocked"/>). Each
    /// checkpoint's own content block and Start CTA are pre-authored in UXML
    /// (mirrors Combine's loading/empty/ready/error state-switching); this
    /// controller only toggles which one is visible. Pan/zoom is a separate,
    /// independent concern owned by <see cref="MapPanZoomController"/>. Formerly
    /// the single always-open Okinawa preview panel; that MVP-only design is
    /// retired with this rebuild.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapLevelPreviewController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller reacts to (leaving it pauses preview playback and resets the panel).</summary>
        public const string ScreenId = "map";

        /// <summary>Where the top bar's STATS tab navigates (frontend mock data lives on the existing Profile screen).</summary>
        public const string StatsTarget = "profile";

        /// <summary>Where the top bar's TECHNIQUES tab navigates once unlocked.</summary>
        public const string TechniquesTarget = "techniques";

        private const string SelectedNodeClass = "map-node--selected";
        private const string LockedNodeClass = "map-node--locked";
        private const string LockedCtaClass = "map-detail__cta--locked";
        private const string LockedTabClass = "map-topbar__tab--locked";
        private const string PanelOpenClass = "map-detail--open";

        [Serializable]
        public struct CheckpointBinding
        {
            [Tooltip("Name of the checkpoint marker Button on the Map canvas (e.g. 'map-node-lvl0').")]
            public string nodeElementName;

            [Tooltip("Name of this checkpoint's content block inside the detail panel (e.g. 'map-detail-content-lvl0').")]
            public string contentElementName;

            [Tooltip("Name of this checkpoint's Start CTA Button inside its content block.")]
            public string ctaElementName;

            [Tooltip("Screen id this checkpoint's CTA navigates to once unlocked.")]
            public string navigationTarget;

            [Tooltip("Minimum tutorial progression state required to unlock this checkpoint's CTA. NewPlayer means always unlocked.")]
            public TutorialProgressState requiredState;

            [Tooltip("Optional inline looping preview clip shown in the detail panel for this checkpoint.")]
            public VideoClip previewClip;
        }

        [SerializeField] private CheckpointBinding[] checkpoints = Array.Empty<CheckpointBinding>();

        private VisualElement _root;
        private VisualElement _mapScreen;
        private VisualElement _detailPanel;
        private VisualElement _videoTarget;
        private VisualElement _videoFallback;
        private Button _techniquesTab;
        private Button _settingsOpenButton;
        private Button _settingsCloseButton;
        private VisualElement _settingsModal;
        private Label _levelLabel;
        private Slider _musicSlider;
        private Slider _sfxSlider;
        private Slider _trainerVoiceSlider;

        private readonly Dictionary<string, Button> _checkpointButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, VisualElement> _checkpointContent = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Button> _checkpointCtas = new Dictionary<string, Button>();
        private readonly Dictionary<string, EventCallback<ClickEvent>> _checkpointClickCallbacks = new Dictionary<string, EventCallback<ClickEvent>>();
        private readonly List<ActionBinding> _actionBindings = new List<ActionBinding>();
        private readonly Dictionary<string, VideoPlayer> _players = new Dictionary<string, VideoPlayer>();
        private readonly Dictionary<string, RenderTexture> _renderTextures = new Dictionary<string, RenderTexture>();

        private IScreenNavigator _navigator;
        private ITutorialProgress _progress;
        private IAudioSettings _audioSettings;
        private EventCallback<PointerDownEvent> _outsideClickCallback;
        private EventCallback<ChangeEvent<float>> _musicChangedCallback;
        private EventCallback<ChangeEvent<float>> _sfxChangedCallback;
        private EventCallback<ChangeEvent<float>> _trainerVoiceChangedCallback;

        private string _selectedNodeName;
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
                foreach (KeyValuePair<string, EventCallback<ClickEvent>> kvp in _checkpointClickCallbacks)
                {
                    if (_checkpointButtons.TryGetValue(kvp.Key, out Button node))
                        node.UnregisterCallback(kvp.Value);
                }
                _checkpointClickCallbacks.Clear();

                for (int i = 0; i < _actionBindings.Count; i++)
                    _actionBindings[i].Unbind();
                _actionBindings.Clear();

                if (_mapScreen != null && _outsideClickCallback != null)
                    _mapScreen.UnregisterCallback(_outsideClickCallback);

                if (_musicSlider != null && _musicChangedCallback != null)
                    _musicSlider.UnregisterValueChangedCallback(_musicChangedCallback);
                if (_sfxSlider != null && _sfxChangedCallback != null)
                    _sfxSlider.UnregisterValueChangedCallback(_sfxChangedCallback);
                if (_trainerVoiceSlider != null && _trainerVoiceChangedCallback != null)
                    _trainerVoiceSlider.UnregisterValueChangedCallback(_trainerVoiceChangedCallback);
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            if (_progress != null)
            {
                _progress.Changed -= RefreshProgressionUi;
                _progress = null;
            }

            foreach (VideoPlayer player in _players.Values)
            {
                if (player == null)
                    continue;
                player.prepareCompleted -= OnPrepareCompleted;
                player.errorReceived -= OnErrorReceived;
                player.Stop();
                Destroy(player.gameObject);
            }
            _players.Clear();

            foreach (RenderTexture rt in _renderTextures.Values)
            {
                if (rt == null)
                    continue;
                rt.Release();
                Destroy(rt);
            }
            _renderTextures.Clear();

            _checkpointButtons.Clear();
            _checkpointContent.Clear();
            _checkpointCtas.Clear();
            _root = null;
            _mapScreen = null;
            _detailPanel = null;
            _videoTarget = null;
            _videoFallback = null;
            _techniquesTab = null;
            _settingsOpenButton = null;
            _settingsCloseButton = null;
            _settingsModal = null;
            _levelLabel = null;
            _musicSlider = null;
            _sfxSlider = null;
            _trainerVoiceSlider = null;
            _selectedNodeName = null;
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
                    Debug.LogError("[MapLevelPreviewController] UIDocument root unavailable; Map not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;
            _mapScreen = _root.Q<VisualElement>(ScreenId);
            _detailPanel = _root.Q<VisualElement>("map-detail");
            _videoTarget = _root.Q<VisualElement>("map-detail-video");
            _videoFallback = _root.Q<VisualElement>("map-detail-video-fallback");
            _techniquesTab = _root.Q<Button>("map-topbar-techniques");
            _settingsOpenButton = _root.Q<Button>("map-settings-open");
            _settingsCloseButton = _root.Q<Button>("map-settings-close");
            _settingsModal = _root.Q<VisualElement>("map-settings-modal");
            _levelLabel = _root.Q<Label>("map-topbar-level");
            _musicSlider = _root.Q<Slider>("map-settings-music");
            _sfxSlider = _root.Q<Slider>("map-settings-sfx");
            _trainerVoiceSlider = _root.Q<Slider>("map-settings-trainer");

            if (_mapScreen == null || _detailPanel == null || _videoTarget == null)
            {
                Debug.LogError("[MapLevelPreviewController] Map screen/panel elements missing; not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            foreach (CheckpointBinding binding in checkpoints)
            {
                if (string.IsNullOrEmpty(binding.nodeElementName))
                    continue;

                Button node = _root.Q<Button>(binding.nodeElementName);
                VisualElement content = _root.Q<VisualElement>(binding.contentElementName);
                Button cta = _root.Q<Button>(binding.ctaElementName);
                if (node == null || content == null || cta == null)
                    continue;

                _checkpointButtons[binding.nodeElementName] = node;
                _checkpointContent[binding.nodeElementName] = content;
                _checkpointCtas[binding.nodeElementName] = cta;

                string nodeName = binding.nodeElementName;
                EventCallback<ClickEvent> nodeCallback = _ => SelectCheckpoint(nodeName);
                _checkpointClickCallbacks[nodeName] = nodeCallback;
                node.RegisterCallback(nodeCallback);

                CheckpointBinding capturedBinding = binding;
                Action ctaAction = () => StartCheckpoint(capturedBinding);
                cta.clicked += ctaAction;
                _actionBindings.Add(new ActionBinding(cta, ctaAction));

                content.style.display = DisplayStyle.None;
            }

            BindAction(_techniquesTab, OnTechniquesTabClicked);
            BindAction(_settingsOpenButton, () => SetModalOpen(_settingsModal, true));
            BindAction(_settingsCloseButton, () => SetModalOpen(_settingsModal, false));

            _outsideClickCallback = OnScreenPointerDown;
            _mapScreen.RegisterCallback(_outsideClickCallback);

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            _audioSettings = GetComponent<IAudioSettings>();
            WireSlider(_musicSlider, _audioSettings, (s, v) => s.MusicVolume = v, s => s.MusicVolume, cb => _musicChangedCallback = cb);
            WireSlider(_sfxSlider, _audioSettings, (s, v) => s.SfxVolume = v, s => s.SfxVolume, cb => _sfxChangedCallback = cb);
            WireSlider(_trainerVoiceSlider, _audioSettings, (s, v) => s.TrainerVoiceVolume = v, s => s.TrainerVoiceVolume, cb => _trainerVoiceChangedCallback = cb);

            _progress = GetComponent<ITutorialProgress>();
            if (_progress != null)
                _progress.Changed += RefreshProgressionUi;

            ClosePanel();
            SetModalOpen(_settingsModal, false);
            RefreshProgressionUi();

            _bound = true;
            _bindRoutine = null;
        }

        private void BindAction(Button button, Action onClick)
        {
            if (button == null)
                return;
            button.clicked += onClick;
            _actionBindings.Add(new ActionBinding(button, onClick));
        }

        private static void WireSlider(
            Slider slider,
            IAudioSettings settings,
            Action<IAudioSettings, float> apply,
            Func<IAudioSettings, float> read,
            Action<EventCallback<ChangeEvent<float>>> storeCallback)
        {
            if (slider == null || settings == null)
                return;

            slider.value = read(settings);
            EventCallback<ChangeEvent<float>> callback = evt => apply(settings, evt.newValue);
            slider.RegisterValueChangedCallback(callback);
            storeCallback(callback);
        }

        /// <summary>
        /// Opens the panel for the given checkpoint; tapping the ALREADY-selected
        /// checkpoint again closes it instead (toggle). Switches which pre-authored
        /// content block is visible and starts/resumes its inline preview (or the
        /// safe fallback if none is bound).
        /// </summary>
        public void SelectCheckpoint(string nodeElementName)
        {
            if (!_checkpointButtons.ContainsKey(nodeElementName))
                return;

            if (_selectedNodeName == nodeElementName)
            {
                ClosePanel();
                return;
            }

            if (_selectedNodeName != null)
            {
                if (_checkpointButtons.TryGetValue(_selectedNodeName, out Button previousNode))
                    previousNode.RemoveFromClassList(SelectedNodeClass);
                if (_checkpointContent.TryGetValue(_selectedNodeName, out VisualElement previousContent))
                    previousContent.style.display = DisplayStyle.None;
            }

            _selectedNodeName = nodeElementName;
            _checkpointButtons[nodeElementName].AddToClassList(SelectedNodeClass);
            _checkpointContent[nodeElementName].style.display = DisplayStyle.Flex;
            _detailPanel?.AddToClassList(PanelOpenClass);

            if (!TryFindBinding(nodeElementName, out CheckpointBinding binding) || binding.previewClip == null)
            {
                ShowFallback();
                return;
            }

            HideFallback();
            VideoPlayer player = GetOrCreatePlayer(nodeElementName, binding);
            if (player.isPrepared)
            {
                ApplyTexture(nodeElementName);
                player.Play();
            }
            else
            {
                player.Prepare();
            }
        }

        /// <summary>Hides the panel (its default state on Map entry), pauses the active preview and deselects the checkpoint node.</summary>
        public void ClosePanel()
        {
            _detailPanel?.RemoveFromClassList(PanelOpenClass);
            PausePlayback();

            if (_selectedNodeName != null)
            {
                if (_checkpointButtons.TryGetValue(_selectedNodeName, out Button node))
                    node.RemoveFromClassList(SelectedNodeClass);
                if (_checkpointContent.TryGetValue(_selectedNodeName, out VisualElement content))
                    content.style.display = DisplayStyle.None;
            }
            _selectedNodeName = null;
        }

        /// <summary>Routes a checkpoint's CTA through the existing navigator, unless it's still locked (safe no-op — the CTA is also visibly disabled).</summary>
        private void StartCheckpoint(CheckpointBinding binding)
        {
            if (_progress != null && _progress.State < binding.requiredState)
                return;
            _navigator?.Show(binding.navigationTarget);
        }

        private void OnTechniquesTabClicked()
        {
            if (_progress != null && !TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))
                return;
            _navigator?.Show(TechniquesTarget);
        }

        private static void SetModalOpen(VisualElement modal, bool open)
        {
            if (modal != null)
                modal.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Closes the panel when a tap lands outside it and outside every hotspot (which handle their own taps via SelectCheckpoint).</summary>
        private void OnScreenPointerDown(PointerDownEvent evt)
        {
            if (_selectedNodeName == null)
                return;

            var target = evt.target as VisualElement;
            if (target == null)
                return;

            if (_detailPanel != null && IsSelfOrDescendant(_detailPanel, target))
                return;

            foreach (Button hotspot in _checkpointButtons.Values)
            {
                if (IsSelfOrDescendant(hotspot, target))
                    return;
            }

            ClosePanel();
        }

        private static bool IsSelfOrDescendant(VisualElement ancestor, VisualElement node)
        {
            for (VisualElement p = node; p != null; p = p.parent)
            {
                if (p == ancestor)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Refreshes every progression-gated affordance: each checkpoint's
        /// hotspot dimming + CTA lock, the top bar's Techniques tab lock, and the
        /// Level readout. Run on bind, on entering the Map screen, and whenever
        /// progression state changes.
        /// </summary>
        private void RefreshProgressionUi()
        {
            TutorialProgressState state = _progress?.State ?? TutorialProgressState.NewPlayer;

            foreach (CheckpointBinding binding in checkpoints)
            {
                bool unlocked = state >= binding.requiredState;

                if (_checkpointButtons.TryGetValue(binding.nodeElementName, out Button node))
                {
                    if (unlocked)
                        node.RemoveFromClassList(LockedNodeClass);
                    else
                        node.AddToClassList(LockedNodeClass);
                }

                if (_checkpointCtas.TryGetValue(binding.nodeElementName, out Button cta))
                {
                    cta.SetEnabled(unlocked);
                    if (unlocked)
                        cta.RemoveFromClassList(LockedCtaClass);
                    else
                        cta.AddToClassList(LockedCtaClass);
                }
            }

            bool techniquesUnlocked = TutorialProgressPresenter.IsTechniquesUnlocked(state);
            if (_techniquesTab != null)
            {
                _techniquesTab.SetEnabled(techniquesUnlocked);
                if (techniquesUnlocked)
                    _techniquesTab.RemoveFromClassList(LockedTabClass);
                else
                    _techniquesTab.AddToClassList(LockedTabClass);
            }

            if (_levelLabel != null)
            {
                int level = state >= TutorialProgressState.Level1Unlocked ? 1 : 0;
                _levelLabel.text = $"LEVEL {level}";
            }
        }

        /// <summary>Entering Map always starts with the panel and Settings overlay closed (their safe default state).</summary>
        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
            {
                ClosePanel();
                SetModalOpen(_settingsModal, false);
                RefreshProgressionUi();
            }
            else
            {
                PausePlayback();
            }
        }

        private void PausePlayback()
        {
            if (_selectedNodeName == null)
                return;
            if (_players.TryGetValue(_selectedNodeName, out VideoPlayer player) && player != null && (player.isPlaying || player.isPaused))
                player.Pause();
        }

        private bool TryFindBinding(string nodeElementName, out CheckpointBinding binding)
        {
            foreach (CheckpointBinding candidate in checkpoints)
            {
                if (candidate.nodeElementName == nodeElementName)
                {
                    binding = candidate;
                    return true;
                }
            }
            binding = default;
            return false;
        }

        private VideoPlayer GetOrCreatePlayer(string nodeElementName, CheckpointBinding binding)
        {
            if (_players.TryGetValue(nodeElementName, out VideoPlayer existing) && existing != null)
                return existing;

            var rt = new RenderTexture((int)binding.previewClip.width, (int)binding.previewClip.height, 0)
            {
                name = $"MapPreviewRT_{nodeElementName}"
            };
            _renderTextures[nodeElementName] = rt;

            var playerGo = new GameObject($"MapPreviewVideoPlayer_{nodeElementName}");
            playerGo.transform.SetParent(transform, false);
            var player = playerGo.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = rt;
            player.source = VideoSource.VideoClip;
            player.clip = binding.previewClip;
            // Inline level previews are muted unless an asset spec explicitly requires sound.
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.prepareCompleted += OnPrepareCompleted;
            player.errorReceived += OnErrorReceived;

            _players[nodeElementName] = player;
            return player;
        }

        private void OnPrepareCompleted(VideoPlayer source)
        {
            foreach (KeyValuePair<string, VideoPlayer> kvp in _players)
            {
                if (kvp.Value != source)
                    continue;

                // The user may have deselected this checkpoint while it was still
                // preparing; only auto-play if it's still the active selection.
                if (kvp.Key == _selectedNodeName)
                {
                    ApplyTexture(kvp.Key);
                    source.Play();
                }
                return;
            }
        }

        private void OnErrorReceived(VideoPlayer source, string message)
        {
            foreach (KeyValuePair<string, VideoPlayer> kvp in _players)
            {
                if (kvp.Value != source)
                    continue;

                Debug.LogWarning($"[MapLevelPreviewController] '{kvp.Key}' preview video error: {message}. Falling back to the static placeholder.");
                if (kvp.Key == _selectedNodeName)
                    ShowFallback();
                return;
            }
        }

        private void ApplyTexture(string nodeElementName)
        {
            if (_videoTarget != null && _renderTextures.TryGetValue(nodeElementName, out RenderTexture rt))
                _videoTarget.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
        }

        private void ShowFallback()
        {
            if (_videoTarget != null)
                _videoTarget.style.backgroundImage = StyleKeyword.Null;
            _videoFallback?.AddToClassList("map-detail__video-fallback--visible");
        }

        private void HideFallback()
        {
            _videoFallback?.RemoveFromClassList("map-detail__video-fallback--visible");
        }

        private readonly struct ActionBinding
        {
            private readonly Button _button;
            private readonly Action _callback;

            public ActionBinding(Button button, Action callback)
            {
                _button = button;
                _callback = callback;
            }

            public void Unbind()
            {
                if (_button != null && _callback != null)
                    _button.clicked -= _callback;
            }
        }
    }
}
