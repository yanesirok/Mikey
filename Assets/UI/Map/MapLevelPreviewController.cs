using System;
using System.Collections;
using System.Collections.Generic;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Drives the Map screen's right-side Okinawa level-detail panel. MVP: Okinawa
    /// is the only playable destination, so entering the "map" screen automatically
    /// selects it (opens the panel, marks it selected, starts its inline looping
    /// preview video) without requiring a click — the transparent hotspot remains
    /// for re-selection but isn't required. "START LESSON" routes through the
    /// existing <see cref="IScreenNavigator"/> to the Techniques hub, and leaving
    /// the Map screen pauses playback so no inline preview keeps running
    /// off-screen. Deliberately separate from BackgroundMediaController — that
    /// drives full-bleed screen backgrounds (the Map screen's own full-bleed
    /// background media is disabled in Map.uss now that the flattened map artwork
    /// is the only visible map image); this only owns the small in-panel preview,
    /// never a full-screen video. Coexists with ScreenManager and
    /// BackgroundMediaController on the shared UI GameObject, mirroring
    /// PracticeController's bind/entry/unsubscribe pattern.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapLevelPreviewController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        /// <summary>The screen id this controller reacts to (leaving it pauses preview playback).</summary>
        public const string ScreenId = "map";

        /// <summary>Where "START LESSON" navigates today (an existing production screen).</summary>
        public const string StartLessonTarget = "techniques";

        private const string OpenClass = "map-detail--open";
        private const string SelectedNodeClass = "map-node--selected";
        private const string FallbackVisibleClass = "map-detail__video-fallback--visible";

        [Serializable]
        public struct CheckpointPreviewBinding
        {
            [Tooltip("Name of the checkpoint node Button on the Map screen (e.g. 'map-node-okinawa').")]
            public string nodeElementName;

            [Tooltip("Inline looping preview clip shown in the detail panel for this checkpoint.")]
            public VideoClip previewClip;
        }

        [SerializeField] private CheckpointPreviewBinding[] checkpoints = Array.Empty<CheckpointPreviewBinding>();

        private VisualElement _root;
        private VisualElement _detailPanel;
        private VisualElement _videoTarget;
        private VisualElement _videoFallback;
        private Button _closeButton;
        private Button _startButton;

        private readonly Dictionary<string, Button> _checkpointButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Action> _checkpointClickHandlers = new Dictionary<string, Action>();
        private readonly Dictionary<string, VideoPlayer> _players = new Dictionary<string, VideoPlayer>();
        private readonly Dictionary<string, RenderTexture> _renderTextures = new Dictionary<string, RenderTexture>();

        private IScreenNavigator _navigator;
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
                foreach (KeyValuePair<string, Button> kvp in _checkpointButtons)
                {
                    if (_checkpointClickHandlers.TryGetValue(kvp.Key, out Action handler))
                        kvp.Value.clicked -= handler;
                }
                _checkpointClickHandlers.Clear();

                if (_closeButton != null)
                    _closeButton.clicked -= ClosePanel;
                if (_startButton != null)
                    _startButton.clicked -= StartLesson;
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
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
            _root = null;
            _detailPanel = null;
            _videoTarget = null;
            _videoFallback = null;
            _closeButton = null;
            _startButton = null;
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
                    Debug.LogError("[MapLevelPreviewController] UIDocument root unavailable; Map level panel not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;

            _detailPanel = _root.Q<VisualElement>("map-detail");
            _videoTarget = _root.Q<VisualElement>("map-detail-video");
            _videoFallback = _root.Q<VisualElement>("map-detail-video-fallback");
            _closeButton = _root.Q<Button>("map-detail-close");
            _startButton = _root.Q<Button>("map-detail-start");

            if (_detailPanel == null || _videoTarget == null || _startButton == null)
            {
                Debug.LogError("[MapLevelPreviewController] Map detail panel elements missing; panel not bound.", this);
                _bindRoutine = null;
                yield break;
            }

            foreach (CheckpointPreviewBinding binding in checkpoints)
            {
                if (string.IsNullOrEmpty(binding.nodeElementName))
                    continue;

                Button node = _root.Q<Button>(binding.nodeElementName);
                if (node == null)
                    continue;

                _checkpointButtons[binding.nodeElementName] = node;

                string nodeName = binding.nodeElementName;
                Action handler = () => SelectCheckpoint(nodeName);
                _checkpointClickHandlers[nodeName] = handler;
                node.clicked += handler;
            }

            if (_closeButton != null)
                _closeButton.clicked += ClosePanel;
            _startButton.clicked += StartLesson;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                // If Map is already the active screen when binding finishes,
                // treat it as an entry so Okinawa is selected immediately
                // (mirrors BackgroundMediaController's same defensive check).
                if (_navigator.CurrentScreen == ScreenId)
                    SelectDefaultCheckpoint();
            }

            _bound = true;
            _bindRoutine = null;
        }

        /// <summary>
        /// Opens the detail panel for the given checkpoint node, marks it the active
        /// (red) checkpoint, and starts/resumes its inline preview. Falls back to the
        /// safe static "preview unavailable" state if no clip is bound for it (the
        /// same fallback a runtime video error also lands on).
        /// </summary>
        public void SelectCheckpoint(string nodeElementName)
        {
            if (!TryFindBinding(nodeElementName, out CheckpointPreviewBinding binding))
                return;

            if (_selectedNodeName != nodeElementName)
            {
                if (_selectedNodeName != null && _checkpointButtons.TryGetValue(_selectedNodeName, out Button previous))
                    previous.RemoveFromClassList(SelectedNodeClass);

                _selectedNodeName = nodeElementName;
                if (_checkpointButtons.TryGetValue(nodeElementName, out Button current))
                    current.AddToClassList(SelectedNodeClass);
            }

            _detailPanel?.AddToClassList(OpenClass);

            if (binding.previewClip == null)
            {
                ShowFallback(nodeElementName);
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

        /// <summary>Hides the panel, pauses the active preview and deselects the checkpoint node.</summary>
        public void ClosePanel()
        {
            _detailPanel?.RemoveFromClassList(OpenClass);
            PausePlayback();

            if (_selectedNodeName != null && _checkpointButtons.TryGetValue(_selectedNodeName, out Button node))
                node.RemoveFromClassList(SelectedNodeClass);
            _selectedNodeName = null;
        }

        /// <summary>Routes "START LESSON" through the existing screen navigator.</summary>
        public void StartLesson() => _navigator?.Show(StartLessonTarget);

        /// <summary>
        /// Entering the Map screen auto-selects the default checkpoint (Okinawa)
        /// so the approved 61.7/38.3 selected-level layout is shown immediately,
        /// on first entry and on every return visit. Leaving the Map screen pauses
        /// the active inline preview so no hidden video keeps playing behind
        /// another screen.
        /// </summary>
        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
                SelectDefaultCheckpoint();
            else
                PausePlayback();
        }

        /// <summary>
        /// MVP: Okinawa is the only playable destination, so it's also the default
        /// (first) checkpoint binding — selecting it opens the panel and starts its
        /// preview without requiring the user to click the transparent hotspot.
        /// </summary>
        private void SelectDefaultCheckpoint()
        {
            if (checkpoints.Length == 0)
                return;
            SelectCheckpoint(checkpoints[0].nodeElementName);
        }

        private void PausePlayback()
        {
            if (_selectedNodeName == null)
                return;
            if (_players.TryGetValue(_selectedNodeName, out VideoPlayer player) && player != null && (player.isPlaying || player.isPaused))
                player.Pause();
        }

        private bool TryFindBinding(string nodeElementName, out CheckpointPreviewBinding binding)
        {
            foreach (CheckpointPreviewBinding candidate in checkpoints)
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

        private VideoPlayer GetOrCreatePlayer(string nodeElementName, CheckpointPreviewBinding binding)
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
                    ShowFallback(kvp.Key);
                return;
            }
        }

        private void ApplyTexture(string nodeElementName)
        {
            if (_videoTarget != null && _renderTextures.TryGetValue(nodeElementName, out RenderTexture rt))
                _videoTarget.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
        }

        private void ShowFallback(string nodeElementName)
        {
            if (_videoTarget != null)
                _videoTarget.style.backgroundImage = StyleKeyword.Null;
            _videoFallback?.AddToClassList(FallbackVisibleClass);
        }

        private void HideFallback()
        {
            _videoFallback?.RemoveFromClassList(FallbackVisibleClass);
        }
    }
}
