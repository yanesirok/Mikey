# Yoko geri v5: строгое окно высоты — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Повтор yoko geri засчитывается только на точно заданной высоте: пик выше заданной зоны → no-rep «Ниже», ниже → «Выше»; замах и сигнатура v4 не меняются.

**Architecture:** В `YokoGeriAnalyzer` условие зачёта `peak >= _requested` меняется на `peak == _requested`, в cue-приоритет добавляется ветка «Ниже» (пик выше заданной). Корпуса: размеченная сессия перезакрепляется 3/4/Chudan, новая сессия высот 03:28 добавляется как 4/5/Jodan.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-08-yokogeri-strict-zone-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; вывод НЕ в `Temp/` проекта):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Logs\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Исходник записи для нового корпуса: `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_032811.csv`.
- Если корпуса дают НЕ (3/4/Chudan размеченный, 5/9/Gedan смешанный, 4/5/Jodan высоты, 0 ходьба) — статус BLOCKED с фактическими числами, ожидания не подгонять.
- Пороги v4 не трогать: `fastKickAt = 1.2f`, `kickBandAt = 0.45f`, `minBandFrames = 2`, `chamberMaxKneeDeg = 110f`, `minExtensionDeg = 150f`, `minVisibility = 0.6f`, `smoothingAlpha = 0.6f`. Cue-строки точные: «Сначала колено», «Выпрями ногу», «Ниже», «Выше», «В кадр (лицом)».
- В `StatCalculatorTests.cs` меняется ТОЛЬКО строка 116 (`KickZone.Gedan` → `KickZone.Chudan` — под строгим окном чудан-удар в гэдан-режиме не зачёт); ассерты не трогать.
- Корпусы пуш-апа (5/4/0), приседа (18/15/1), wall-sit (6/0) не трогаются.
- Файл в `Recordings/` добавлять через `git add -f`; новые `.meta` — в тот же коммит. Посторонние изменённые файлы (арена, ProjectSettings) не трогать.
- Если код и тесты брифа противоречат друг другу — статус BLOCKED, не подгонять одно под другое.
- Если тест-раннер завис: `Get-Process Unity*`, убить, повторить один раз; снова завис — BLOCKED. Сборка без зелёных тестов запрещена.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Строгое окно + корпуса + пересборка

**Files:**
- Modify: `Assets/Pose/YokoGeriAnalyzer.cs` (зачёт `==`, ветка «Ниже», doc-строка)
- Modify: `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` (перевернуть один тест; остальные десять НЕ менять)
- Modify: `Assets/Pose/Tests/YokoGeriRecordingTests.cs` (два числа в `GedanSession…` + новый тест)
- Modify: `Assets/Pose/Tests/StatCalculatorTests.cs` (одна строка: зона в yoko-секции Absorb)
- Create: `Assets/Pose/Tests/Recordings/yoko_gedan_heights.csv` (копия исходника из Global Constraints)

**Interfaces:**
- Consumes: всё уже в кодовой базе (`CsvPoseFrames.Load`, `LegTestFrames`, `YokoGeriAnalyzer` v4).
- Produces: поведение «строгое окно»; публичные сигнатуры не меняются.

- [ ] **Step 1: Тесты (красная фаза)**

Скопировать запись (PowerShell):

```powershell
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_032811.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\yoko_gedan_heights.csv"
```

В `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` заменить тест `KickAboveRequestedZoneCounts` целиком на:

```csharp
        [Test]
        public void KickAboveRequestedZoneIsNoRepWithLowerCue()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0);
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));
            Feed(a, JodanY, 0.6);
            Feed(a, Floor, 0.9);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Ниже", a.Cue);
            Assert.AreEqual(KickZone.Jodan, a.BestZone);   // гибкость копится и на «Ниже»
        }
```

В `Assets/Pose/Tests/YokoGeriRecordingTests.cs` в тесте `GedanSession_CountsKicksNotKneeRaises` заменить

```csharp
            Assert.AreEqual(4, analyzer.Reps);
            Assert.AreEqual(3, analyzer.NoReps);
```

на

