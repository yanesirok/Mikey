using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Audio.Tests
{
    /// <summary>
    /// Round-trip contract for the production <see cref="PlayerPrefsAudioSettingsStorage"/>.
    /// Every test cleans up its own PlayerPrefs key in TearDown so this run never
    /// leaves residue in the real Editor/player local storage.
    /// </summary>
    public class PlayerPrefsAudioSettingsStorageTests
    {
        private const string Key = "Mikey.Audio.MusicVolume";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(Key);
        }

        [Test]
        public void TryLoad_ReturnsFalse_WhenNothingWasEverSaved()
        {
            PlayerPrefs.DeleteKey(Key);
            var storage = new PlayerPrefsAudioSettingsStorage();

            Assert.IsFalse(storage.TryLoad(Key, out _));
        }

        [Test]
        public void Save_ThenTryLoad_RoundTrips()
        {
            var storage = new PlayerPrefsAudioSettingsStorage();
            storage.Save(Key, 0.42f);

            Assert.IsTrue(storage.TryLoad(Key, out float value));
            Assert.AreEqual(0.42f, value);
        }
    }
}
