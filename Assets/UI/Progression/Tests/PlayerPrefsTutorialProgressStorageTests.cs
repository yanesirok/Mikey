using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Round-trip contract for the production <see cref="PlayerPrefsTutorialProgressStorage"/>.
    /// Every test cleans up its own PlayerPrefs key in TearDown so this run never
    /// leaves residue in the real Editor/player local storage.
    /// </summary>
    public class PlayerPrefsTutorialProgressStorageTests
    {
        private const string Key = "Mikey.TutorialProgress.State";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(Key);
        }

        [Test]
        public void TryLoad_ReturnsFalse_WhenNothingWasEverSaved()
        {
            PlayerPrefs.DeleteKey(Key);
            var storage = new PlayerPrefsTutorialProgressStorage();

            Assert.IsFalse(storage.TryLoad(out _));
        }

        [Test]
        public void Save_ThenTryLoad_RoundTrips()
        {
            var storage = new PlayerPrefsTutorialProgressStorage();
            storage.Save(TutorialProgressState.Level1Unlocked.ToString());

            Assert.IsTrue(storage.TryLoad(out string value));
            Assert.AreEqual("Level1Unlocked", value);
        }

        [Test]
        public void Delete_ClearsTheSavedValue()
        {
            var storage = new PlayerPrefsTutorialProgressStorage();
            storage.Save(TutorialProgressState.CombineStarted.ToString());

            storage.Delete();

            Assert.IsFalse(storage.TryLoad(out _));
        }
    }
}
