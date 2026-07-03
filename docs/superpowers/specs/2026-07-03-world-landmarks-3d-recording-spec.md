# ТЗ: запись 3D world-landmarks на устройстве (объёмный скелет пользователя)

**Дата:** 2026-07-03
**Статус:** к реализации
**Для:** отдельного агента (контекст самодостаточный)
**Оценка:** ~пол-дня + 1 сборка APK + повторная съёмка

---

## 1. Что это за система (контекст)

Проект — **Unity 6.3** (`C:\Users\user\Mikey`). Есть пайплайн pose-детекции отжиманий:

- **Нативный плагин** `Assets/Plugins/Android/MikeyPose.androidlib/` (Java) — CameraX + MediaPipe
  Tasks `PoseLandmarker` (LIVE_STREAM). Файл `src/main/java/com/mikey/pose/PoseSession.java`.
- **Мост Unity** `Assets/Pose/AndroidPoseSource.cs` — тянет landmark'и из плагина по кадрам
  (`readLatest()` возвращает `float[132]` = 33 точки × {x,y,z,visibility}) и строит `PoseFrame`.
- **Модель кадра** `Assets/Pose/PoseFrame.cs` — держит 33 `PoseLandmark`.
- **Скоринг** `Assets/Pose/PushUp*.cs` — считает отжимания (пороги откалиброваны, работают).
- **Запись** `Assets/Pose/PoseController.cs` — метод `RecordFrame` + `SaveRecording()` пишут CSV
  (`t,x0,y0,z0,v0,...,x32,y32,z32,v32`) в `Application.persistentDataPath`.
- **Разборщик** `Assets/Pose/DevSandbox/PoseReviewer.cs` (Editor-инструмент) — грузит два CSV
  (ваш + эталон), рисует **два 3D-скелета** на площадке, свободная камера. Меню
  *Mikey → Dev → Create or Open Pose Review Scene*.
- **Эталон из видео** — уже извлечён скриптом `tools/extract_reference.py`
  (Python + MediaPipe) в `Assets/PoseRecordings/reference.csv`. Он использует
  **world-landmarks** (`pose_world_landmarks`).

---

## 2. Проблема

В `PoseReviewer` **скелет пользователя выглядит плоским/скомканным**, а эталон из урока —
полноценно объёмным. При вращении камеры у пользовательского скелета почти нет глубины.

### Корневая причина

Плагин записывает **нормализованные landmark'и** (`result.landmarks()` — координаты в
пространстве изображения: `x,y ∈ [0,1]`, а `z` — грубая относительная глубина, «примерно в
масштабе x»). Это по сути 2.5D: глубина слабая и шумная.

Эталон же берётся из **world-landmarks** (`result.worldLandmarks()` / в python
`pose_world_landmarks`) — это **метрические 3D-координаты в метрах с началом в центре таза**,
с настоящей глубиной.

Итог: пользователь и эталон — в **разных системах координат**, и у пользователя нет реального 3D.
`PoseReviewer` нормализует оба по тазу/корпусу, но из плоских normalized-данных объём не появится.

---

## 3. Что нужно сделать

**Записывать world-landmarks для пользователя тоже**, чтобы его скелет стал настоящим 3D и
совпадал по системе координат с эталоном. Нормализованные landmark'и **оставить** — они нужны
для (а) наложения точек на камеру в `ExerciseSandbox` (экранное пространство) и (б) уже
откалиброванного скоринга. То есть плагин отдаёт **оба набора**; в CSV пишем **world**.

### 3.1 Нативный плагин — `PoseSession.java`

Сейчас `onResult(PoseLandmarkerResult result)` заполняет буфер `latest[132]` из
`result.landmarks().get(0)`. Добавить параллельно world-набор:

- Завести поле `private final float[] latestWorld = new float[FLOATS];` (FLOATS=132).
- В `onResult`, помимо `result.landmarks().get(0)`, взять `result.worldLandmarks().get(0)`
  (тип элемента — `com.google.mediapipe.tasks.components.containers.Landmark`, методы
  `.x() .y() .z() .visibility()` где visibility — `Optional<Float>`), и заполнить `latestWorld`
  под тем же `lock`.
- Добавить публичный метод, симметричный `readLatest()`:
  ```java
  public float[] readLatestWorld() {
      synchronized (lock) {
          if (!hasNewWorld) return null;   // или переиспользовать hasNew, если наборы синхронны
          hasNewWorld = false;
          return latestWorld.clone();
      }
  }
  ```
  Наборы приходят в одном `onResult`, так что можно обойтись одним флагом `hasNew`, а
  `readLatestWorld()` просто клонировать `latestWorld` (см. 3.2 про синхронность чтения).

> Важно: `worldLandmarks()` может быть пустым, если поза не найдена — проверять `isEmpty()`.

### 3.2 Мост — `AndroidPoseSource.cs`

Сейчас `Tick()` вызывает `_session.Call<float[]>("readLatest")` и строит `PoseFrame` из
нормализованных точек. Нужно дополнительно прочитать world:

- В том же кадре вызвать `sbyte[]`/`float[] world = _session.Call<float[]>("readLatestWorld")`.
  (Замечание по перфу: `float[]` через JNI не даёт warning про byte[], можно оставить `float[]`.)
