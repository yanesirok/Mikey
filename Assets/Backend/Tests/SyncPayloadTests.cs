using Mikey.Pose;
using Mikey.UI.Profile;
using Mikey.UI.Progression;
using NUnit.Framework;

namespace Mikey.Backend.Tests
{
    /// <summary>
    /// Контракт слияния на стороне клиента. Здесь ловится единственная ошибка,
    /// которая молча съедает данные пользователя: ответ сервера, понижающий
    /// локальный результат.
    /// </summary>
    public class SyncPayloadTests
    {
        [Test]
        public void Build_CopiesEveryLocalField()
        {
            var profile = new ProfileUserData
            {
                DisplayName = "Дима", Gender = ProfileUserData.GenderMale,
                Age = 21, WeightKg = 70f, HeightCm = 180,
                UpdatedAtIso = "2026-08-20T10:00:00Z",
            };
            var level0 = new Level0Results
            {
                PushUpReps = 20, SquatReps = 30, YokoGeriSlowReps = 5,
                YokoGeriBestZone = 2, WallSitSeconds = 45f, YokoGeriHoldSeconds = 12f,
            };
            var level1 = new Level1Progress();
            level1.Absorb("stance-zenkutsu", 4);

            SyncState state = SyncPayload.Build(profile, TutorialProgressState.CombineCompleted, level0, level1);

            Assert.AreEqual("Дима", state.profile.display_name);
            Assert.AreEqual(21, state.profile.age);
            Assert.AreEqual(180, state.profile.height_cm);
            Assert.AreEqual("2026-08-20T10:00:00Z", state.profile.profile_updated_at);
            Assert.AreEqual((int)TutorialProgressState.CombineCompleted, state.profile.tutorial_progress);
            Assert.AreEqual(20, state.level0.pushup_reps);
            Assert.AreEqual(45f, state.level0.wallsit_seconds);
            Assert.AreEqual(1, state.level1.Count);
            Assert.AreEqual("stance-zenkutsu", state.level1[0].technique_id);
            Assert.AreEqual(4, state.level1[0].clean_reps);
        }

        [Test]
        public void Apply_NeverLowersALocalResult()
        {
            var profile = new ProfileUserData { UpdatedAtIso = "2026-08-20T10:00:00Z" };
            var level0 = new Level0Results { PushUpReps = 20, WallSitSeconds = 45f };
            var level1 = new Level1Progress();
            level1.Absorb("stance-zenkutsu", 4);
            var progress = TutorialProgressState.CombineCompleted;

            // Ответ беднее локального состояния во всём — так выглядит усечённый
            // ответ или ответ после отката серверной миграции.
            var merged = new SyncState
            {
                profile = new SyncProfile { tutorial_progress = (int)TutorialProgressState.NewPlayer },
                level0 = new SyncLevel0 { pushup_reps = 1, wallsit_seconds = 0f },
            };
            merged.level1.Add(new SyncTechnique { technique_id = "stance-zenkutsu", clean_reps = 1 });

            SyncPayload.Apply(merged, profile, level0, level1, ref progress);

            Assert.AreEqual(20, level0.PushUpReps, "Отжимания понижены ответом сервера.");
            Assert.AreEqual(45f, level0.WallSitSeconds, "Стенка понижена ответом сервера.");
            Assert.AreEqual(4, level1.RepsFor("stance-zenkutsu"), "Уровень 1 понижен ответом сервера.");
            Assert.AreEqual(TutorialProgressState.CombineCompleted, progress, "Прогресс откатился назад.");
        }

        [Test]
        public void Apply_TakesServerResultsWhenTheyAreBetter()
        {
            var profile = new ProfileUserData { UpdatedAtIso = "2026-08-20T10:00:00Z" };
            var level0 = new Level0Results { PushUpReps = 20 };
            var level1 = new Level1Progress();
            var progress = TutorialProgressState.CombineStarted;

            var merged = new SyncState
            {
                profile = new SyncProfile { tutorial_progress = (int)TutorialProgressState.Level1Unlocked },
                level0 = new SyncLevel0 { pushup_reps = 33 },
            };
            merged.level1.Add(new SyncTechnique { technique_id = "kizamizuki-jodan", clean_reps = 5 });

            SyncPayload.Apply(merged, profile, level0, level1, ref progress);

            Assert.AreEqual(33, level0.PushUpReps);
            Assert.AreEqual(5, level1.RepsFor("kizamizuki-jodan"));
            Assert.AreEqual(TutorialProgressState.Level1Unlocked, progress);
        }

        [Test]
        public void Apply_ReplacesProfileOnlyWhenServerCopyIsNewer()
        {
            var profile = new ProfileUserData { DisplayName = "Локальное", UpdatedAtIso = "2026-08-20T10:00:00Z" };
            var level0 = new Level0Results();
            var level1 = new Level1Progress();
            var progress = TutorialProgressState.NewPlayer;

            var stale = new SyncState
            {
                profile = new SyncProfile { display_name = "Старое", profile_updated_at = "2026-08-19T10:00:00Z" },
            };
            SyncPayload.Apply(stale, profile, level0, level1, ref progress);
            Assert.AreEqual("Локальное", profile.DisplayName, "Старый профиль затёр свежий локальный.");

            var fresh = new SyncState
            {
                profile = new SyncProfile { display_name = "Свежее", age = 30, profile_updated_at = "2026-08-21T10:00:00Z" },
            };
            SyncPayload.Apply(fresh, profile, level0, level1, ref progress);
            Assert.AreEqual("Свежее", profile.DisplayName);
            Assert.AreEqual(30, profile.Age);
            Assert.AreEqual("2026-08-21T10:00:00Z", profile.UpdatedAtIso);
        }

        [Test]
        public void Apply_ToleratesAnEmptyOrPartialResponse()
        {
            var profile = new ProfileUserData { DisplayName = "Дима", UpdatedAtIso = "2026-08-20T10:00:00Z" };
            var level0 = new Level0Results { PushUpReps = 20 };
            var level1 = new Level1Progress();
            var progress = TutorialProgressState.CombineStarted;

            SyncPayload.Apply(null, profile, level0, level1, ref progress);
            SyncPayload.Apply(new SyncState { profile = null, level0 = null, level1 = null },
                              profile, level0, level1, ref progress);

            Assert.AreEqual("Дима", profile.DisplayName);
            Assert.AreEqual(20, level0.PushUpReps);
            Assert.AreEqual(TutorialProgressState.CombineStarted, progress);
        }

        [Test]
        public void IsNewer_TreatsMissingStampAsOldest()
        {
            Assert.IsTrue(SyncPayload.IsNewer("2026-08-20T10:00:00Z", string.Empty));
            Assert.IsFalse(SyncPayload.IsNewer(string.Empty, "2026-08-20T10:00:00Z"));
            Assert.IsFalse(SyncPayload.IsNewer(string.Empty, string.Empty));
            Assert.IsFalse(SyncPayload.IsNewer("2026-08-20T10:00:00Z", "2026-08-20T10:00:00Z"));
            Assert.IsFalse(SyncPayload.IsNewer("мусор", "2026-08-20T10:00:00Z"));
        }
    }
}
