using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Round-trip contract for the production <see cref="PlayerPrefsOkinawaProgressStorage"/>.
    /// Every test cleans up its own PlayerPrefs key in TearDown so this run never
    /// leaves residue in the real Editor/player local storage.
    /// </summary>
    public class PlayerPrefsOkinawaProgressStorageTests
    {
        private const string Key = "Mikey.OkinawaProgress.CompletedLevels";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(Key);
        }

        [Test]
        public void TryLoad_ReturnsFalse_WhenNothingWasEverSaved()
        {
            PlayerPrefs.DeleteKey(Key);
            var storage = new PlayerPrefsOkinawaProgressStorage();

            Assert.IsFalse(storage.TryLoad(out _));
        }

        [Test]
        public void Save_ThenTryLoad_RoundTrips()
        {
            var storage = new PlayerPrefsOkinawaProgressStorage();
            storage.Save("1,2");

            Assert.IsTrue(storage.TryLoad(out string value));
            Assert.AreEqual("1,2", value);
        }

        [Test]
        public void Delete_ClearsTheSavedValue()
        {
            var storage = new PlayerPrefsOkinawaProgressStorage();
            storage.Save("1");

            storage.Delete();

            Assert.IsFalse(storage.TryLoad(out _));
        }
    }
}
