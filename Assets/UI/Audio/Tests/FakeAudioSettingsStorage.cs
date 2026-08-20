using System.Collections.Generic;

namespace Mikey.UI.Audio.Tests
{
    /// <summary>In-memory <see cref="IAudioSettingsStorage"/> so store tests never touch real local storage.</summary>
    public sealed class FakeAudioSettingsStorage : IAudioSettingsStorage
    {
        private readonly Dictionary<string, float> _values = new Dictionary<string, float>();

        /// <summary>Seeds a raw saved value directly (including invalid/garbage), bypassing Save.</summary>
        public void Seed(string key, float rawValue) => _values[key] = rawValue;

        public bool TryLoad(string key, out float value) => _values.TryGetValue(key, out value);

        public void Save(string key, float value) => _values[key] = value;
    }
}
