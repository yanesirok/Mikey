# Yoko geri: гейт верхушки взмаха — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yoko geri перестаёт засчитывать подъёмы колена и махи без удара: зона считается только у верхушки взмаха (сглаженный подъём ≥ порога начала цикла), транзитные кадры опускания — нет.

**Architecture:** `LegLiftCycle` открывает свой порог свойством `LiftedAt`; `YokoGeriAnalyzer` добавляет его в условие сэмплирования зоны. Корпус-тест на реальной сессии 07:41 (граунд-трус 4 удара / 3 подъёма колена) охраняет от регрессий.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-06-yokogeri-apex-gate-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; при падениях смотреть `Temp/pose_tests.xml`):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Temp\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Исходник записи для корпуса: `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_074125.csv`.
- Если корпус yoko gedan даёт НЕ 4 (Reps) / НЕ 3 (NoReps) / НЕ Chudan (BestZone) — статус BLOCKED с фактическими числами, ожидания не подгонять.
- Гейт — ровно `_smoothedLift >= _cycle.LiftedAt` (порог 1.0 из цикла по умолчанию); не ослаблять до 0.9 — подъём колена в записи даёт прямой кадр на 0.93.
- Корпусы пуш-апа (5/4/0), приседа (18/15/1), wall-sit (6/0) и все существующие yoko/mae-тесты — зелёные без правок их ассертов.
- Файл в `Assets/Pose/Tests/Recordings/` добавлять через `git add -f` (правило gitignore `[Rr]ecordings/`); новые `.meta` появятся после прогона — в тот же коммит. Посторонние изменённые файлы (арена, ProjectSettings) не трогать.
- Если код и тесты брифа противоречат друг другу — статус BLOCKED, не подгонять одно под другое.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Гейт верхушки + корпус + пересборка

**Files:**
- Modify: `Assets/Pose/LegLiftCycle.cs` (свойство `LiftedAt` после `LiftedSeconds`)
- Modify: `Assets/Pose/YokoGeriAnalyzer.cs` (условие сэмплирования зоны + doc-строка)
- Modify: `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` (один новый тест; существующие восемь НЕ менять)
- Create: `Assets/Pose/Tests/Recordings/yoko_gedan_session.csv` (копия исходника из Global Constraints)
- Test: `Assets/Pose/Tests/YokoGeriRecordingTests.cs` (новый)

**Interfaces:**
- Consumes (всё уже в кодовой базе): `CsvPoseFrames.Load(string)` → `List<PoseFrame>`; `LegTestFrames.Kick` / `LegTestFrames.ChamberHigh`; `YokoGeriAnalyzer(KickZone requested, …)` с `Reps`/`NoReps`/`BestZone`/`Cue`.
- Produces: `LegLiftCycle.LiftedAt` → `float` (порог входа в цикл).

- [ ] **Step 1: Корпус и новый тест (красная фаза)**

Скопировать запись (PowerShell):

```powershell
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_074125.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\yoko_gedan_session.csv"
```

Создать `Assets/Pose/Tests/YokoGeriRecordingTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Characterization corpus for yoko geri gedan: a real on-device session
    /// (2026-08-06, ground truth: three slow kicks and one chudan-height swing count,
    /// three knee raises must not). Guards the apex-gate scoring against regressions.
    /// </summary>
    public class YokoGeriRecordingTests
    {
        [Test]
        public void GedanSession_CountsKicksNotKneeRaises()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/yoko_gedan_session.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(4, analyzer.Reps);
            Assert.AreEqual(3, analyzer.NoReps);
            Assert.AreEqual(KickZone.Chudan, analyzer.BestZone);
        }
    }
}
```

В `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` добавить после `HighChamberWithoutExtensionIsNoRepWithExtendCue`:

```csharp
        [Test]
        public void ExtensionOnlyOnDescentDoesNotAward()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            a.ProcessFrame(LegTestFrames.Kick(Floor, chambered: false, 1f, 0.0));
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));   // вход в цикл, колено согнуто
            Feed(a, 0.78f, 0.6);      // нога прямая, но подъём 0.6 — уже опускается
            Feed(a, Floor, 0.9);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выпрями ногу", a.Cue);
        }
```

- [ ] **Step 2: Прогнать — красная фаза.** Run: команда тестов. Expected: exit=6, ровно два падения: `GedanSession_CountsKicksNotKneeRaises` (Reps 7 ≠ 4 — текущий код считает всё) и `ExtensionOnlyOnDescentDoesNotAward` (Reps 1 ≠ 0 — опускание даёт гэдан). Другие падения или другие числа → BLOCKED с фактами.

- [ ] **Step 3: Реализация**

В `Assets/Pose/LegLiftCycle.cs` после строки `public double LiftedSeconds { get; private set; }` добавить:

```csharp
        /// <summary>Lift threshold that starts a cycle; kick analyzers gate zone sampling on it.</summary>
        public float LiftedAt => _liftedAt;
```

В `Assets/Pose/YokoGeriAnalyzer.cs` заменить

```csharp
                _lastKneeDeg = PoseMath.AngleDeg(hip, knee, ankle);
                if (_lastKneeDeg >= _minExtensionDeg)
```

на

```csharp
                _lastKneeDeg = PoseMath.AngleDeg(hip, knee, ankle);
                // Зона — только у верхушки взмаха: опускающаяся нога распрямляется сама
                // (маятник), и её транзитные кадры ниже порога цикла ударом не являются.
                if (_lastKneeDeg >= _minExtensionDeg && _smoothedLift >= _cycle.LiftedAt)
```

и в doc-комментарии класса заменить фразу `the height zone is sampled only on frames
where the leg is extended (in-plane knee angle ≥ minExtensionDeg — noisy z depth
is not involved), so a raised chamber alone is not a kick.` на `the height zone is
sampled only on frames where the leg is extended (in-plane knee angle ≥
minExtensionDeg — noisy z depth is not involved) AND still at the swing's top
(smoothed lift ≥ the cycle's LiftedAt), so neither a raised chamber nor the
straightening descent counts as a kick.`

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`, все зелёные (ориентир — 91 тест: 89 + корпус + юнит). Корпус yoko gedan ровно 4/3/Chudan; числа НЕ совпали → BLOCKED с фактическими значениями.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/LegLiftCycle.cs Assets/Pose/YokoGeriAnalyzer.cs Assets/Pose/Tests/YokoGeriAnalyzerTests.cs Assets/Pose/Tests/YokoGeriRecordingTests.cs
git add -f Assets/Pose/Tests/YokoGeriRecordingTests.cs.meta Assets/Pose/Tests/Recordings/yoko_gedan_session.csv Assets/Pose/Tests/Recordings/yoko_gedan_session.csv.meta
git commit -m @'
fix: yoko geri считает зону только у верхушки взмаха — подъёмы колена больше не проходят

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

(Если `.meta` для новых файлов ещё не созданы — они появятся после прогона тестов на Step 4; добавить их этим же коммитом.)

- [ ] **Step 6: Сборка** (Editor закрыт)

```powershell
unity build "C:\Users\user\Mikey" --target Android --execute-method Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid --no-banner; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, свежий mtime `Builds/ExerciseSandbox.apk` (несвежий — не устанавливать, эскалировать).

- [ ] **Step 7: Установка**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 8: Пользовательская проверка** — гэдан-удар с выпрямлением считается; подъём колена / мах без удара — «Выпрями ногу», счёт не растёт.
