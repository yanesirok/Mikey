namespace Mikey.UI.Audio
{
    /// <summary>
    /// Minimal persistence abstraction behind <see cref="AudioSettingsStore"/> so its
    /// defaults/clamping logic can be unit-tested with an in-memory fake, without
    /// touching real Editor/player local storage from a test run. Mirrors
    /// <see cref="Mikey.UI.Progression.ITutorialProgressStorage"/>'s shape, generalized
    /// to a named float per volume instead of a single value.
    /// </summary>
    public interface IAudioSettingsStorage
    {
        /// <summary>Attempts to read the last saved raw value for <paramref name="key"/>. Returns false if none was ever saved.</summary>
        bool TryLoad(string key, out float value);

        /// <summary>Persists <paramref name="value"/> under <paramref name="key"/>, surviving an app/Editor restart.</summary>
        void Save(string key, float value);
    }
}
