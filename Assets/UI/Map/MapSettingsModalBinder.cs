using System;
using Mikey.UI.Audio;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Wires a Map screen's minimal local Settings modal (Music/SFX/Trainer
    /// Voice sliders, see Map.uss ".map-settings-modal") directly to the
    /// shared <see cref="IAudioSettings"/> instance — a small, decoupled
    /// duplicate of Home's Settings overlay markup, not a reuse of
    /// HomeController itself. Shared by JapanMapController and
    /// OkinawaMapController so this ~20-line binding isn't written a third
    /// time. Mirrors HomeController's WireSlider pattern.
    /// </summary>
    internal static class MapSettingsModalBinder
    {
        private const string OpenClass = "map-settings-modal--open";

        /// <summary>Wires open/close + the three sliders. Returns an unbind action to call from the owning controller's OnDisable.</summary>
        public static Action Bind(
            VisualElement modal,
            Button openButton,
            Button closeButton,
            Slider musicSlider,
            Slider sfxSlider,
            Slider trainerSlider,
            IAudioSettings settings)
        {
            if (modal == null)
                return () => { };

            Action openHandler = () => modal.AddToClassList(OpenClass);
            Action closeHandler = () => modal.RemoveFromClassList(OpenClass);
            if (openButton != null)
                openButton.clicked += openHandler;
            if (closeButton != null)
                closeButton.clicked += closeHandler;

            EventCallback<ChangeEvent<float>> musicCallback = null;
            EventCallback<ChangeEvent<float>> sfxCallback = null;
            EventCallback<ChangeEvent<float>> trainerCallback = null;
            if (settings != null)
            {
                musicCallback = WireSlider(musicSlider, settings, (s, v) => s.MusicVolume = v, s => s.MusicVolume);
                sfxCallback = WireSlider(sfxSlider, settings, (s, v) => s.SfxVolume = v, s => s.SfxVolume);
                trainerCallback = WireSlider(trainerSlider, settings, (s, v) => s.TrainerVoiceVolume = v, s => s.TrainerVoiceVolume);
            }

            return () =>
            {
                if (openButton != null)
                    openButton.clicked -= openHandler;
                if (closeButton != null)
                    closeButton.clicked -= closeHandler;
                if (musicSlider != null && musicCallback != null)
                    musicSlider.UnregisterValueChangedCallback(musicCallback);
                if (sfxSlider != null && sfxCallback != null)
                    sfxSlider.UnregisterValueChangedCallback(sfxCallback);
                if (trainerSlider != null && trainerCallback != null)
                    trainerSlider.UnregisterValueChangedCallback(trainerCallback);
            };
        }

        /// <summary>Closes the modal (used when leaving the owning screen).</summary>
        public static void Close(VisualElement modal) => modal?.RemoveFromClassList(OpenClass);

        private static EventCallback<ChangeEvent<float>> WireSlider(
            Slider slider,
            IAudioSettings settings,
            Action<IAudioSettings, float> apply,
            Func<IAudioSettings, float> read)
        {
            if (slider == null)
                return null;

            slider.value = read(settings);
            EventCallback<ChangeEvent<float>> callback = evt => apply(settings, evt.newValue);
            slider.RegisterValueChangedCallback(callback);
            return callback;
        }
    }
}
