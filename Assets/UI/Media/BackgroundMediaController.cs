using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

/// <summary>
/// Drives full-bleed background media for screens that have one, keyed to
/// <see cref="ScreenManager.ScreenChanged"/> so at most one bound video plays
/// at a time: the newly active screen's video resumes (or starts preparing),
/// every other tracked video is paused. Screens whose binding carries a
/// <see cref="ScreenVideoBinding.staticImage"/> instead of a clip (e.g. the
/// Map) get a one-time texture assignment and no playback lifecycle. Screens
/// with no binding, or a binding with neither a clip nor an image, simply
/// keep their existing static USS background — that is the safe fallback if
/// a video asset is ever missing or fails to prepare.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class BackgroundMediaController : MonoBehaviour
{
    [Serializable]
    public struct ScreenVideoBinding
    {
        [Tooltip("ScreenManager screen id this binding applies to (e.g. 'title').")]
        public string screenId;

        [Tooltip("Name of the VisualElement (inside that screen's full-bleed background layer) that receives the media.")]
        public string targetElementName;

        [Tooltip("Looping background video for this screen. Leave empty to use a static image or the existing solid/glow fallback.")]
        public VideoClip clip;

        [Tooltip("Static background image (e.g. the Map). Ignored if a clip is set.")]
        public Texture2D staticImage;
    }

    [SerializeField] private ScreenVideoBinding[] bindings = Array.Empty<ScreenVideoBinding>();

    private ScreenManager _screenManager;
    private VisualElement _root;
    private readonly Dictionary<string, VisualElement> _targets = new Dictionary<string, VisualElement>();
    private readonly Dictionary<string, ScreenVideoBinding> _videoBindingsById = new Dictionary<string, ScreenVideoBinding>();
    private readonly Dictionary<string, VideoPlayer> _players = new Dictionary<string, VideoPlayer>();
    private readonly Dictionary<string, RenderTexture> _renderTextures = new Dictionary<string, RenderTexture>();
    private string _activeScreenId;

    private void OnEnable()
    {
        _screenManager = GetComponent<ScreenManager>();
        _root = GetComponent<UIDocument>().rootVisualElement;
        if (_root == null)
            return;

        _targets.Clear();
        _videoBindingsById.Clear();

        foreach (ScreenVideoBinding binding in bindings)
        {
            if (string.IsNullOrEmpty(binding.screenId) || string.IsNullOrEmpty(binding.targetElementName))
                continue;

            VisualElement element = _root.Q<VisualElement>(binding.targetElementName);
            if (element == null)
                continue;

            _targets[binding.screenId] = element;

            if (binding.clip != null)
            {
                _videoBindingsById[binding.screenId] = binding;
            }
            else if (binding.staticImage != null)
            {
                element.style.backgroundImage = new StyleBackground(binding.staticImage);
            }
        }

        if (_screenManager != null)
        {
            _screenManager.ScreenChanged += OnScreenChanged;
            if (!string.IsNullOrEmpty(_screenManager.CurrentScreen))
                OnScreenChanged(_screenManager.CurrentScreen);
        }
    }

    private void OnDisable()
    {
        if (_screenManager != null)
            _screenManager.ScreenChanged -= OnScreenChanged;

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

        _targets.Clear();
        _videoBindingsById.Clear();
        _activeScreenId = null;
    }

    private void OnScreenChanged(string screenId)
    {
        _activeScreenId = screenId;

        // Never let more than one tracked background video play at once.
        foreach (KeyValuePair<string, VideoPlayer> kvp in _players)
        {
            if (kvp.Key == screenId)
                continue;
            VideoPlayer other = kvp.Value;
            if (other != null && (other.isPlaying || other.isPaused))
                other.Pause();
        }

        if (!_videoBindingsById.TryGetValue(screenId, out ScreenVideoBinding binding))
            return;

        VideoPlayer player = GetOrCreatePlayer(screenId, binding);
        if (player.isPrepared)
        {
            ApplyTexture(screenId);
            player.Play();
        }
        else
        {
            player.Prepare();
        }
    }

    private VideoPlayer GetOrCreatePlayer(string screenId, ScreenVideoBinding binding)
    {
        if (_players.TryGetValue(screenId, out VideoPlayer existing) && existing != null)
            return existing;

        var rt = new RenderTexture((int)binding.clip.width, (int)binding.clip.height, 0)
        {
            name = $"BgVideoRT_{screenId}"
        };
        _renderTextures[screenId] = rt;

        var playerGo = new GameObject($"BgVideoPlayer_{screenId}");
        playerGo.transform.SetParent(transform, false);
        var player = playerGo.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.isLooping = true;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = rt;
        player.source = VideoSource.VideoClip;
        player.clip = binding.clip;
        // Background loops are muted unless an asset spec explicitly requires sound.
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.prepareCompleted += OnPrepareCompleted;
        player.errorReceived += OnErrorReceived;

        _players[screenId] = player;
        return player;
    }

    private void OnPrepareCompleted(VideoPlayer source)
    {
        foreach (KeyValuePair<string, VideoPlayer> kvp in _players)
        {
            if (kvp.Value != source)
                continue;

            // The user may have navigated away again while this clip was still
            // preparing; only auto-play if its screen is still the active one.
            if (kvp.Key == _activeScreenId)
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

            Debug.LogWarning($"[BackgroundMediaController] '{kvp.Key}' background video error: {message}. Falling back to the static background.");
            if (_targets.TryGetValue(kvp.Key, out VisualElement element))
                element.style.backgroundImage = StyleKeyword.Null;
            return;
        }
    }

    private void ApplyTexture(string screenId)
    {
        if (_targets.TryGetValue(screenId, out VisualElement element) && _renderTextures.TryGetValue(screenId, out RenderTexture rt))
            element.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
    }
}
