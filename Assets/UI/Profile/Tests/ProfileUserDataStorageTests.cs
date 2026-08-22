using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Round-trip, migration and completeness contract for
    /// <see cref="ProfileUserDataStorage"/>. Every test cleans up both the new
    /// primary key and the old username-only key in TearDown so this run never
    /// leaves residue in the real Editor/player local storage — mirrors
    /// PlayerPrefsTutorialProgressStorageTests' established pattern.
    /// </summary>
    public class ProfileUserDataStorageTests
    {
        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(ProfileUserDataStorage.PlayerPrefsKey);
            PlayerPrefs.DeleteKey(ProfileDisplayNameStorage.PlayerPrefsKey);
        }

        [Test]
        public void PrimaryStorageKey_IsTheOneApprovedKey()
        {
            Assert.AreEqual("Mikey.Profile.UserData", ProfileUserDataStorage.PlayerPrefsKey);
        }

        [Test]
        public void Load_WithNothingSavedAnywhere_ReturnsDefaultDisplayNameAndEmptyOtherFields()
        {
            PlayerPrefs.DeleteKey(ProfileUserDataStorage.PlayerPrefsKey);
            PlayerPrefs.DeleteKey(ProfileDisplayNameStorage.PlayerPrefsKey);

            var data = ProfileUserDataStorage.Load();

            Assert.AreEqual(ProfileDisplayNameStorage.DefaultDisplayName, data.DisplayName);
            Assert.AreEqual(string.Empty, data.Gender);
            Assert.AreEqual(0, data.Age);
        }

        [Test]
        public void Save_ThenLoad_RoundTripsEveryField()
        {
            var saved = new ProfileUserData
            {
                DisplayName = "Jak",
                Gender = ProfileUserData.GenderOther,
                Age = 30,
                WeightKg = 72.5f,
                HeightCm = 178,
            };

            ProfileUserDataStorage.Save(saved);
            var loaded = ProfileUserDataStorage.Load();

            Assert.AreEqual(saved.DisplayName, loaded.DisplayName);
            Assert.AreEqual(saved.Gender, loaded.Gender);
            Assert.AreEqual(saved.Age, loaded.Age);
            Assert.AreEqual(saved.WeightKg, loaded.WeightKg, 0.001f);
            Assert.AreEqual(saved.HeightCm, loaded.HeightCm);
        }

        [Test]
        public void Load_MigratesAnExistingDisplayNameOnly_WhenNoUserDataHasBeenSavedYet()
        {
            PlayerPrefs.DeleteKey(ProfileUserDataStorage.PlayerPrefsKey);
            ProfileDisplayNameStorage.Save("PreviouslySavedName");

            var data = ProfileUserDataStorage.Load();

            Assert.AreEqual("PreviouslySavedName", data.DisplayName,
                "An existing Mikey.Profile.DisplayName must not be lost when ProfileUserData is introduced.");
        }

        [Test]
        public void Load_DoesNotMigrate_OnceUserDataHasBeenSavedAtLeastOnce()
        {
            ProfileDisplayNameStorage.Save("OldName");
            ProfileUserDataStorage.Save(new ProfileUserData { DisplayName = "NewName" });

            var data = ProfileUserDataStorage.Load();

            Assert.AreEqual("NewName", data.DisplayName, "Once ProfileUserData exists, it is the source of truth, not the old key.");
        }

        [Test]
        public void OldDisplayNameKey_IsNotDeleted_ByMigration()
        {
            ProfileDisplayNameStorage.Save("StillThere");
            PlayerPrefs.DeleteKey(ProfileUserDataStorage.PlayerPrefsKey);

            ProfileUserDataStorage.Load();

            Assert.IsTrue(PlayerPrefs.HasKey(ProfileDisplayNameStorage.PlayerPrefsKey),
                "Migration must not aggressively delete the old key.");
        }

        [Test]
        public void IsComplete_IsFalse_ForFreshDefaultData()
        {
            Assert.IsFalse(ProfileUserDataStorage.IsComplete(new ProfileUserData()));
        }

        [Test]
        public void IsComplete_IsFalse_ForNull()
        {
            Assert.IsFalse(ProfileUserDataStorage.IsComplete(null));
        }

        [Test]
        public void IsComplete_IsTrue_WhenEveryFieldIsValid()
        {
            var data = new ProfileUserData
            {
                DisplayName = "Mikey",
                Gender = ProfileUserData.GenderPreferNotToSay,
                Age = 25,
                WeightKg = 70f,
                HeightCm = 175,
            };
            Assert.IsTrue(ProfileUserDataStorage.IsComplete(data));
        }

        [TestCase("", ProfileUserData.GenderMale, 25, 70f, 175)]
        [TestCase("Mikey", "", 25, 70f, 175)]
        [TestCase("Mikey", ProfileUserData.GenderMale, 5, 70f, 175)]
        [TestCase("Mikey", ProfileUserData.GenderMale, 25, 10f, 175)]
        [TestCase("Mikey", ProfileUserData.GenderMale, 25, 70f, 50)]
        public void IsComplete_IsFalse_IfAnySingleFieldIsInvalid(string name, string gender, int age, float weightKg, int heightCm)
        {
            var data = new ProfileUserData { DisplayName = name, Gender = gender, Age = age, WeightKg = weightKg, HeightCm = heightCm };
            Assert.IsFalse(ProfileUserDataStorage.IsComplete(data));
        }

        // Штамп времени правки — основа правила «профиль побеждает по свежести».
        // Если Save перестанет его ставить, синхронизация начнёт молча терять правки.

        [Test]
        public void Save_StampsUpdatedAt_InRoundTrippableUtcIso()
        {
            var data = new ProfileUserData { DisplayName = "Дима", Age = 21 };

            ProfileUserDataStorage.Save(data);
            ProfileUserData loaded = ProfileUserDataStorage.Load();

            Assert.IsNotEmpty(loaded.UpdatedAtIso, "Save обязан проставить UpdatedAtIso.");
            Assert.IsTrue(
                DateTime.TryParse(loaded.UpdatedAtIso, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                  out _),
                $"UpdatedAtIso должен разбираться как дата, получено: '{loaded.UpdatedAtIso}'.");
        }

        [Test]
        public void Save_ReplacesAnyExistingStamp_WithTheCurrentTime()
        {
            // Штамп из прошлого: Save обязан затереть его своим, а не оставить чужой.
            var data = new ProfileUserData { DisplayName = "Дима", UpdatedAtIso = "2000-01-01T00:00:00Z" };
            DateTime before = DateTime.UtcNow.AddSeconds(-1);

            ProfileUserDataStorage.Save(data);

            string stamp = ProfileUserDataStorage.Load().UpdatedAtIso;

            Assert.AreNotEqual("2000-01-01T00:00:00Z", stamp,
                "Save обязан заменить старый штамп своим — иначе правка человека выглядит устаревшей.");
            Assert.IsTrue(
                DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                  out DateTime parsed),
                $"Штамп должен разбираться как дата, получено: '{stamp}'.");
            Assert.GreaterOrEqual(parsed, before,
                "Штамп должен быть текущим временем, а не произвольным значением.");
        }

        [Test]
        public void SaveSynced_KeepsTheStampItWasGiven()
        {
            var fromServer = new ProfileUserData
            {
                DisplayName = "С сервера",
                UpdatedAtIso = "2026-08-19T10:00:00Z",
            };

            ProfileUserDataStorage.SaveSynced(fromServer);

            Assert.AreEqual("2026-08-19T10:00:00Z", ProfileUserDataStorage.Load().UpdatedAtIso,
                "Принятая с сервера копия не должна выглядеть как своя свежая правка — " +
                "иначе это устройство навсегда станет «самым новым».");
        }
    }
}
