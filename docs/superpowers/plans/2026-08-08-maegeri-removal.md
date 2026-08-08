# Удаление mae geri — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Убрать все три упражнения mae geri из приложения; гибкость считается только по yoko geri (дзёдан = 100).

**Architecture:** Чистое удаление: три строки каталога, файл анализатора с тестами, поле `MaeGeriBestZone` с веткой `Absorb`, mae-слагаемое в формуле гибкости. Пикер сандбокса строится из каталога — правок UI не нужно.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-08-maegeri-removal-design.md`

## Global Constraints

- **КРИТИЧНО — чужой индекс:** в git-индексе лежат staged-изменения арены из параллельной сессии пользователя (удаления в `Assets/Editor`, `Assets/Fight` и др.). ЗАПРЕЩЕНО: `git add .`, `git add -A` без путей, `git commit` без явного списка путей, любой `git reset`/`git restore --staged`. Коммитить ТОЛЬКО так: `git commit -m "..." -- <явные пути>`. Файлы вне `Assets/Pose/` и `docs/` не трогать вообще.
- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; вывод НЕ в `Temp/` проекта):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Logs\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Если тест-раннер или сборка падают с «another Unity instance is running» — у пользователя открыт Editor: НЕ убивать процесс с GUI (у него есть MainWindowTitle), статус BLOCKED. Если завис headless-раннер без окна: убить, повторить один раз; снова завис — BLOCKED.
- Yoko geri (`YokoGeriAnalyzer.cs` и его тесты), корпусные CSV, пуш-ап/присед/wall-sit не трогаются. `Assets/Fight/animations` (мокап mae geri для файт-сцены) не трогается.
- Ассерты якорей/середин в `StatCalculatorTests.cs` не меняются: среднее двух равных зон равно одной зоне (Jodan+Jodan→100→100, Chudan+Chudan→66→66).
- Сборка без зелёных тестов запрещена.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Удаление mae geri + пересборка

**Files:**
- Delete: `Assets/Pose/MaeGeriAnalyzer.cs` (+ `.meta`)
- Delete: `Assets/Pose/Tests/MaeGeriAnalyzerTests.cs` (+ `.meta`)
- Modify: `Assets/Pose/ExerciseCatalog.cs` (минус 3 строки)
- Modify: `Assets/Pose/Level0Results.cs` (поле + ветка Absorb)
- Modify: `Assets/Pose/StatCalculator.cs` (формула гибкости + комментарий)
- Modify: `Assets/Pose/Tests/StatCalculatorTests.cs` (4 места)

**Interfaces:**
- Consumes: всё уже в кодовой базе.
- Produces: каталог из 6 упражнений; `Level0Results` без `MaeGeriBestZone`; гибкость = зона yoko.

- [ ] **Step 1: Тесты (красная фаза)**

Удалить файл тестов mae geri (PowerShell):

```powershell
git rm "Assets/Pose/Tests/MaeGeriAnalyzerTests.cs" "Assets/Pose/Tests/MaeGeriAnalyzerTests.cs.meta"
```

В `Assets/Pose/Tests/StatCalculatorTests.cs`:

1. В `AnchorsGiveExactly100` удалить строку:

```csharp
                MaeGeriBestZone = (int)KickZone.Jodan,
```

2. В `MidpointsScaleLinearly` удалить строку:

```csharp
                MaeGeriBestZone = (int)KickZone.Chudan,
```

3. Тест `FlexibilityAveragesFrontAndSideKicks` заменить целиком на:

```csharp
        [Test]
        public void FlexibilityComesFromSideKick()
        {
            var r = new Level0Results();
            Assert.AreEqual(0, StatCalculator.Compute(r).Flexibility);
            r.YokoGeriBestZone = (int)KickZone.Jodan;
            Assert.AreEqual(100, StatCalculator.Compute(r).Flexibility);
        }
