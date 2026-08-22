using System;
using System.Globalization;
using Mikey.Pose;
using Mikey.UI.Profile;
using Mikey.UI.Progression;

namespace Mikey.Backend
{
    /// <summary>
    /// Перекладывает состояние между локальными хранилищами и телом запроса
    /// <c>sync_progress</c>. Не делает ни одного вызова UnityEngine — ни
    /// PlayerPrefs, ни MonoBehaviour, — поэтому проверяется EditMode-тестами на
    /// голых объектах, без сцены, панели и сети. Тот же приём, что у
    /// <c>PracticeSessionModel</c> и <c>TutorialProgressPresenter</c>.
    ///
    /// Ключевое свойство: <see cref="Apply"/> берёт максимум сам, а не доверяет
    /// серверу. Сервер и так возвращает слитое значение, но повторная защита
    /// стоит трёх строк и закрывает целый класс аварий — усечённый ответ, ответ
    /// после отката миграции, ответ не от того пользователя. Цена ошибки здесь —
    /// молча стёртый прогресс живого человека.
    /// </summary>
    public static class SyncPayload
    {
        /// <summary>Собирает текущее состояние устройства в тело запроса.</summary>
        public static SyncState Build(
            ProfileUserData profile,
            TutorialProgressState progress,
            Level0Results level0,
            Level1Progress level1)
        {
            var state = new SyncState();

            if (profile != null)
            {
                state.profile.display_name = profile.DisplayName ?? string.Empty;
                state.profile.gender = profile.Gender ?? string.Empty;
                state.profile.age = profile.Age;
                state.profile.weight_kg = profile.WeightKg;
                state.profile.height_cm = profile.HeightCm;
                state.profile.profile_updated_at = profile.UpdatedAtIso ?? string.Empty;
            }

            state.profile.tutorial_progress = (int)progress;

            if (level0 != null)
            {
                state.level0.pushup_reps = level0.PushUpReps;
                state.level0.squat_reps = level0.SquatReps;
                state.level0.yokogeri_slow_reps = level0.YokoGeriSlowReps;
                state.level0.yokogeri_best_zone = level0.YokoGeriBestZone;
                state.level0.wallsit_seconds = level0.WallSitSeconds;
                state.level0.yokogeri_hold_seconds = level0.YokoGeriHoldSeconds;
            }

            if (level1?.Entries != null)
            {
                foreach (Level1Progress.Entry entry in level1.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Id))
                        continue;
                    state.level1.Add(new SyncTechnique
                    {
                        technique_id = entry.Id,
                        clean_reps = entry.CleanReps,
                    });
                }
            }

            return state;
        }

        /// <summary>
        /// Вливает ответ сервера в локальные объекты. Прогресс — только вверх;
        /// профиль — только если серверная копия свежее. Ничего не сохраняет:
        /// запись в PlayerPrefs остаётся за вызывающим.
        /// </summary>
        public static void Apply(
            SyncState merged,
            ProfileUserData profile,
            Level0Results level0,
            Level1Progress level1,
            ref TutorialProgressState progress)
        {
            if (merged == null)
                return;

            if (merged.level0 != null && level0 != null)
            {
                level0.PushUpReps = Math.Max(level0.PushUpReps, merged.level0.pushup_reps);
                level0.SquatReps = Math.Max(level0.SquatReps, merged.level0.squat_reps);
                level0.YokoGeriSlowReps = Math.Max(level0.YokoGeriSlowReps, merged.level0.yokogeri_slow_reps);
                level0.YokoGeriBestZone = Math.Max(level0.YokoGeriBestZone, merged.level0.yokogeri_best_zone);
                level0.WallSitSeconds = Math.Max(level0.WallSitSeconds, merged.level0.wallsit_seconds);
                level0.YokoGeriHoldSeconds = Math.Max(level0.YokoGeriHoldSeconds, merged.level0.yokogeri_hold_seconds);
            }

            if (merged.level1 != null && level1 != null)
            {
                foreach (SyncTechnique technique in merged.level1)
                {
                    if (technique == null || string.IsNullOrEmpty(technique.technique_id))
                        continue;
                    // Absorb уже берёт максимум и игнорирует мусор.
                    level1.Absorb(technique.technique_id, technique.clean_reps);
                }
            }

            if (merged.profile == null)
                return;

            var incoming = (TutorialProgressState)merged.profile.tutorial_progress;
            if (incoming > progress)
                progress = incoming;

            if (profile != null
                && !LooksEmpty(merged.profile)
                && IsNewer(merged.profile.profile_updated_at, profile.UpdatedAtIso))
            {
                profile.DisplayName = merged.profile.display_name;
                profile.Gender = merged.profile.gender;
                profile.Age = merged.profile.age;
                profile.WeightKg = merged.profile.weight_kg;
                profile.HeightCm = merged.profile.height_cm;
                profile.UpdatedAtIso = merged.profile.profile_updated_at;
            }
        }

        /// <summary>
        /// Профиль, пустой целиком, но объявленный свежим, — это усечённый ответ, а не
        /// человек, стёрший о себе сразу всё. Отличить одно от другого можно: очистка
        /// одного поля оставляет остальные заполненными. Принять такой ответ значит
        /// обнулить профиль, который человек заполнял руками.
        /// </summary>
        private static bool LooksEmpty(SyncProfile p) =>
            string.IsNullOrEmpty(p.display_name)
            && string.IsNullOrEmpty(p.gender)
            && p.age == 0
            && p.weight_kg == 0f
            && p.height_cm == 0;

        /// <summary>
        /// Строго ли <paramref name="candidateIso"/> свежее <paramref name="currentIso"/>.
        /// Пустой или неразбираемый штамп считается самым старым: это значит
        /// «время правки неизвестно», и такая копия не должна затирать ту, у
        /// которой время известно.
        /// </summary>
        public static bool IsNewer(string candidateIso, string currentIso)
        {
            if (!TryParse(candidateIso, out DateTime candidate))
                return false;
            if (!TryParse(currentIso, out DateTime current))
                return true;
            return candidate > current;
        }

        private static bool TryParse(string iso, out DateTime value)
        {
            value = default;
            return !string.IsNullOrEmpty(iso)
                   && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                        out value);
        }
    }
}
