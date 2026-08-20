using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Round-trip contract for the production <see cref="PlayerPrefsLevel0ProgressStorage"/>.
    /// Every test cleans up its own PlayerPrefs key in TearDown so this run never
    /// leaves residue in the real Editor/player local storage.
    /// </summary>
    public class PlayerPrefsLevel0ProgressStorageTests
    {
        private const string Key = "Mikey.Level0Progress.CompletedTests";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(Key);
        }

        [Test]
        public void TryLoad_ReturnsFalse_WhenNothingWasEverSaved()
        {
            PlayerPrefs.DeleteKey(Key);
            var storage = new PlayerPrefsLevel0ProgressStorage();

            Assert.IsFalse(storage.TryLoad(out _));
        }

        [Test]
        public void Save_ThenTryLoad_RoundTrips()
        {
            var storage = new PlayerPrefsLevel0ProgressStorage();
            storage.Save("CameraTest,PushUps");

            Assert.IsTrue(storage.TryLoad(out string value));
            Assert.AreEqual("CameraTest,PushUps", value);
        }

        [Test]
        public void Delete_ClearsTheSavedValue()
        {
            var storage = new PlayerPrefsLevel0ProgressStorage();
            storage.Save("CameraTest");

            storage.Delete();

            Assert.IsFalse(storage.TryLoad(out _));
        }
    }
}