- Собрать `PoseFrame`, передав **оба** массива (нормализованные + world) — см. 3.3.
- Чтобы наборы точно соответствовали одному кадру, читать world **сразу после** landmarks в
  пределах одного `Tick` (они пишутся вместе в `onResult`, гонки некритичны для визуализации).

### 3.3 Модель кадра — `PoseFrame.cs`

Добавить второй набор точек, не ломая существующий API:

- Хранить `private readonly PoseLandmark[] _world;` (может быть null, если world недоступны).
- Конструктор с двумя массивами: `PoseFrame(PoseLandmark[] landmarks, PoseLandmark[] world, double t)`.
  Старый конструктор оставить (world=null) — чтобы синтетические тесты и симуляция не падали.
- Аксессор `public PoseLandmark WorldLandmark(int index)` и `public bool HasWorld => _world != null;`.
- **Скоринг не трогать** — `PushUpAnalyzer`/`PushUpFormEvaluator` продолжают читать `Landmark()`
  (нормализованные), пороги остаются прежними.

### 3.4 Запись — `PoseController.cs`

Метод `RecordFrame`/`SaveRecording` сейчас пишет `frame.Landmark(i)`. Изменить так, чтобы в CSV
шли **world**-координаты, когда они есть:

- В `RecordFrame`: если `frame.HasWorld`, писать `frame.WorldLandmark(i)`, иначе `frame.Landmark(i)`
  (fallback, чтобы симуляция в Editor тоже писала хоть что-то).
- Формат CSV **не менять** (те же 133 колонки) — меняется только *содержимое* (world вместо
  normalized). Тогда `PoseReviewer` и `reference.csv` останутся в одном формате и системе координат.

### 3.5 Симуляция/овеонлей (не ломать)

- `SimulatedPoseSource.cs` — можно оставить как есть (world=null → в CSV пойдут его normalized
  точки; для Editor-симуляции это ок).
- `ExerciseSandbox.cs` (наложение точек на камеру) — **оставить на нормализованных**
  (`LatestFrame.Landmark`, экранное пространство). World для экранного оверлея не годится.

---

## 4. Ключевое решение про координаты

- **Запись/3D-разбор (CSV, `PoseReviewer`)** → **world-landmarks** (реальный 3D, совпадает с эталоном).
- **Скоринг (`PushUpAnalyzer`)** → **нормализованные** (пороги уже откалиброваны на них: up=140°,
  down=105°, сглаживание 0.6; менять НЕ надо).
- **Экранный оверлей на камере (`ExerciseSandbox`)** → **нормализованные** (image-space).

Не переводить скоринг на world без повторной калибровки — это отдельная задача.

---

## 5. Верификация

1. **Сборка APK**: `Assets/Pose/DevSandbox/Editor/AndroidBuilder.cs` → меню
   *Mikey → Dev → Build Android (Exercise Sandbox)* (ARMv7, IL2CPP, minSdk24). Установить на
   устройство (`adb install -r Builds/ExerciseSandbox.apk`).
2. **Съёмка**: зайти в Push-ups, сделать N отжиманий сбоку (тело целиком в кадр), нажать SAVE LOG.
3. **Вытянуть CSV**: `adb pull /storage/emulated/0/Android/data/com.mikey.equilibrium/files/pose_rec_*.csv`
   → положить в `Assets/PoseRecordings/pose_rec.csv`.
4. **Проверить содержимое**: значения должны быть **метрическими** (примерно −1..1, центр у таза),
   а НЕ в диапазоне 0..1 — это признак, что записались world-landmarks. Сравнить с
   `reference.csv` (там уже world, тот же масштаб).
5. **`PoseReviewer`** (меню *Create or Open Pose Review Scene* → Play): пользовательский скелет
   (jade) теперь **объёмный** при вращении камеры и сопоставим по масштабу с эталоном (orange),
   оба стоят на полу.
6. **Счёт не сломался**: REPS по вашей записи считается как раньше (скоринг на нормализованных).

---

## 6. Вне области (follow-ups, не в этом ТЗ)

- **Обрезка эталона**: `reference.csv` — весь ролик 3.6 мин (с разговорами). Отдельно вырезать
  чистый цикл отжимания (диапазон кадров) — в `tools/extract_reference.py` или постобработкой.
- **Выравнивание по фазе** двух скелетов во времени (сейчас играют независимо).
- **Перевод скоринга на world-landmarks** с повторной калибровкой порогов.
- **Низкий FPS инференса** на бюджетном устройстве (~6 fps замечен по таймстемпам записи) —
  профилировать отдельно.

---

## 7. Уже готовые артефакты (переиспользовать, не переделывать)

- `tools/extract_reference.py` — извлечение world-скелета из видео (Python+MediaPipe уже стоят).
- `Assets/PoseRecordings/reference.csv` — эталон (world), формат-эталон для CSV.
- `Assets/Pose/DevSandbox/PoseReviewer.cs` — двух-скелетный 3D-разборщик со свободной камерой и
  покадровым приземлением на пол (менять не нужно — он читает CSV как есть).
- Скоринг `PushUp*.cs` и его калибровка — **не трогать**.
