namespace Mikey.UI.Settings
{
    /// <summary>
    /// Minimal persistence abstraction behind <see cref="MotionSettingsStore"/> so its
    /// load/default logic can be unit-tested with an in-memory fake, without touching
    /// real Editor/player local storage from a test run. Mirrors
    /// <see cref="Mikey.UI.Audio.IAudioSettingsStorage"/>, generalized to a single bool
    /// instead of a float per key.
    /// </summary>
    public interface IMotionSettingsStorage
    {
        /// <summary>Attempts to read the last saved raw value for <paramref name="key"/>. Returns false if none was ever saved.</summary>
        bool TryLoad(string key, out bool value);

        /// <summary>Persists <paramref name="value"/> under <paramref name="key"/>, surviving an app/Editor restart.</summary>
        void Save(string key, bool value);
    }
}