```csharp
            Assert.AreEqual(3, analyzer.Reps);            // чудан-мах теперь «Ниже», не зачёт
            Assert.AreEqual(4, analyzer.NoReps);
```

(ассерт `BestZone == Chudan` не трогать) и добавить после `MixedSession…`:

```csharp
        [Test]
        public void HeightsSession_RejectsKicksAboveRequested()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/yoko_gedan_heights.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(4, analyzer.Reps);
            Assert.AreEqual(5, analyzer.NoReps);
            Assert.AreEqual(KickZone.Jodan, analyzer.BestZone);   // дзёдан-удары растяжку показали
        }
```

В `Assets/Pose/Tests/StatCalculatorTests.cs` заменить строку

```csharp
            var yoko = new YokoGeriAnalyzer(KickZone.Gedan, smoothingAlpha: 1f);
```

на

```csharp
            var yoko = new YokoGeriAnalyzer(KickZone.Chudan, smoothingAlpha: 1f);
```

(в секции бьётся чудан-удар — под строгим окном он зачётен только в чудан-режиме; ассерты не меняются).

- [ ] **Step 2: Прогнать — красная фаза.** Run: команда тестов. Expected: exit=6, ровно три падения: `GedanSession_CountsKicksNotKneeRaises` (Reps 4 ≠ 3), `HeightsSession_RejectsKicksAboveRequested` (Reps 6 ≠ 4 — текущий код считает дзёданы в гэдане), `KickAboveRequestedZoneIsNoRepWithLowerCue` (Reps 1 ≠ 0). `MixedSession…` (5/9), `WalkingRecording…` (0) и правленый `AbsorbKeepsBestOfEachExercise` зелёные уже на текущем коде. Другие падения или числа → BLOCKED с фактами.

- [ ] **Step 3: Реализация**

В `Assets/Pose/YokoGeriAnalyzer.cs` заменить блок завершения

```csharp
                if (peak >= _requested)
                {
                    Reps++;
                    TotalLiftedSeconds += _cycle.LiftedSeconds;
                }
                else
                {
                    NoReps++;
                    Cue = !_chambered ? "Сначала колено"
                        : peak == KickZone.None ? "Выпрями ногу"
                        : "Выше";
                }
```

на

```csharp
                if (peak == _requested)
                {
                    Reps++;
                    TotalLiftedSeconds += _cycle.LiftedSeconds;
                }
                else
                {
                    NoReps++;
                    Cue = !_chambered ? "Сначала колено"
                        : peak == KickZone.None ? "Выпрями ногу"
                        : peak > _requested ? "Ниже"
                        : "Выше";
                }
```

и в doc-комментарии класса заменить предложение

```
Lenient policy: reaching the requested
    /// zone OR higher counts.
```

на

```
Strict height window: only the requested zone
    /// counts — higher is a no-rep ("Ниже"), lower is a no-rep ("Выше").
```

(остальной текст doc-комментария не трогать).

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`, все зелёные (ориентир — 96 тестов: 95 + новый корпус). Корпуса: размеченный 3/4/Chudan, смешанный 5/9/Gedan, высоты 4/5/Jodan, ходьба 0. Числа НЕ совпали → BLOCKED с фактическими значениями.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/YokoGeriAnalyzer.cs Assets/Pose/Tests/YokoGeriAnalyzerTests.cs Assets/Pose/Tests/YokoGeriRecordingTests.cs Assets/Pose/Tests/StatCalculatorTests.cs
git add -f Assets/Pose/Tests/Recordings/yoko_gedan_heights.csv Assets/Pose/Tests/Recordings/yoko_gedan_heights.csv.meta
git commit -m @'
feat: yoko geri v5 — строгое окно высоты: зона ровно заданная, выше — «Ниже»

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

(Если `.meta` нового CSV ещё нет — он появится после прогона на Step 4; добавить тем же коммитом.)

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

- [ ] **Step 8: Пользовательская проверка** — в режиме гэдан: удар на высоте колена (с замахом) — зачёт; тот же удар в корпус/голову — «Ниже», счёт стоит; в режимах чудан/дзёдан — своя высота, ниже — «Выше», выше — «Ниже».
