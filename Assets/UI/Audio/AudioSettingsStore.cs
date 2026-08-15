using System;

namespace Mikey.UI.Audio
{
    /// <summary>
    /// Pure volume-storage logic for <see cref="IAudioSettings"/>, kept free of
    /// MonoBehaviour so it can be exercised directly in EditMode tests (mirrors
    /// <see cref="Mikey.UI.Progression.TutorialProgressStore"/>). Every value is
    /// clamped to 0..1 and falls back to its safe default when nothing was ever
    /// saved, or the saved value is invalid (NaN/Infinity) — never throws. The
    /// concrete <see cref="IAudioSettingsStorage"/> is injected so tests can use an
    /// in-memory fake instead of real local storage.
    /// </summary>
    public sealed class AudioSettingsStore : IAudioSettings
    {
        public const float DefaultMusicVolume = 0.70f;
        public const float DefaultSfxVolume = 1.00f;
        public const float DefaultTrainerVoiceVolume = 1.00f;

        private const string MusicKey = "Mikey.Audio.MusicVolume";
        private const string SfxKey = "Mikey.Audio.SfxVolume";
        private const string TrainerVoiceKey = "Mikey.Audio.TrainerVoiceVolume";

        private readonly IAudioSettingsStorage _storage;

        private float _musicVolume;
        private float _sfxVolume;
        private float _trainerVoiceVolume;

        public event Action Changed;

        public AudioSettingsStore(IAudioSettingsStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _musicVolume = LoadOrDefault(MusicKey, DefaultMusicVolume);
            _sfxVolume = LoadOrDefault(SfxKey, DefaultSfxVolume);
            _trainerVoiceVolume = LoadOrDefault(TrainerVoiceKey, DefaultTrainerVoiceVolume);
        }

        public float MusicVolume
        {
            get => _musicVolume;
            set => SetVolume(ref _musicVolume, value, MusicKey);
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set => SetVolume(ref _sfxVolume, value, SfxKey);
        }

        public float TrainerVoiceVolume
        {
            get => _trainerVoiceVolume;
            set => SetVolume(ref _trainerVoiceVolume, value, TrainerVoiceKey);
        }

        private float LoadOrDefault(string key, float defaultValue)
        {
            if (_storage.TryLoad(key, out float value) && IsFinite(value))
                return Clamp01(value);
            return defaultValue;
        }

        private void SetVolume(ref float field, float value, string key)
        {
            float clamped = IsFinite(value) ? Clamp01(value) : 0f;
            if (field == clamped)
                return;

            field = clamped;
            _storage.Save(key, clamped);
            Changed?.Invoke();
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
