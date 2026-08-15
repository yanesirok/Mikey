using NUnit.Framework;

namespace Mikey.UI.Audio.Tests
{
    /// <summary>
    /// Contract for <see cref="AudioSettingsStore"/>: safe defaults (Music 0.70,
    /// SFX 1.00, Trainer Voice 1.00) when nothing was ever saved, 0..1 clamping,
    /// invalid-data fallback, persistence round trip and change notification — all
    /// driven through an in-memory <see cref="FakeAudioSettingsStorage"/> so no real
    /// local storage is touched by this test run. Mirrors TutorialProgressStoreTests.
    /// </summary>
    public class AudioSettingsStoreTests
    {
        [Test]
        public void Defaults_MatchSpec_WhenNothingWasEverSaved()
        {
            var store = new AudioSettingsStore(new FakeAudioSettingsStorage());

            Assert.AreEqual(0.70f, store.MusicVolume);
            Assert.AreEqual(1.00f, store.SfxVolume);
            Assert.AreEqual(1.00f, store.TrainerVoiceVolume);
        }

        [Test]
        public void PersistenceRoundTrip_SurvivesANewStoreInstance()
        {
            var storage = new FakeAudioSettingsStorage();
            var first = new AudioSettingsStore(storage);
            first.MusicVolume = 0.35f;
            first.SfxVolume = 0.2f;
            first.TrainerVoiceVolume = 0.9f;

            // A fresh store over the SAME storage simulates an app/Editor restart.
            var second = new AudioSettingsStore(storage);
            Assert.AreEqual(0.35f, second.MusicVolume);
            Assert.AreEqual(0.2f, second.SfxVolume);
            Assert.AreEqual(0.9f, second.TrainerVoiceVolume);
        }

        [TestCase(-5f)]
        [TestCase(1.5f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void SettingOutOfRangeOrInvalid_ClampsInto0To1(float raw)
        {
            var store = new AudioSettingsStore(new FakeAudioSettingsStorage());
            store.MusicVolume = raw;

            Assert.GreaterOrEqual(store.MusicVolume, 0f);
            Assert.LessOrEqual(store.MusicVolume, 1f);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteSavedData_FallsBackSafelyToDefault(float corrupted)
        {
            var storage = new FakeAudioSettingsStorage();
            storage.Seed("Mikey.Audio.MusicVolume", corrupted);

            var store = new AudioSettingsStore(storage);

            Assert.AreEqual(AudioSettingsStore.DefaultMusicVolume, store.MusicVolume,
                $"Non-finite saved value '{corrupted}' must fall back to the safe default.");
        }

        [TestCase(-2f, 0f)]
        [TestCase(2f, 1f)]
        public void OutOfRangeButFiniteSavedData_IsClampedNotDefaulted(float saved, float expectedClamped)
        {
            // Unlike NaN/Infinity (genuinely unusable), an out-of-range-but-finite
            // saved value is recoverable — clamping preserves intent (e.g. a stray
            // 1.05 from floating-point rounding) instead of discarding it entirely.
            var storage = new FakeAudioSettingsStorage();
            storage.Seed("Mikey.Audio.MusicVolume", saved);

            var store = new AudioSettingsStore(storage);

            Assert.AreEqual(expectedClamped, store.MusicVolume,
                $"Out-of-range-but-finite saved value '{saved}' must be clamped to {expectedClamped}, not reset to the default.");
        }

        [Test]
        public void SetVolume_RaisesChanged_OnGenuineChange()
        {
            var store = new AudioSettingsStore(new FakeAudioSettingsStorage());
            int changedCount = 0;
            store.Changed += () => changedCount++;

            store.SfxVolume = 0.4f;

            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void SetVolume_DoesNotRaiseChanged_OnRedundantSameValue()
        {
            var store = new AudioSettingsStore(new FakeAudioSettingsStorage());
            store.SfxVolume = 0.4f;

            int changedCount = 0;
            store.Changed += () => changedCount++;
            store.SfxVolume = 0.4f;

            Assert.AreEqual(0, changedCount, "Re-setting the same value must not re-fire Changed.");
        }

        [Test]
        public void EachVolume_PersistsIndependently()
        {
            var storage = new FakeAudioSettingsStorage();
            var store = new AudioSettingsStore(storage);

            store.MusicVolume = 0.1f;

            Assert.AreEqual(0.1f, store.MusicVolume);
            Assert.AreEqual(AudioSettingsStore.DefaultSfxVolume, store.SfxVolume,
                "Changing Music volume must not affect SFX volume.");
            Assert.AreEqual(AudioSettingsStore.DefaultTrainerVoiceVolume, store.TrainerVoiceVolume,
                "Changing Music volume must not affect Trainer Voice volume.");
        }
    }
}