```

4. В `AbsorbKeepsBestOfEachExercise` удалить mae-секцию (шесть строк):

```csharp
            var mg = new MaeGeriAnalyzer(KickZone.Gedan, smoothingAlpha: 1f);
            mg.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 0.0));
            mg.ProcessFrame(LegTestFrames.Kick(0.18f, timestamp: 0.5));    // jodan
            mg.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 1.0));
            r.Absorb(mg);
            Assert.AreEqual((int)KickZone.Jodan, r.MaeGeriBestZone);
```

- [ ] **Step 2: Прогнать — красная фаза.** Run: команда тестов. Expected: exit=6, ровно одно падение — `FlexibilityComesFromSideKick`: текущая формула-среднее даёт 50 при yoko Jodan без mae (ожидается 100). Всего тестов 88 (было 96, минус 8 mae). Другие падения → BLOCKED с фактами.

- [ ] **Step 3: Реализация**

Удалить анализатор:

```powershell
git rm "Assets/Pose/MaeGeriAnalyzer.cs" "Assets/Pose/MaeGeriAnalyzer.cs.meta"
```

В `Assets/Pose/ExerciseCatalog.cs` удалить три строки:

```csharp
            new ExerciseDescriptor("maegeri-gedan", "Mae geri gedan", () => new MaeGeriAnalyzer(KickZone.Gedan)),
            new ExerciseDescriptor("maegeri-chudan", "Mae geri chudan", () => new MaeGeriAnalyzer(KickZone.Chudan)),
            new ExerciseDescriptor("maegeri-jodan", "Mae geri jodan", () => new MaeGeriAnalyzer(KickZone.Jodan)),
```

В `Assets/Pose/Level0Results.cs` удалить строку поля:

```csharp
        public int MaeGeriBestZone;        // (int)KickZone — JsonUtility дружит с int
```

и ветку в `Absorb`:

```csharp
                case MaeGeriAnalyzer m:
                    MaeGeriBestZone = Math.Max(MaeGeriBestZone, (int)m.BestZone);
                    break;
```

(Старые сейвы совместимы: `JsonUtility.FromJson` молча игнорирует лишнее JSON-поле.)

В `Assets/Pose/StatCalculator.cs` заменить

```csharp
            // Гибкость — среднее переднего (mae) и бокового (yoko) удара: это разные
            // растяжки, один вид удара не даёт 100.
            float flexibility = (FlexibilityByZone[ClampZone(r.MaeGeriBestZone)]
                               + FlexibilityByZone[ClampZone(r.YokoGeriBestZone)]) / 2f;
```

на

```csharp
            float flexibility = FlexibilityByZone[ClampZone(r.YokoGeriBestZone)];
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`, 88 тестов. Корпуса не менялись: yoko 3/4/Chudan, 5/9/Gedan, 4/5/Jodan, ходьба 0; пуш-ап 5/4/0, присед 18/15/1, wall-sit 6/0. Не exit=0 → BLOCKED с фактами.

- [ ] **Step 5: Commit** (ТОЛЬКО явные пути — в индексе чужие staged-изменения арены)

```powershell
git add "Assets/Pose/ExerciseCatalog.cs" "Assets/Pose/Level0Results.cs" "Assets/Pose/StatCalculator.cs" "Assets/Pose/Tests/StatCalculatorTests.cs"
git commit -m @'
feat: mae geri удалён — гибкость считается только по yoko geri

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@ -- "Assets/Pose/MaeGeriAnalyzer.cs" "Assets/Pose/MaeGeriAnalyzer.cs.meta" "Assets/Pose/Tests/MaeGeriAnalyzerTests.cs" "Assets/Pose/Tests/MaeGeriAnalyzerTests.cs.meta" "Assets/Pose/ExerciseCatalog.cs" "Assets/Pose/Level0Results.cs" "Assets/Pose/StatCalculator.cs" "Assets/Pose/Tests/StatCalculatorTests.cs"
```

Проверить: `git show --stat HEAD` — ровно 8 путей, никаких файлов арены.

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

- [ ] **Step 8: Пользовательская проверка** — в пикере 6 кнопок (2 ряда по 3), mae geri нет; yoko geri работает как раньше; после дзёдан-удара yoko гибкость в статах 100.
