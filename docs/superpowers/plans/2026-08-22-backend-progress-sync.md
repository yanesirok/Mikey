# Backend: аккаунт Google и синхронизация прогресса — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** прогресс и профиль игрока переживают переустановку и смену телефона, вход — через Google-аккаунт.

**Architecture:** `PlayerPrefs` остаётся главным хранилищем, сервер — резервная копия; игра полностью работает без сети и без входа. Вся синхронизация — один вызов Postgres-функции `sync_progress(jsonb)`, которая сливает состояние устройства с серверным в одной транзакции и возвращает слитый результат. Прогресс сливается по максимуму, профиль — по времени правки.

**Tech Stack:** Unity 6000.3.18f1, C#, `UnityWebRequest`; Supabase (Postgres 15, PostgREST, GoTrue); Android — Credential Manager + EncryptedSharedPreferences в `.androidlib`.

**Spec:** `docs/superpowers/specs/2026-08-22-backend-progress-sync-design.md`

## Global Constraints

- Unity **6000.3.18f1**. Целевая платформа Android, IL2CPP, ARMv7, minSdk 25, пакет `com.mikey.equilibrium`.
- Supabase URL: `https://maaqlmzispqeldsjsank.supabase.co`
- Publishable key (публичный, идёт в APK): `sb_publishable_2EHwQuPnOYLwa7_BTNuK8A_VKVDwpMk`
- Google Web client ID: `404020688148-53omm395re2ueeciqccpfqr400e3bds0.apps.googleusercontent.com`
- Google Android client ID: `404020688148-elvvh0md3oudvu465qu0mp9h2s8m8o3h.apps.googleusercontent.com`
- **Секретов в репозиторий не коммитить.** `sb_secret_…`, Google client secret и пароль Postgres в код и в git не попадают ни при каких обстоятельствах.
- Локальные данные потерять нельзя: любая ошибка сети, авторизации или валидации оставляет `PlayerPrefs` нетронутым.
- Сообщений об ошибке синхронизации пользователю **не показываем** — неудавшийся бэкап не является игровым событием.
- Никаких таймеров и синхронизации по кадру. Одновременно в полёте не более одного запроса.
- Новый код — в сборке `Mikey.Backend`; сеть не должна протекать в игровые сборки.
- Стиль asmdef копируется с существующих (`Assets/Pose/Mikey.Pose.asmdef`, `Assets/UI/Progression/Tests/*.asmdef`).
- Тесты — EditMode, NUnit, как в `Assets/UI/Progression/Tests/`.

### Среда: редактор Unity уже открыт

Проект держит **запущенный редактор** (порт 7801, при переподключении может стать 7800).
Из этого три следствия, каждое стоило отдельного разбирательства:

- **`unity test --project …` использовать нельзя** — этот синтаксис устарел для установленной
  версии CLI и вдобавок пытается взять блокировку проекта, которую держит открытый редактор.
  Тесты гонять через подключённый редактор: `unity command run_tests …`.
- **Длинные прогоны асинхронны.** `run_tests` без фильтра возвращает управление сразу, а
  вызов CLI отваливается по таймауту в 30 секунд — это не ошибка. Опрашивать
  `unity command test_status --format json` до `completed`. Прервать можно
  `unity command cancel_tests`.
- **Не вызывать `run_tests` повторно, пока прогон идёт.** Новый вызов отменяет предыдущий
  на стороне сервера, и оба остаются без результата — со стороны это выглядит как
  бесконечно висящий прогон. Запустить один раз, затем читать итог из `TestResults.xml`
  или из консоли редактора, а не перезапускать.
- **Новые файлы редактор сам не импортирует.** После создания исходников —
  `unity --json cmd eval 'UnityEditor.AssetDatabase.Refresh(); return "ok";'`, иначе `.meta`
  не появятся и в коммит уйдут файлы без идентификаторов. Добавление файлов вызывает
  перезагрузку домена, во время которой CLI на несколько секунд недоступен: подождать и
  повторить.

### Добавления по имени файла недостаточно, если файл уже изменён

`git add <путь>` забирает **все** изменения файла, а не только твои. В рабочей копии
больше сотни посторонних незакоммиченных правок, и `Assets/UI/MikeyApp.uxml` — одна из
них: там лежит переделка экрана камеры, к этой работе отношения не имеющая.

Перед коммитом файла, который уже был изменён, посмотри `git diff --stat <путь>`. Если
размер правки заметно больше твоей — забирай только свой фрагмент: извлеки его
(`git diff` в файл, отредактируй) и примени `git apply --cached`. После коммита проверь
`git show --stat`, что размер совпадает с ожидаемым, а чужие изменения остались в рабочей
копии.

### Существующие файлы не перезаписывать

Там, где план говорит «создать» тестовый файл, он может уже существовать с чужими тестами —
так было с `ProfileUserDataStorageTests`, где лежало ещё одиннадцать проверок из прошлой
работы. **Сначала проверить наличие, при наличии — дописать свои тесты в существующий
класс.** Перезапись молча уничтожает чужую работу, и ни один тест этого не поймает.

### Известный падающий тест, не связанный с этой работой

`Mikey.Fight.Tests.FightSceneTests.Fighters_WearTheirOwnModelAndAvatar` падает давно: оба
бойца в `FightSandbox.unity` используют одну модель, а тест требует разные. Не чинить, своей
поломкой не считать. Ориентир полного прогона на начало работы — **1219 из 1220**.

---

## Файловая структура

**Фаза A — база (самостоятельно работающий и проверяемый кусок).**

| Файл | Ответственность |
|---|---|
| `supabase/migrations/0001_schema.sql` | три таблицы, CHECK-ограничения, RLS, гранты |
| `supabase/migrations/0002_sync.sql` | `sync_progress(jsonb)` — слияние |
| `supabase/migrations/0003_delete_account.sql` | `delete_account()` |
| `supabase/tests/sync_progress.sql` | assert-тест слияния и изоляции пользователей |

**Фаза B — чистое ядро клиента.**

| Файл | Ответственность |
|---|---|
| `Assets/Backend/Mikey.Backend.asmdef` | сборка |
| `Assets/Backend/SyncState.cs` | DTO запроса и ответа, имена полей = имена колонок |
| `Assets/Backend/SyncPayload.cs` | сборка запроса из хранилищ и применение ответа. Без вызовов UnityEngine |
| `Assets/Backend/SupabaseConfig.cs` | ScriptableObject: URL и publishable-ключ |
| `Assets/Backend/Tests/…` | EditMode-тесты |

**Фаза C — сессия.**

| Файл | Ответственность |
|---|---|
| `Assets/Backend/ITokenStore.cs` | интерфейс хранения refresh-токена |
| `Assets/Backend/MemoryTokenStore.cs` | реализация для редактора и тестов |
| `Assets/Backend/SupabaseSession.cs` | токены, арифметика истечения, решение «пора обновлять» |

**Фаза D — Android.**

| Файл | Ответственность |
|---|---|
| `Assets/Plugins/Android/MikeyAuth.androidlib/**` | Credential Manager + EncryptedSharedPreferences |
| `Assets/Backend/IAuthGateway.cs` | интерфейс входа |
| `Assets/Backend/AndroidGoogleAuth.cs` | мост к плагину |
| `Assets/Backend/AndroidTokenStore.cs` | мост к шифрованному хранилищу |

**Фаза E — связывание.**

| Файл | Ответственность |
|---|---|
| `Assets/Backend/SupabaseClient.cs` | HTTP: обмен ID-токена, обновление, вызов RPC |
| `Assets/Backend/SyncService.cs` | когда синхронизировать, обработка ошибок |
| `Assets/UI/Profile/ProfileUserData.cs` | **правка:** поле `UpdatedAtIso` |
| `Assets/UI/Profile/ProfileUserDataStorage.cs` | **правка:** штамп времени в `Save` |
| `Assets/UI/MikeyApp.uxml` | **правка:** блок аккаунта на экране `profileDetails` |
| `Assets/Scenes/SampleScene.unity` | **правка:** `SyncService` на GameObject `UI` |

---

# Фаза A — база

### Task 1: Схема, ограничения и доступы

**Files:**
- Create: `supabase/migrations/0001_schema.sql`

**Interfaces:**
- Consumes: ничего.
- Produces: таблицы `public.profiles`, `public.level0_results`, `public.level1_progress` с колонками ровно теми именами, что перечислены ниже — Task 2 и Task 6 обращаются к ним по этим именам.

Замечание для исполнителя: значения по умолчанию для возраста, веса и роста в приложении — нули (`ProfileUserData` объявляет `public int Age;` без инициализатора). Поэтому CHECK обязан пропускать 0 наравне с валидным диапазоном, иначе первый же синк не заполненного профиля упадёт.

Второе замечание: при создании проекта выключено «Automatically expose new tables», поэтому таблицы не выдаются наружу автоматически, и мы намеренно **не** выдаём на них гранты. Клиент дотягивается до данных только через две функции. RLS-политики всё равно заводим — как второй рубеж на случай, если гранты когда-нибудь добавят.

- [ ] **Step 1: Написать миграцию**

Создать `supabase/migrations/0001_schema.sql`:

```sql
-- Профиль. Возраст/вес/рост допускают 0 = «не заполнено»: приложение хранит
-- незаполненные поля нулями (ProfileUserData), и первый синк присылает именно их.
create table if not exists public.profiles (
  id                 uuid primary key references auth.users(id) on delete cascade,
  display_name       text not null default 'Mikey',
  gender             text not null default '',
  age                int  not null default 0 check (age = 0 or age between 10 and 100),
  weight_kg          real not null default 0 check (weight_kg = 0 or weight_kg between 30 and 300),
  height_cm          int  not null default 0 check (height_cm = 0 or height_cm between 100 and 250),
  tutorial_progress  int  not null default 0 check (tutorial_progress between 0 and 10),
  profile_updated_at timestamptz not null default now(),
  created_at         timestamptz not null default now()
);

create table if not exists public.level0_results (
  user_id               uuid primary key references auth.users(id) on delete cascade,
  pushup_reps           int  not null default 0 check (pushup_reps >= 0),
  squat_reps            int  not null default 0 check (squat_reps >= 0),
  yokogeri_slow_reps    int  not null default 0 check (yokogeri_slow_reps >= 0),
  yokogeri_best_zone    int  not null default 0 check (yokogeri_best_zone >= 0),
  wallsit_seconds       real not null default 0 check (wallsit_seconds >= 0),
  yokogeri_hold_seconds real not null default 0 check (yokogeri_hold_seconds >= 0),
  updated_at            timestamptz not null default now()
);

-- Строка на технику, а не JSON-колонка: «сколько людей освоило технику» должно
-- быть обычным GROUP BY. technique_id — свободный текст: добавление упражнения
-- в ExerciseCatalog не должно требовать миграции базы.
create table if not exists public.level1_progress (
  user_id      uuid not null references auth.users(id) on delete cascade,
  technique_id text not null check (length(technique_id) between 1 and 64),
  clean_reps   int  not null default 0 check (clean_reps >= 0),
  updated_at   timestamptz not null default now(),
  primary key (user_id, technique_id)
);

alter table public.profiles        enable row level security;
alter table public.level0_results  enable row level security;
alter table public.level1_progress enable row level security;

create policy profiles_own on public.profiles
  for all to authenticated using (auth.uid() = id) with check (auth.uid() = id);

create policy level0_own on public.level0_results
  for all to authenticated using (auth.uid() = user_id) with check (auth.uid() = user_id);

create policy level1_own on public.level1_progress
  for all to authenticated using (auth.uid() = user_id) with check (auth.uid() = user_id);

-- Гранты на таблицы НЕ выдаются намеренно: клиент ходит только через функции.
grant usage on schema public to authenticated;
```

- [ ] **Step 2: Выполнить миграцию**

Открыть SQL Editor проекта: `https://supabase.com/dashboard/project/maaqlmzispqeldsjsank/sql/new`, вставить содержимое файла целиком, нажать Run.
Ожидается: `Success. No rows returned`.

- [ ] **Step 3: Проверить, что схема встала и доступы закрыты**

Выполнить в SQL Editor:

```sql
select tablename, rowsecurity
from pg_tables
where schemaname = 'public'
order by tablename;

select has_table_privilege('authenticated', 'public.profiles', 'select') as authenticated_can_read_profiles;
```

Ожидается: три строки `level0_results`, `level1_progress`, `profiles`, у всех `rowsecurity = true`; `authenticated_can_read_profiles = false`.

- [ ] **Step 4: Проверить, что ограничения действительно отбивают мусор**

Выполнить в SQL Editor:

```sql
begin;
insert into auth.users (id, instance_id, aud, role, email, encrypted_password, created_at, updated_at)
values ('11111111-1111-1111-1111-111111111111',
        '00000000-0000-0000-0000-000000000000',
        'authenticated', 'authenticated', 'check1@example.test', '', now(), now());

-- 0 разрешён (не заполнено)
insert into public.profiles (id, age, weight_kg, height_cm) values
  ('11111111-1111-1111-1111-111111111111', 0, 0, 0);

-- а 9 лет — нет
do $$
begin
  update public.profiles set age = 9 where id = '11111111-1111-1111-1111-111111111111';
  raise exception 'CHECK не сработал: возраст 9 принят';
exception when check_violation then
  raise notice 'OK: возраст 9 отклонён';
end $$;
rollback;
```

Ожидается: `NOTICE: OK: возраст 9 отклонён`, транзакция откачена.

- [ ] **Step 5: Коммит**

```bash
git add supabase/migrations/0001_schema.sql
git commit -m "feat(backend): схема profiles/level0_results/level1_progress с RLS

Гранты на таблицы намеренно не выдаются: клиент ходит только через
функции. CHECK на возраст/вес/рост пропускает 0 — приложение хранит
незаполненные поля нулями, и первый синк присылает именно их."
```

---

### Task 2: Функция слияния `sync_progress`

**Files:**
- Create: `supabase/migrations/0002_sync.sql`

**Interfaces:**
- Consumes: таблицы из Task 1.
- Produces: `public.sync_progress(payload jsonb) returns jsonb`. Принимает объект вида
  `{"profile":{…},"level0":{…},"level1":[{"technique_id":"…","clean_reps":N}]}` и возвращает
  объект **той же формы**. Task 8 (`SyncState`) и Task 12 (`SupabaseClient`) полагаются на эту форму дословно.

Замечание: функция `security definer`, то есть выполняется от владельца и RLS обходит. Защиту даёт `auth.uid()` из токена — идентификатор пользователя клиент не передаёт и передать не может. Это единственный рубеж на этом пути, поэтому проверка `uid is null` обязательна.

- [ ] **Step 1: Написать миграцию**

Создать `supabase/migrations/0002_sync.sql`:

```sql
create or replace function public.sync_progress(payload jsonb)
returns jsonb
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
  uid         uuid := auth.uid();
  p           jsonb := coalesce(payload->'profile', '{}'::jsonb);
  l0          jsonb := coalesce(payload->'level0',  '{}'::jsonb);
  t           jsonb;
  tid         text;
  incoming_ts timestamptz;
  result      jsonb;
begin
  -- Личность берётся ТОЛЬКО из токена. Без неё работать нельзя.
  if uid is null then
    raise exception 'not authenticated' using errcode = '28000';
  end if;

  -- Пустой штамп = «время правки неизвестно»: тогда серверная версия профиля
  -- всегда новее и локальная ничего не затирает.
  incoming_ts := coalesce(nullif(p->>'profile_updated_at', '')::timestamptz,
                          'epoch'::timestamptz);

  -- Профиль: правки — последняя запись побеждает; прогресс туториала — максимум.
  insert into public.profiles as pr
    (id, display_name, gender, age, weight_kg, height_cm, tutorial_progress, profile_updated_at)
  values
    (uid,
     coalesce(p->>'display_name', 'Mikey'),
     coalesce(p->>'gender', ''),
     coalesce((p->>'age')::int, 0),
     coalesce((p->>'weight_kg')::real, 0),
     coalesce((p->>'height_cm')::int, 0),
     coalesce((p->>'tutorial_progress')::int, 0),
     incoming_ts)
  on conflict (id) do update set
    display_name = case when excluded.profile_updated_at > pr.profile_updated_at
                        then excluded.display_name else pr.display_name end,
    gender       = case when excluded.profile_updated_at > pr.profile_updated_at
                        then excluded.gender       else pr.gender       end,
    age          = case when excluded.profile_updated_at > pr.profile_updated_at
                        then excluded.age          else pr.age          end,
    weight_kg    = case when excluded.profile_updated_at > pr.profile_updated_at
                        then excluded.weight_kg    else pr.weight_kg    end,
    height_cm    = case when excluded.profile_updated_at > pr.profile_updated_at
                        then excluded.height_cm    else pr.height_cm    end,
    tutorial_progress  = greatest(pr.tutorial_progress, excluded.tutorial_progress),
    profile_updated_at = greatest(pr.profile_updated_at, excluded.profile_updated_at);

  -- Уровень 0: только максимум. Слабый повтор не понижает результат.
  insert into public.level0_results as r
    (user_id, pushup_reps, squat_reps, yokogeri_slow_reps,
     yokogeri_best_zone, wallsit_seconds, yokogeri_hold_seconds, updated_at)
  values
    (uid,
     coalesce((l0->>'pushup_reps')::int, 0),
     coalesce((l0->>'squat_reps')::int, 0),
     coalesce((l0->>'yokogeri_slow_reps')::int, 0),
     coalesce((l0->>'yokogeri_best_zone')::int, 0),
     coalesce((l0->>'wallsit_seconds')::real, 0),
     coalesce((l0->>'yokogeri_hold_seconds')::real, 0),
     now())
  on conflict (user_id) do update set
    pushup_reps           = greatest(r.pushup_reps,           excluded.pushup_reps),
    squat_reps            = greatest(r.squat_reps,            excluded.squat_reps),
    yokogeri_slow_reps    = greatest(r.yokogeri_slow_reps,    excluded.yokogeri_slow_reps),
    yokogeri_best_zone    = greatest(r.yokogeri_best_zone,    excluded.yokogeri_best_zone),
    wallsit_seconds       = greatest(r.wallsit_seconds,       excluded.wallsit_seconds),
    yokogeri_hold_seconds = greatest(r.yokogeri_hold_seconds, excluded.yokogeri_hold_seconds),
    updated_at            = now();

  -- Уровень 1: тоже максимум, по технике. Запись без id молча пропускается —
  -- кривой элемент не должен ронять весь синк.
  for t in select value from jsonb_array_elements(coalesce(payload->'level1', '[]'::jsonb))
  loop
    tid := t->>'technique_id';
    continue when tid is null or length(tid) = 0 or length(tid) > 64;

    insert into public.level1_progress as lp (user_id, technique_id, clean_reps, updated_at)
    values (uid, tid, greatest(coalesce((t->>'clean_reps')::int, 0), 0), now())
    on conflict (user_id, technique_id) do update set
      clean_reps = greatest(lp.clean_reps, excluded.clean_reps),
      updated_at = now();
  end loop;

  -- Возвращаем слитое состояние в той же форме, что приняли.
  select jsonb_build_object(
    'profile', jsonb_build_object(
      'display_name',       pr.display_name,
      'gender',             pr.gender,
      'age',                pr.age,
      'weight_kg',          pr.weight_kg,
      'height_cm',          pr.height_cm,
      'tutorial_progress',  pr.tutorial_progress,
      'profile_updated_at', to_char(pr.profile_updated_at at time zone 'UTC',
                                    'YYYY-MM-DD"T"HH24:MI:SS"Z"')),
    'level0', jsonb_build_object(
      'pushup_reps',           r.pushup_reps,
      'squat_reps',            r.squat_reps,
      'yokogeri_slow_reps',    r.yokogeri_slow_reps,
      'yokogeri_best_zone',    r.yokogeri_best_zone,
      'wallsit_seconds',       r.wallsit_seconds,
      'yokogeri_hold_seconds', r.yokogeri_hold_seconds),
    'level1', coalesce((
      select jsonb_agg(jsonb_build_object('technique_id', technique_id,
                                          'clean_reps',   clean_reps)
                       order by technique_id)
      from public.level1_progress where user_id = uid), '[]'::jsonb)
  )
  into result
  from public.profiles pr
  join public.level0_results r on r.user_id = pr.id
  where pr.id = uid;

  return result;
end;
$$;

revoke all on function public.sync_progress(jsonb) from public;
grant execute on function public.sync_progress(jsonb) to authenticated;
```

- [ ] **Step 2: Выполнить миграцию**

Вставить файл целиком в SQL Editor, нажать Run.
Ожидается: `Success. No rows returned`.

- [ ] **Step 3: Проверить, что анонимному вызывающему функция недоступна**

```sql
select has_function_privilege('anon', 'public.sync_progress(jsonb)', 'execute') as anon_can_call,
       has_function_privilege('authenticated', 'public.sync_progress(jsonb)', 'execute') as authed_can_call;
```

Ожидается: `anon_can_call = false`, `authed_can_call = true`.

- [ ] **Step 4: Коммит**

```bash
git add supabase/migrations/0002_sync.sql
git commit -m "feat(backend): sync_progress — атомарное слияние состояния

Прогресс сливается через GREATEST, профиль — по profile_updated_at.
Пустой штамп трактуется как epoch, чтобы незаполненная локальная копия
не затирала серверную. Личность берётся только из auth.uid()."
```

---

### Task 3: Удаление аккаунта

**Files:**
- Create: `supabase/migrations/0003_delete_account.sql`

**Interfaces:**
- Consumes: таблицы из Task 1.
- Produces: `public.delete_account() returns void`. Task 13 вызывает её по этому имени.

- [ ] **Step 1: Написать миграцию**

Создать `supabase/migrations/0003_delete_account.sql`:

```sql
-- Удаляет пользователя целиком. Три таблицы уносит каскадом
-- (on delete cascade на auth.users), отдельная уборка не нужна.
create or replace function public.delete_account()
returns void
language plpgsql
security definer
set search_path = public, auth, pg_temp
as $$
declare
  uid uuid := auth.uid();
begin
  if uid is null then
    raise exception 'not authenticated' using errcode = '28000';
  end if;

  delete from auth.users where id = uid;
end;
$$;

revoke all on function public.delete_account() from public;
grant execute on function public.delete_account() to authenticated;
```

- [ ] **Step 2: Выполнить миграцию**

Вставить в SQL Editor, Run. Ожидается: `Success. No rows returned`.

- [ ] **Step 3: Проверить каскад**

**Важно про вывод:** SQL Editor Supabase **не показывает сообщения `raise notice`** — в панели
результатов их нет. Поэтому успех фиксируется строкой во временной таблице, а скрипт
завершается `select` из неё **до** `rollback`: возвращённые строки видно. Падения по-прежнему
через `raise exception`, они видны и так.

```sql
begin;
create temp table check_log(step text) on commit drop;

insert into auth.users (id, instance_id, aud, role, email, encrypted_password, created_at, updated_at)
values ('22222222-2222-2222-2222-222222222222',
        '00000000-0000-0000-0000-000000000000',
        'authenticated', 'authenticated', 'cascade@example.test', '', now(), now());

insert into public.profiles (id) values ('22222222-2222-2222-2222-222222222222');
insert into public.level0_results (user_id, pushup_reps) values ('22222222-2222-2222-2222-222222222222', 7);
insert into public.level1_progress (user_id, technique_id, clean_reps)
values ('22222222-2222-2222-2222-222222222222', 'stance-zenkutsu', 3);

delete from auth.users where id = '22222222-2222-2222-2222-222222222222';

do $$
declare n int;
begin
  select (select count(*) from public.profiles        where id      = '22222222-2222-2222-2222-222222222222')
       + (select count(*) from public.level0_results  where user_id = '22222222-2222-2222-2222-222222222222')
       + (select count(*) from public.level1_progress where user_id = '22222222-2222-2222-2222-222222222222')
    into n;
  if n <> 0 then raise exception 'каскад не сработал, осталось строк: %', n; end if;
  insert into check_log values ('OK: каскад унёс все три таблицы');
end $$;

select * from check_log;
rollback;
```

Ожидается: одна строка результата `OK: каскад унёс все три таблицы`. Пустой результат или
ошибка означают, что каскад не отработал.

- [ ] **Step 4: Коммит**

```bash
git add supabase/migrations/0003_delete_account.sql
git commit -m "feat(backend): delete_account — удаление пользователя с каскадом"
```

---

### Task 4: SQL-тест слияния и изоляции

**Files:**
- Create: `supabase/tests/sync_progress.sql`

**Interfaces:**
- Consumes: `sync_progress` из Task 2.
- Produces: ничего для кода; это проверка, которая падает, если слияние сломали.

Замечание: `auth.uid()` читает `request.jwt.claims`, поэтому тест подменяет claim, чтобы притвориться конкретным пользователем. Весь скрипт — одна транзакция, которая в конце откатывается, поэтому в базе после него не остаётся ничего.

- [ ] **Step 1: Написать тест**

Создать `supabase/tests/sync_progress.sql`:

```sql
-- Прогоняется целиком в SQL Editor. Всё внутри одной транзакции и откатывается.
--
-- Успех каждой проверки пишется строкой в check_log, а не через raise notice:
-- SQL Editor Supabase сообщения NOTICE не показывает вовсе, и «ошибки не было»
-- слишком слабое свидетельство для теста, который защищает данные людей.
begin;
create temp table check_log(step text) on commit drop;

insert into auth.users (id, instance_id, aud, role, email, encrypted_password, created_at, updated_at)
values
  ('aaaaaaaa-0000-0000-0000-000000000001',
   '00000000-0000-0000-0000-000000000000', 'authenticated','authenticated','a@example.test','',now(),now()),
  ('bbbbbbbb-0000-0000-0000-000000000002',
   '00000000-0000-0000-0000-000000000000', 'authenticated','authenticated','b@example.test','',now(),now()),
  ('cccccccc-0000-0000-0000-000000000003',
   '00000000-0000-0000-0000-000000000000', 'authenticated','authenticated','c@example.test','',now(),now());

-- ---- Пользователь A ----
set local request.jwt.claims = '{"sub":"aaaaaaaa-0000-0000-0000-000000000001","role":"authenticated"}';

select public.sync_progress('{
  "profile": {"display_name":"Дима","gender":"Male","age":21,"weight_kg":70,"height_cm":180,
              "tutorial_progress":3,"profile_updated_at":"2026-08-20T10:00:00Z"},
  "level0":  {"pushup_reps":20,"squat_reps":30,"wallsit_seconds":45},
  "level1":  [{"technique_id":"stance-zenkutsu","clean_reps":4}]
}'::jsonb);

-- Слабый повтор не должен понизить результат, а профиль со СТАРЫМ штампом
-- не должен затереть более свежее имя.
select public.sync_progress('{
  "profile": {"display_name":"ЗАТЁРТО","age":21,"tutorial_progress":2,
              "profile_updated_at":"2026-08-19T10:00:00Z"},
  "level0":  {"pushup_reps":5,"squat_reps":31},
  "level1":  [{"technique_id":"stance-zenkutsu","clean_reps":1},
              {"technique_id":"","clean_reps":9}]
}'::jsonb);

do $$
declare r record; n int;
begin
  select * into r from public.profiles where id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if r.display_name <> 'Дима' then
    raise exception 'профиль: старый штамп затёр имя -> %', r.display_name;
  end if;
  if r.tutorial_progress <> 3 then
    raise exception 'прогресс туториала откатился -> %', r.tutorial_progress;
  end if;

  select * into r from public.level0_results where user_id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if r.pushup_reps <> 20 then raise exception 'отжимания понизились -> %', r.pushup_reps; end if;
  if r.squat_reps  <> 31 then raise exception 'приседания не выросли -> %', r.squat_reps;  end if;
  if r.wallsit_seconds <> 45 then raise exception 'стенка потерялась -> %', r.wallsit_seconds; end if;

  if (select clean_reps from public.level1_progress
      where user_id = 'aaaaaaaa-0000-0000-0000-000000000001'
        and technique_id = 'stance-zenkutsu') <> 4 then
    raise exception 'уровень 1 понизился';
  end if;

  select count(*) into n from public.level1_progress
   where user_id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if n <> 1 then raise exception 'пустой technique_id не был пропущен, строк: %', n; end if;

  insert into check_log values ('OK: слияние берёт максимум и уважает штамп времени');
end $$;

-- ---- Регрессии на три дефекта, найденных ревью Task 2 ----
--
-- ВАЖНО: внутри одной транзакции Postgres замораживает now(). Поэтому проверить
-- «после будущего штампа настоящая правка всё ещё проходит» здесь невозможно —
-- любой последующий штамп будет не строго больше замороженного now(). Проверяем
-- напрямую то, что чинит дефект: штамп срезается до now() при записи.

-- (а) Штамп из будущего срезается и не может заморозить профиль.
select public.sync_progress('{"profile":{"display_name":"Из будущего",
                                         "profile_updated_at":"2030-01-01T00:00:00Z"}}'::jsonb);

do $$
begin
  if (select profile_updated_at from public.profiles
      where id = 'aaaaaaaa-0000-0000-0000-000000000001') > now() then
    raise exception 'штамп из будущего сохранён как есть — профиль заморожен навсегда';
  end if;
  insert into check_log values ('OK: штамп из будущего срезан до now()');
end $$;

-- (б) NaN не должен пролезать в результат: в Postgres NaN больше всех чисел,
--     и один раз попав в колонку, он выигрывал бы greatest вечно.
do $$
declare landed boolean := false;
begin
  begin
    perform public.sync_progress('{"level0":{"wallsit_seconds":"NaN"}}'::jsonb);
    landed := true;
  exception when others then
    null; -- отказ и есть ожидаемое поведение
  end;

  if landed and (select wallsit_seconds from public.level0_results
                 where user_id = 'aaaaaaaa-0000-0000-0000-000000000001') <> 45 then
    raise exception 'NaN пролез в wallsit_seconds — рекорд больше не опустить';
  end if;
  insert into check_log values ('OK: NaN не попадает в результат');
end $$;

-- (в) Негодное число в профиле не должно ронять прогресс из того же запроса.
--     Возраст 8 и вес 25 для детского карате — обычные значения.
--     Берём ОТДЕЛЬНОГО пользователя C: у A профиль уже записан выше со
--     штампом now(), и в одной транзакции его нечем перекрыть — проверка
--     обнуления возраста на нём была бы недостоверной.
set local request.jwt.claims = '{"sub":"cccccccc-0000-0000-0000-000000000003","role":"authenticated"}';

select public.sync_progress('{"profile":{"age":8,"weight_kg":25,
                                         "profile_updated_at":"2026-08-22T13:00:00Z"},
                              "level0":{"pushup_reps":99}}'::jsonb);

do $$
begin
  if (select pushup_reps from public.level0_results
      where user_id = 'cccccccc-0000-0000-0000-000000000003') <> 99 then
    raise exception 'кривая цифра в профиле откатила прогресс: отжимания не сохранились';
  end if;
  if (select age from public.profiles
      where id = 'cccccccc-0000-0000-0000-000000000003') <> 0 then
    raise exception 'негодный возраст сохранён вместо обнуления';
  end if;
  insert into check_log values ('OK: кривой профиль не роняет прогресс');
end $$;

-- ---- Пользователь B не видит и не трогает данные A ----
set local request.jwt.claims = '{"sub":"bbbbbbbb-0000-0000-0000-000000000002","role":"authenticated"}';

-- B пишет ЗАВЕДОМО БОЛЬШЕЕ значение, чем у A (999 против 20). Это принципиально:
-- при меньшем значении утечка чужой записи в строку A была бы невидима — greatest
-- вернул бы прежние 20, и проверка отчиталась бы успехом при полностью потерянной
-- изоляции. Ловится только запись, которая способна перебить чужой максимум.
select public.sync_progress('{"profile":{"display_name":"Б"},"level0":{"pushup_reps":999},"level1":[]}'::jsonb);

do $$
begin
  -- `is distinct from`, а не `<>`: отсутствующая строка даёт NULL, а `if NULL then`
  -- в plpgsql это ветка «нет» — сравнение через `<>` молча пропустило бы и случай,
  -- когда строки нет вовсе.
  if (select pushup_reps from public.level0_results
      where user_id = 'aaaaaaaa-0000-0000-0000-000000000001') is distinct from 20 then
    raise exception 'ИЗОЛЯЦИЯ НАРУШЕНА: синк B изменил данные A';
  end if;
  if (select pushup_reps from public.level0_results
      where user_id = 'bbbbbbbb-0000-0000-0000-000000000002') is distinct from 999 then
    raise exception 'данные B не записались или ушли не в ту строку';
  end if;
  insert into check_log values ('OK: пользователи изолированы');
end $$;

-- ---- Без токена функция обязана отказать ----
set local request.jwt.claims = '';
do $$
declare refused boolean := false;
begin
  -- Вложенный блок ловит ТОЛЬКО ошибку самого вызова. Если поймать «любую
  -- ошибку» снаружи, то собственное «ПРОВАЛ» ниже тоже будет поймано, и
  -- провалившийся тест отчитается успехом.
  --
  -- Ловим любой код, а не только 28000: auth.uid() приводит claims к json и на
  -- пустой строке может упасть ошибкой приведения. Проверяемое свойство —
  -- «без токена не отрабатывает», а не конкретный код ошибки.
  begin
    perform public.sync_progress('{}'::jsonb);
  exception when others then
    refused := true;
  end;

  if not refused then
    raise exception 'ПРОВАЛ: функция отработала без аутентификации';
  end if;

  insert into check_log values ('OK: без токена отказано');
end $$;

select * from check_log;
rollback;
```

- [ ] **Step 2: Прогнать тест**

Вставить файл целиком в SQL Editor, Run.
Ожидается таблица результата ровно из шести строк:
```
OK: слияние берёт максимум и уважает штамп времени
OK: штамп из будущего не замораживает профиль
OK: NaN не попадает в результат
OK: кривой профиль не роняет прогресс
OK: пользователи изолированы
OK: без токена отказано
```
Меньше шести строк или `ERROR` означают сломанное слияние — чинить Task 2, а не тест.
Три средние строки — регрессии на дефекты, найденные ревью Task 2; если падает одна из
них, значит правка того раунда отменена или не доехала до живой базы.

- [ ] **Step 3: Коммит**

```bash
git add supabase/tests/sync_progress.sql
git commit -m "test(backend): проверка слияния, изоляции пользователей и отказа без токена"
```

---

# Фаза B — чистое ядро клиента

### Task 5: Сборка `Mikey.Backend` и DTO

**Files:**
- Create: `Assets/Backend/Mikey.Backend.asmdef`
- Create: `Assets/Backend/SyncState.cs`

**Interfaces:**
- Consumes: типы `Mikey.Pose`, `Mikey.UI.Profile`, `Mikey.UI.Progression`.
- Produces: `Mikey.Backend.SyncState` с полями `profile` (`SyncProfile`), `level0` (`SyncLevel0`),
  `level1` (`List<SyncTechnique>`). Все следующие задачи используют именно эти имена.

Замечание: имена полей — snake_case, потому что `JsonUtility` сопоставляет поля по имени буквально, а на той стороне это имена колонок Postgres. Отступление от C#-стиля здесь осознанное и локализовано в одном файле.

- [ ] **Step 1: Создать asmdef**

Создать `Assets/Backend/Mikey.Backend.asmdef`:

```json
{
    "name": "Mikey.Backend",
    "rootNamespace": "Mikey.Backend",
    "references": [
        "Mikey.Pose",
        "Mikey.UI.Profile",
        "Mikey.UI.Progression",
        "Mikey.UI.SafeArea"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Создать DTO**

Создать `Assets/Backend/SyncState.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Mikey.Backend
{
    /// <summary>
    /// Форма запроса и ответа <c>sync_progress</c>. Имена полей — snake_case
    /// намеренно: JsonUtility сопоставляет поля по имени буквально, а на той
    /// стороне это имена колонок Postgres. Отступление от C#-стиля локализовано
    /// в этом файле и нигде больше не расползается.
    /// </summary>
    [Serializable]
    public sealed class SyncProfile
    {
        public string display_name = string.Empty;
        public string gender = string.Empty;
        public int age;
        public float weight_kg;
        public int height_cm;
        public int tutorial_progress;
        public string profile_updated_at = string.Empty;
    }

    [Serializable]
    public sealed class SyncLevel0
    {
        public int pushup_reps;
        public int squat_reps;
        public int yokogeri_slow_reps;
        public int yokogeri_best_zone;
        public float wallsit_seconds;
        public float yokogeri_hold_seconds;
    }

    [Serializable]
    public sealed class SyncTechnique
    {
        public string technique_id = string.Empty;
        public int clean_reps;
    }

    [Serializable]
    public sealed class SyncState
    {
        public SyncProfile profile = new SyncProfile();
        public SyncLevel0 level0 = new SyncLevel0();
        public List<SyncTechnique> level1 = new List<SyncTechnique>();
    }
}
```

- [ ] **Step 3: Проверить, что проект компилируется**

Выполнить:
```bash
unity --json cmd eval 'return "compiled";'
```
Ожидается: `"result": "compiled"`. Если Unity сообщает об ошибках компиляции — исправить до перехода к следующему шагу.

- [ ] **Step 4: Коммит**

```bash
git add Assets/Backend/Mikey.Backend.asmdef Assets/Backend/Mikey.Backend.asmdef.meta \
        Assets/Backend/SyncState.cs Assets/Backend/SyncState.cs.meta
git commit -m "feat(backend): сборка Mikey.Backend и DTO синхронизации"
```

---

### Task 6: Штамп времени правки профиля

**Files:**
- Modify: `Assets/UI/Profile/ProfileUserData.cs`
- Modify: `Assets/UI/Profile/ProfileUserDataStorage.cs`
- Test: `Assets/UI/Profile/Tests/ProfileUserDataStorageTests.cs`

**Interfaces:**
- Produces: `ProfileUserData.UpdatedAtIso` (string, ISO-8601 UTC вида `2026-08-22T01:30:00Z`),
  проставляется в `ProfileUserDataStorage.Save`. Task 7 читает это поле.

Замечание: правило «профиль побеждает по времени правки» требует штампа, а его сейчас нет. Штамп кладётся рядом с данными, которые он описывает, и проставляется в единственном месте записи — так его нельзя забыть проставить из нового вызывающего кода. Старые сохранения без поля читаются нормально: `JsonUtility` оставит значение по умолчанию.

- [ ] **Step 1: Написать падающий тест**

Создать `Assets/UI/Profile/Tests/ProfileUserDataStorageTests.cs`:

```csharp
using System;
using System.Globalization;
using Mikey.UI.Profile;
using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Штамп времени правки — основа правила «профиль побеждает по свежести».
    /// Если Save перестанет его ставить, синхронизация начнёт молча терять правки.
    /// </summary>
    public class ProfileUserDataStorageTests
    {
        [TearDown]
        public void ClearKey() => PlayerPrefs.DeleteKey(ProfileUserDataStorage.PlayerPrefsKey);

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
            // Намеренно НЕ «сохранить дважды и сравнить»: точность штампа — секунда,
            // два сохранения подряд попадают в одну и ту же, строки выходят равными,
            // и такой тест проходит, ничего не проверив. Здесь старое значение
            // заведомо отличается от нового, и отличие детерминировано.
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
```

Создать `Assets/UI/Profile/Tests/Mikey.UI.Profile.Tests.asmdef`, если его ещё нет:

```json
{
    "name": "Mikey.UI.Profile.Tests",
    "rootNamespace": "Mikey.UI.Profile.Tests",
    "references": [
        "Mikey.UI.Profile",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Убедиться, что тест падает**

```bash
unity command run_tests --mode EditMode --filter "ProfileUserDataStorageTests" --filter_type testName --format json
```
Ожидается: FAIL — `ProfileUserData` не содержит `UpdatedAtIso` (ошибка компиляции теста).

- [ ] **Step 3: Добавить поле**

В `Assets/UI/Profile/ProfileUserData.cs` после `public int HeightCm;` добавить:

```csharp
        /// <summary>
        /// Время последней правки, ISO-8601 UTC. Проставляется в
        /// <see cref="ProfileUserDataStorage.Save"/> и служит арбитром при
        /// синхронизации: профиль — это правки, а не рекорды, поэтому побеждает
        /// более свежая версия, а не большее число. Пусто у сохранений,
        /// сделанных до появления синхронизации.
        /// </summary>
        public string UpdatedAtIso = string.Empty;
```

- [ ] **Step 4: Проставлять штамп при записи**

В `Assets/UI/Profile/ProfileUserDataStorage.cs` заменить строку

```csharp
        public static void Save(ProfileUserData data) => PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(data));
```

на

```csharp
        /// <summary>
        /// Сохранение правки пользователя: штамп времени ставится здесь, а не у
        /// вызывающих — забыть его из нового кода невозможно.
        /// </summary>
        public static void Save(ProfileUserData data)
        {
            if (data == null)
                return;

            data.UpdatedAtIso = System.DateTime.UtcNow.ToString(
                "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            Write(data);
        }

        /// <summary>
        /// Сохранение копии, принятой с сервера: штамп НЕ обновляется, иначе
        /// принятая чужая правка тут же выглядела бы как своя свежая, и это
        /// устройство навсегда стало бы «самым новым» — слияние по времени
        /// перестало бы работать.
        /// </summary>
        public static void SaveSynced(ProfileUserData data)
        {
            if (data == null)
                return;
            Write(data);
        }

        private static void Write(ProfileUserData data)
        {
            PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
```

- [ ] **Step 5: Прогнать тест**

```bash
unity command run_tests --mode EditMode --filter "ProfileUserDataStorageTests" --filter_type testName --format json
```
Ожидается: PASS, 3 теста.

- [ ] **Step 6: Прогнать весь EditMode, чтобы не сломать соседей**

```bash
unity command run_tests --mode EditMode --async_tests true --format json
```
Ожидается: количество провалов не выросло относительно состояния до задачи. Известный ранее падающий тест — `Mikey.Fight.Tests.FightSceneTests.Fighters_WearTheirOwnModelAndAvatar` (оба бойца используют одну модель); он к этой работе отношения не имеет.

- [ ] **Step 7: Коммит**

```bash
git add Assets/UI/Profile/ProfileUserData.cs Assets/UI/Profile/ProfileUserDataStorage.cs \
        Assets/UI/Profile/Tests/
git commit -m "feat(profile): штамп времени правки профиля

Правило «профиль побеждает по свежести» требует штампа. Ставится в
единственном месте записи, чтобы его нельзя было забыть из нового кода."
```

---

### Task 7: `SyncPayload` — сборка запроса и применение ответа

**Files:**
- Create: `Assets/Backend/SyncPayload.cs`
- Create: `Assets/Backend/Tests/SyncPayloadTests.cs`
- Create: `Assets/Backend/Tests/Mikey.Backend.Tests.asmdef`

**Interfaces:**
- Consumes: `SyncState` (Task 5), `ProfileUserData.UpdatedAtIso` (Task 6),
  `Mikey.Pose.Level0Results`, `Mikey.Pose.Level1Progress`, `Mikey.UI.Progression.TutorialProgressState`.
- Produces:
  - `public static SyncState Build(ProfileUserData profile, TutorialProgressState progress, Level0Results level0, Level1Progress level1)`
  - `public static void Apply(SyncState merged, ProfileUserData profile, Level0Results level0, Level1Progress level1, ref TutorialProgressState progress)`
  - `public static bool IsNewer(string candidateIso, string currentIso)`
  Task 12 (`SyncService`) вызывает ровно эти три.

Это сердце всей работы и единственное место, где ошибка молча съедает данные пользователя. Поэтому `Apply` **сам** берёт максимум, а не доверяет серверу: сервер уже вернул слитое значение, но повторная защита на клиенте стоит трёх строк и закрывает целый класс аварий — усечённый ответ, ответ не от того пользователя, откат серверной миграции.

- [ ] **Step 1: Написать падающие тесты**

Создать `Assets/Backend/Tests/Mikey.Backend.Tests.asmdef`:

```json
{
    "name": "Mikey.Backend.Tests",
    "rootNamespace": "Mikey.Backend.Tests",
    "references": [
        "Mikey.Backend",
        "Mikey.Pose",
        "Mikey.UI.Profile",
        "Mikey.UI.Progression",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Создать `Assets/Backend/Tests/SyncPayloadTests.cs`:

```csharp
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
```

- [ ] **Step 2: Убедиться, что тесты падают**

```bash
unity command run_tests --mode EditMode --filter "SyncPayloadTests" --filter_type testName --format json
```
Ожидается: FAIL — тип `SyncPayload` не найден.

- [ ] **Step 3: Реализовать `SyncPayload`**

Создать `Assets/Backend/SyncPayload.cs`:

```csharp
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

            if (profile != null && IsNewer(merged.profile.profile_updated_at, profile.UpdatedAtIso))
            {
                profile.DisplayName = merged.profile.display_name ?? profile.DisplayName;
                profile.Gender = merged.profile.gender ?? profile.Gender;
                profile.Age = merged.profile.age;
                profile.WeightKg = merged.profile.weight_kg;
                profile.HeightCm = merged.profile.height_cm;
                profile.UpdatedAtIso = merged.profile.profile_updated_at;
            }
        }

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
```

- [ ] **Step 4: Прогнать тесты**

```bash
unity command run_tests --mode EditMode --filter "SyncPayloadTests" --filter_type testName --format json
```
Ожидается: PASS, 6 тестов.

- [ ] **Step 5: Коммит**

```bash
git add Assets/Backend/SyncPayload.cs Assets/Backend/SyncPayload.cs.meta Assets/Backend/Tests/
git commit -m "feat(backend): SyncPayload — сборка запроса и безопасное применение ответа

Apply берёт максимум сам, а не доверяет серверу: усечённый ответ или
откат серверной миграции не должны стирать прогресс живого человека."
```

---

### Task 8: `SupabaseConfig`

**Files:**
- Create: `Assets/Backend/SupabaseConfig.cs`
- Create: `Assets/Resources/SupabaseConfig.asset` (через меню Unity)

**Interfaces:**
- Produces: `SupabaseConfig` со свойствами `Url` (string), `PublishableKey` (string),
  `GoogleWebClientId` (string) и статическим `SupabaseConfig.Load()`. Task 11 и Task 12 используют их.

Замечание: значения публичные и в APK попадают намеренно — данные защищает RLS и `auth.uid()`, а не секретность ключа. Ассет, а не константы в коде, чтобы dev и prod различались ассетом, а не правкой исходника.

- [ ] **Step 1: Написать класс**

Создать `Assets/Backend/SupabaseConfig.cs`:

```csharp
using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Реквизиты проекта Supabase. Все три значения публичные и попадают в APK
    /// намеренно: publishable-ключ и client id спроектированы как открытые, а
    /// данные защищает RLS и auth.uid(), а не их секретность. Секретный ключ
    /// (<c>sb_secret_…</c>) и client secret сюда класть нельзя ни при каких
    /// обстоятельствах — они server-only.
    ///
    /// Ассет, а не константы в коде, чтобы разделить dev и prod подменой ассета,
    /// а не правкой исходника.
    /// </summary>
    [CreateAssetMenu(fileName = "SupabaseConfig", menuName = "Mikey/Supabase Config")]
    public sealed class SupabaseConfig : ScriptableObject
    {
        /// <summary>Имя ассета в Resources, откуда его берёт <see cref="Load"/>.</summary>
        public const string ResourceName = "SupabaseConfig";

        [SerializeField] private string _url = "https://maaqlmzispqeldsjsank.supabase.co";
        [SerializeField] private string _publishableKey = "sb_publishable_2EHwQuPnOYLwa7_BTNuK8A_VKVDwpMk";
        [SerializeField] private string _googleWebClientId =
            "404020688148-53omm395re2ueeciqccpfqr400e3bds0.apps.googleusercontent.com";

        public string Url => _url;
        public string PublishableKey => _publishableKey;

        /// <summary>
        /// Web-клиент, а не Android: Credential Manager требует именно его как
        /// audience выдаваемого ID-токена, и его же ждёт Supabase.
        /// </summary>
        public string GoogleWebClientId => _googleWebClientId;

        /// <summary>Ассет из Resources, или null — тогда синхронизация просто не включается.</summary>
        public static SupabaseConfig Load() => Resources.Load<SupabaseConfig>(ResourceName);
    }
}
```

- [ ] **Step 2: Создать ассет**

Выполнить:

```bash
unity --json cmd eval 'System.IO.Directory.CreateDirectory("Assets/Resources");
var cfg = UnityEngine.ScriptableObject.CreateInstance<Mikey.Backend.SupabaseConfig>();
UnityEditor.AssetDatabase.CreateAsset(cfg, "Assets/Resources/SupabaseConfig.asset");
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
return "created";'
```
Ожидается: `"result": "created"`.

- [ ] **Step 3: Проверить, что ассет читается и заполнен**

```bash
unity --json cmd eval 'var c = Mikey.Backend.SupabaseConfig.Load();
return c == null ? "NULL" : c.Url + " | " + (c.PublishableKey.StartsWith("sb_publishable_") ? "key ok" : "KEY BAD");'
```
Ожидается: `https://maaqlmzispqeldsjsank.supabase.co | key ok`.

- [ ] **Step 4: Коммит**

```bash
git add Assets/Backend/SupabaseConfig.cs Assets/Backend/SupabaseConfig.cs.meta \
        Assets/Resources/SupabaseConfig.asset Assets/Resources/SupabaseConfig.asset.meta \
        Assets/Resources.meta
git commit -m "feat(backend): SupabaseConfig — публичные реквизиты проекта в ассете"
```

---

# Фаза C — сессия

### Task 9: Хранилище токена и `SupabaseSession`

**Files:**
- Create: `Assets/Backend/ITokenStore.cs`
- Create: `Assets/Backend/MemoryTokenStore.cs`
- Create: `Assets/Backend/SupabaseSession.cs`
- Create: `Assets/Backend/Tests/SupabaseSessionTests.cs`

**Interfaces:**
- Produces:
  - `interface ITokenStore { bool TryLoadRefreshToken(out string token); void SaveRefreshToken(string token); void Clear(); }`
  - `sealed class MemoryTokenStore : ITokenStore`
  - `sealed class SupabaseSession` с `IsSignedIn`, `AccessToken`, `NeedsRefresh(double nowUnix)`,
    `Adopt(string accessToken, string refreshToken, long expiresInSeconds, double nowUnix)`,
    `SignOut()`, `RefreshToken`.
  Task 11 подставляет `AndroidTokenStore`, Task 12 вызывает `NeedsRefresh` и `Adopt`.

Замечание про время: `SupabaseSession` не читает часы сам, а принимает `nowUnix` параметром. Иначе арифметику истечения нельзя проверить тестом, не подменяя системное время. Обновляемся заранее, за 60 секунд до истечения: запрос, отправленный ровно в момент истечения, гарантированно получит 401.

- [ ] **Step 1: Написать падающие тесты**

Создать `Assets/Backend/Tests/SupabaseSessionTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.Backend.Tests
{
    /// <summary>
    /// Арифметика жизни сессии. Часы подаются параметром, поэтому проверяется
    /// без подмены системного времени.
    /// </summary>
    public class SupabaseSessionTests
    {
        private const double Now = 1_800_000_000d;

        [Test]
        public void FreshSession_IsSignedIn_AndDoesNotNeedRefreshYet()
        {
            var session = new SupabaseSession(new MemoryTokenStore());
            session.Adopt("access", "refresh", expiresInSeconds: 3600, nowUnix: Now);

            Assert.IsTrue(session.IsSignedIn);
            Assert.AreEqual("access", session.AccessToken);
            Assert.IsFalse(session.NeedsRefresh(Now));
            Assert.IsFalse(session.NeedsRefresh(Now + 3000));
        }

        [Test]
        public void Refresh_IsRequestedBeforeExpiry_NotAtIt()
        {
            var session = new SupabaseSession(new MemoryTokenStore());
            session.Adopt("access", "refresh", expiresInSeconds: 3600, nowUnix: Now);

            Assert.IsFalse(session.NeedsRefresh(Now + 3600 - SupabaseSession.RefreshSkewSeconds - 1),
                "Обновлялись слишком рано.");
            Assert.IsTrue(session.NeedsRefresh(Now + 3600 - SupabaseSession.RefreshSkewSeconds),
                "Запрос ровно в момент истечения гарантированно получит 401 — надо обновляться заранее.");
            Assert.IsTrue(session.NeedsRefresh(Now + 4000));
        }

        [Test]
        public void RefreshToken_SurvivesRestart_ViaTheStore()
        {
            var store = new MemoryTokenStore();
            var first = new SupabaseSession(store);
            first.Adopt("access", "refresh-abc", 3600, Now);

            var restarted = new SupabaseSession(store);

            Assert.AreEqual("refresh-abc", restarted.RefreshToken);
            Assert.IsFalse(restarted.IsSignedIn,
                "Access-токен между запусками не переживает — сначала обновление.");
        }

        [Test]
        public void SignOut_ClearsBothTokensAndTheStore()
        {
            var store = new MemoryTokenStore();
            var session = new SupabaseSession(store);
            session.Adopt("access", "refresh", 3600, Now);

            session.SignOut();

            Assert.IsFalse(session.IsSignedIn);
            Assert.IsNull(session.AccessToken);
            Assert.IsNull(session.RefreshToken);
            Assert.IsFalse(store.TryLoadRefreshToken(out _));
        }

        [Test]
        public void Adopt_WithAnEmptyAccessToken_AdoptsNothingAtAll()
        {
            var store = new MemoryTokenStore();
            var session = new SupabaseSession(store);

            // Проверять только IsSignedIn бессмысленно: это следует уже из
            // определения свойства через IsNullOrEmpty, и защитную проверку в
            // Adopt можно было бы удалить, не уронив тест. Смысл проверки в том,
            // что из испорченного ответа нельзя взять НИЧЕГО — иначе он подменит
            // рабочий ключ от аккаунта, и человек окажется разлогинен без причины.
            session.Adopt(string.Empty, "refresh-from-broken-response", 3600, Now);

            Assert.IsFalse(session.IsSignedIn);
            Assert.IsNull(session.RefreshToken,
                "Токен обновления взят из ответа, в котором не было токена доступа.");
            Assert.IsFalse(store.TryLoadRefreshToken(out _),
                "Испорченный ответ записан в хранилище токенов.");
        }
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

```bash
unity command run_tests --mode EditMode --filter "SupabaseSessionTests" --filter_type testName --format json
```
Ожидается: FAIL — типы `SupabaseSession` и `MemoryTokenStore` не найдены.

- [ ] **Step 3: Написать интерфейс и хранилище в памяти**

Создать `Assets/Backend/ITokenStore.cs`:

```csharp
namespace Mikey.Backend
{
    /// <summary>
    /// Хранилище refresh-токена. Две реализации существуют не «на будущее»: в
    /// редакторе EncryptedSharedPreferences физически недоступны, а тестам нужна
    /// подмена. Тот же приём, что у <c>ITutorialProgressStorage</c> и
    /// <c>IAudioSettingsStorage</c> в этом проекте.
    /// </summary>
    public interface ITokenStore
    {
        bool TryLoadRefreshToken(out string token);
        void SaveRefreshToken(string token);
        void Clear();
    }
}
```

Создать `Assets/Backend/MemoryTokenStore.cs`:

```csharp
namespace Mikey.Backend
{
    /// <summary>
    /// Хранилище на время процесса — для редактора и тестов. Намеренно ничего не
    /// записывает на диск: refresh-токен это долгоживущий ключ от аккаунта, и
    /// класть его в PlayerPrefs нельзя (обычный XML, попадающий в автобэкап
    /// Android). На устройстве работает <c>AndroidTokenStore</c>.
    /// </summary>
    public sealed class MemoryTokenStore : ITokenStore
    {
        private string _token;

        public bool TryLoadRefreshToken(out string token)
        {
            token = _token;
            return !string.IsNullOrEmpty(_token);
        }

        public void SaveRefreshToken(string token) => _token = token;

        public void Clear() => _token = null;
    }
}
```

- [ ] **Step 4: Написать `SupabaseSession`**

Создать `Assets/Backend/SupabaseSession.cs`:

```csharp
using System;

namespace Mikey.Backend
{
    /// <summary>
    /// Состояние сессии: короткоживущий access-токен в памяти, долгоживущий
    /// refresh-токен — в <see cref="ITokenStore"/>.
    ///
    /// Часы подаются параметром, а не читаются внутри: иначе арифметику
    /// истечения нельзя проверить тестом, не подменяя системное время.
    /// </summary>
    public sealed class SupabaseSession
    {
        /// <summary>
        /// За сколько секунд до истечения считать токен требующим обновления.
        /// Запрос, отправленный ровно в момент истечения, гарантированно
        /// получит 401 — пока он летит, токен уже мёртв.
        /// </summary>
        public const double RefreshSkewSeconds = 60d;

        private readonly ITokenStore _store;
        private double _expiresAtUnix;

        public SupabaseSession(ITokenStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (_store.TryLoadRefreshToken(out string saved))
                RefreshToken = saved;
        }

        /// <summary>Текущий access-токен, или null.</summary>
        public string AccessToken { get; private set; }

        /// <summary>Refresh-токен, переживающий перезапуск. Null, если входа не было.</summary>
        public string RefreshToken { get; private set; }

        /// <summary>Есть ли пригодный access-токен прямо сейчас.</summary>
        public bool IsSignedIn => !string.IsNullOrEmpty(AccessToken);

        /// <summary>Пора ли обновляться к моменту <paramref name="nowUnix"/>.</summary>
        public bool NeedsRefresh(double nowUnix) =>
            !IsSignedIn || nowUnix >= _expiresAtUnix - RefreshSkewSeconds;

        /// <summary>Принимает свежую пару токенов. Пустой access оставляет сессию разлогиненной.</summary>
        public void Adopt(string accessToken, string refreshToken, long expiresInSeconds, double nowUnix)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                AccessToken = null;
                _expiresAtUnix = 0d;
                return;
            }

            AccessToken = accessToken;
            _expiresAtUnix = nowUnix + expiresInSeconds;

            if (string.IsNullOrEmpty(refreshToken))
                return;

            RefreshToken = refreshToken;
            _store.SaveRefreshToken(refreshToken);
        }

        /// <summary>Забывает сессию целиком. Локальных данных игрока не касается.</summary>
        public void SignOut()
        {
            AccessToken = null;
            RefreshToken = null;
            _expiresAtUnix = 0d;
            _store.Clear();
        }

        /// <summary>Текущее время в секундах Unix — единственная точка чтения часов.</summary>
        public static double NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
```

- [ ] **Step 5: Прогнать тесты**

```bash
unity command run_tests --mode EditMode --filter "SupabaseSessionTests" --filter_type testName --format json
```
Ожидается: PASS, 5 тестов.

- [ ] **Step 6: Коммит**

```bash
git add Assets/Backend/ITokenStore.cs Assets/Backend/MemoryTokenStore.cs \
        Assets/Backend/SupabaseSession.cs Assets/Backend/Tests/SupabaseSessionTests.cs \
        Assets/Backend/*.meta Assets/Backend/Tests/*.meta
git commit -m "feat(backend): сессия Supabase и хранилище refresh-токена

Часы подаются параметром, чтобы истечение проверялось тестом.
Обновляемся за 60 секунд до конца: запрос, отправленный в момент
истечения, гарантированно получит 401."
```

---

# Фаза D — Android

### Task 10: Плагин `MikeyAuth.androidlib`

**Files:**
- Create: `Assets/Plugins/Android/MikeyAuth.androidlib/AndroidManifest.xml`
- Create: `Assets/Plugins/Android/MikeyAuth.androidlib/build.gradle`
- Create: `Assets/Plugins/Android/MikeyAuth.androidlib/src/main/java/com/mikey/auth/GoogleAuth.java`
- Create: `Assets/Plugins/Android/MikeyAuth.androidlib/src/main/java/com/mikey/auth/SecureStore.java`

**Interfaces:**
- Produces два Java-класса, вызываемых из C# через `AndroidJavaObject`/`AndroidJavaClass`:
  - `com.mikey.auth.GoogleAuth(Activity activity)` с методами
    `void requestIdToken(String webClientId)`, `String consumeIdToken()`, `String consumeError()`
  - `com.mikey.auth.SecureStore(Context context)` с методами
    `void put(String key, String value)`, `String get(String key)`, `void remove(String key)`
  Task 11 обращается ровно к этим именам и сигнатурам.

Замечание про форму API: `requestIdToken` асинхронный и не возвращает значение, потому что Credential Manager показывает системный диалог. Результат забирается опросом через `consumeIdToken`, как уже сделано в `MikeyPose.androidlib` (`readLatest`). Это сознательно повторяет существующий в проекте приём, а не вводит второй.

- [ ] **Step 1: Манифест и gradle**

Создать `Assets/Plugins/Android/MikeyAuth.androidlib/AndroidManifest.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="com.mikey.auth">
    <uses-permission android:name="android.permission.INTERNET" />
</manifest>
```

Создать `Assets/Plugins/Android/MikeyAuth.androidlib/build.gradle`:

```gradle
apply plugin: 'com.android.library'

android {
    namespace "com.mikey.auth"
    compileSdkVersion 34
    defaultConfig {
        minSdkVersion 25
        targetSdkVersion 34
    }

    // Unity держит AndroidManifest.xml в корне модуля .androidlib, а сборщик по
    // умолчанию ищет его в src/main/. Без этого переопределения манифест плагина
    // не участвует в сборке вовсе, и разрешение INTERNET не попадает в APK —
    // проявится не «классом не найден», а молчаливым отказом сети при входе.
    // Точно такой же блок с тем же обоснованием стоит в MikeyPose.androidlib.
    sourceSets {
        main {
            manifest.srcFile 'AndroidManifest.xml'
        }
    }

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_1_8
        targetCompatibility JavaVersion.VERSION_1_8
    }
}

dependencies {
    implementation 'androidx.credentials:credentials:1.3.0'
    implementation 'androidx.credentials:credentials-play-services-auth:1.3.0'
    implementation 'com.google.android.libraries.identity.googleid:googleid:1.1.1'
    implementation 'androidx.security:security-crypto:1.1.0-alpha06'
}
```

- [ ] **Step 2: Вход через Credential Manager**

Создать `Assets/Plugins/Android/MikeyAuth.androidlib/src/main/java/com/mikey/auth/GoogleAuth.java`:

```java
package com.mikey.auth;

import android.app.Activity;
import android.os.CancellationSignal;
import android.util.Log;

import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.security.SecureRandom;
import java.util.concurrent.Executors;

/**
 * Вход через Google: Credential Manager показывает системный диалог и отдаёт
 * ID-токен, который Unity меняет в Supabase на сессию.
 *
 * Результат забирается опросом (consumeIdToken), а не колбэком в Unity: диалог
 * асинхронный, а этот приём в проекте уже используется — MikeyPose.androidlib
 * отдаёт кадры через readLatest. Второй механизм ради одного вызова заводить
 * незачем.
 */
public final class GoogleAuth {

    private static final String TAG = "MikeyAuth";

    private final Activity activity;
    private final CredentialManager credentialManager;

    private volatile String idToken;
    private volatile String error;

    public GoogleAuth(Activity activity) {
        this.activity = activity;
        this.credentialManager = CredentialManager.create(activity);
    }

    /** Запускает диалог входа. Результат забирать через consumeIdToken/consumeError. */
    public void requestIdToken(String webClientId) {
        idToken = null;
        error = null;

        // nonce привязывает выданный токен к этой попытке входа: перехваченный
        // чужой токен с другим nonce Supabase не примет.
        byte[] raw = new byte[32];
        new SecureRandom().nextBytes(raw);
        StringBuilder nonce = new StringBuilder(raw.length * 2);
        for (byte b : raw) nonce.append(String.format("%02x", b));

        GetGoogleIdOption option = new GetGoogleIdOption.Builder()
                .setServerClientId(webClientId)
                .setFilterByAuthorizedAccounts(false)
                .setNonce(nonce.toString())
                .build();

        GetCredentialRequest request = new GetCredentialRequest.Builder()
                .addCredentialOption(option)
                .build();

        credentialManager.getCredentialAsync(
                activity,
                request,
                new CancellationSignal(),
                Executors.newSingleThreadExecutor(),
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override
                    public void onResult(GetCredentialResponse response) {
                        try {
                            GoogleIdTokenCredential credential =
                                    GoogleIdTokenCredential.createFrom(
                                            response.getCredential().getData());
                            idToken = credential.getIdToken();
                        } catch (Exception e) {
                            Log.e(TAG, "не удалось разобрать учётные данные", e);
                            error = String.valueOf(e.getMessage());
                        }
                    }

                    @Override
                    public void onError(GetCredentialException e) {
                        Log.e(TAG, "вход отменён или не удался", e);
                        error = String.valueOf(e.getMessage());
                    }
                });
    }

    /** ID-токен, либо null, если вход ещё идёт. Отдаётся один раз. */
    public String consumeIdToken() {
        String value = idToken;
        idToken = null;
        return value;
    }

    /** Текст ошибки, либо null. Отдаётся один раз. */
    public String consumeError() {
        String value = error;
        error = null;
        return value;
    }
}
```

- [ ] **Step 3: Шифрованное хранилище**

Создать `Assets/Plugins/Android/MikeyAuth.androidlib/src/main/java/com/mikey/auth/SecureStore.java`:

```java
package com.mikey.auth;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import androidx.security.crypto.EncryptedSharedPreferences;
import androidx.security.crypto.MasterKey;

/**
 * Шифрованное хранилище для refresh-токена. Не PlayerPrefs: там это обычный
 * XML, который вдобавок попадает в автоматический бэкап Android, то есть
 * долгоживущий ключ от аккаунта уезжал бы в облако открытым текстом.
 */
public final class SecureStore {

    private static final String TAG = "MikeyAuth";
    private static final String FILE = "mikey_auth";

    private final SharedPreferences prefs;

    public SecureStore(Context context) throws Exception {
        MasterKey key = new MasterKey.Builder(context)
                .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
                .build();

        prefs = EncryptedSharedPreferences.create(
                context,
                FILE,
                key,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM);
    }

    public void put(String key, String value) {
        prefs.edit().putString(key, value).apply();
    }

    /** Значение или null. */
    public String get(String key) {
        try {
            return prefs.getString(key, null);
        } catch (Exception e) {
            // Испорченное хранилище лечится повторным входом, а не падением.
            Log.w(TAG, "не удалось прочитать значение, считаем отсутствующим", e);
            return null;
        }
    }

    public void remove(String key) {
        prefs.edit().remove(key).apply();
    }
}
```

- [ ] **Step 4: Проверить, что проект по-прежнему собирается**

```bash
unity --json cmd eval 'return "compiled";'
```
Ожидается: `"result": "compiled"`. Java-код на этом шаге ещё не компилируется — он собирается только при сборке APK, это произойдёт в Task 14.

- [ ] **Step 5: Коммит**

```bash
git add Assets/Plugins/Android/MikeyAuth.androidlib/
git commit -m "feat(auth): Android-плагин входа через Google и шифрованного хранилища

Refresh-токен в EncryptedSharedPreferences, а не в PlayerPrefs: последний
это обычный XML, попадающий в автобэкап Android. Результат входа
забирается опросом — тем же приёмом, что кадры в MikeyPose.androidlib."
```

---

### Task 11: Мосты `IAuthGateway`, `AndroidGoogleAuth`, `AndroidTokenStore`

**Files:**
- Create: `Assets/Backend/IAuthGateway.cs`
- Create: `Assets/Backend/AndroidGoogleAuth.cs`
- Create: `Assets/Backend/AndroidTokenStore.cs`

**Interfaces:**
- Consumes: Java-классы из Task 10, `SupabaseConfig` (Task 8), `ITokenStore` (Task 9).
- Produces:
  - `interface IAuthGateway { bool IsAvailable { get; } void BeginSignIn(string webClientId); bool TryTakeIdToken(out string idToken); bool TryTakeError(out string message); }`
  - `sealed class AndroidGoogleAuth : IAuthGateway` — на устройстве работает, в редакторе `IsAvailable == false`
  - `sealed class AndroidTokenStore : ITokenStore`
  Task 12 (`SyncService`) использует эти имена.

- [ ] **Step 1: Интерфейс**

Создать `Assets/Backend/IAuthGateway.cs`:

```csharp
namespace Mikey.Backend
{
    /// <summary>
    /// Источник Google ID-токена. Диалог входа асинхронный и системный, поэтому
    /// начало и получение результата разделены, а результат забирается опросом.
    /// </summary>
    public interface IAuthGateway
    {
        /// <summary>Доступен ли вход на этой платформе. В редакторе — нет.</summary>
        bool IsAvailable { get; }

        /// <summary>Показывает системный диалог выбора аккаунта.</summary>
        void BeginSignIn(string webClientId);

        /// <summary>Забирает полученный ID-токен ровно один раз.</summary>
        bool TryTakeIdToken(out string idToken);

        /// <summary>Забирает текст ошибки ровно один раз.</summary>
        bool TryTakeError(out string message);
    }
}
```

- [ ] **Step 2: Реализация входа**

Создать `Assets/Backend/AndroidGoogleAuth.cs`:

```csharp
using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Мост к <c>com.mikey.auth.GoogleAuth</c>. Вне Android-устройства
    /// <see cref="IsAvailable"/> ложно, и приложение просто не показывает вход —
    /// в редакторе Credential Manager не существует.
    /// </summary>
    public sealed class AndroidGoogleAuth : IAuthGateway
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _auth;

        public bool IsAvailable => true;

        public void BeginSignIn(string webClientId)
        {
            try
            {
                if (_auth == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        _auth = new AndroidJavaObject("com.mikey.auth.GoogleAuth", activity);
                }

                _auth.Call("requestIdToken", webClientId);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AndroidGoogleAuth] не удалось начать вход: {e}");
            }
        }

        public bool TryTakeIdToken(out string idToken)
        {
            idToken = _auth?.Call<string>("consumeIdToken");
            return !string.IsNullOrEmpty(idToken);
        }

        public bool TryTakeError(out string message)
        {
            message = _auth?.Call<string>("consumeError");
            return !string.IsNullOrEmpty(message);
        }
#else
        public bool IsAvailable => false;

        public void BeginSignIn(string webClientId) { }

        public bool TryTakeIdToken(out string idToken)
        {
            idToken = null;
            return false;
        }

        public bool TryTakeError(out string message)
        {
            message = null;
            return false;
        }
#endif
    }
}
```

- [ ] **Step 3: Реализация хранилища**

Создать `Assets/Backend/AndroidTokenStore.cs`:

```csharp
using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Мост к <c>com.mikey.auth.SecureStore</c>. Если шифрованное хранилище не
    /// поднялось, ведём себя как «токена нет»: это означает повторный вход, а не
    /// падение приложения.
    /// </summary>
    public sealed class AndroidTokenStore : ITokenStore
    {
        private const string Key = "supabase.refresh";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _store;

        private AndroidJavaObject Store()
        {
            if (_store != null)
                return _store;

            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _store = new AndroidJavaObject("com.mikey.auth.SecureStore", activity);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AndroidTokenStore] шифрованное хранилище недоступно: {e}");
            }

            return _store;
        }

        public bool TryLoadRefreshToken(out string token)
        {
            token = Store()?.Call<string>("get", Key);
            return !string.IsNullOrEmpty(token);
        }

        public void SaveRefreshToken(string token) => Store()?.Call("put", Key, token);

        public void Clear() => Store()?.Call("remove", Key);
#else
        public bool TryLoadRefreshToken(out string token)
        {
            token = null;
            return false;
        }

        public void SaveRefreshToken(string token) { }

        public void Clear() { }
#endif
    }
}
```

- [ ] **Step 4: Проверить компиляцию**

```bash
unity --json cmd eval 'return "compiled";'
```
Ожидается: `"result": "compiled"`.

- [ ] **Step 5: Коммит**

```bash
git add Assets/Backend/IAuthGateway.cs Assets/Backend/AndroidGoogleAuth.cs \
        Assets/Backend/AndroidTokenStore.cs Assets/Backend/*.meta
git commit -m "feat(backend): мосты к Android-плагину входа и шифрованного хранилища"
```

---

# Фаза E — связывание

### Task 12: HTTP-клиент и `SyncService`

**Files:**
- Create: `Assets/Backend/SupabaseClient.cs`
- Create: `Assets/Backend/SyncService.cs`

**Interfaces:**
- Consumes: `SupabaseConfig`, `SyncState`, `SyncPayload`, `SupabaseSession`, `ITokenStore`, `IAuthGateway`.
- Produces: `SyncService` — MonoBehaviour со свойством `bool IsSignedIn`, событием `event Action Changed`,
  методами `void SignIn()`, `void SignOut()`, `void DeleteAccount()`, `void RequestSync()`.
  Task 13 (UI) вызывает ровно их.

Замечание про политику ошибок, из спеки: пользователю ничего не показываем, локальные данные не трогаем, отклонённый запрос не повторяем по кругу.

- [ ] **Step 1: HTTP-клиент**

Создать `Assets/Backend/SupabaseClient.cs`:

```csharp
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Mikey.Backend
{
    /// <summary>
    /// Три запроса, которые нужны приложению: обмен Google ID-токена на сессию,
    /// обновление сессии и вызов функции слияния. Ничего не решает — решения
    /// принимает <see cref="SyncService"/>.
    /// </summary>
    public sealed class SupabaseClient
    {
        /// <summary>Ответ GoTrue на выдачу или обновление сессии.</summary>
        [Serializable]
        public sealed class TokenResponse
        {
            public string access_token;
            public string refresh_token;
            public long expires_in;
        }

        /// <summary>Итог запроса. Различает «сеть/сервер» и «отказ» — политика повтора у них разная.</summary>
        public enum Outcome
        {
            Ok,
            Retryable,     // нет сети, таймаут, 5xx — повторим при следующем триггере
            Unauthorized,  // 401 — нужен refresh или разлогин
            Rejected,      // 4xx — повторять бессмысленно
        }

        private const int TimeoutSeconds = 20;

        private readonly SupabaseConfig _config;

        public SupabaseClient(SupabaseConfig config) =>
            _config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>Меняет Google ID-токен на сессию Supabase.</summary>
        public IEnumerator ExchangeGoogleIdToken(string idToken, Action<Outcome, TokenResponse> done)
        {
            string url = $"{_config.Url}/auth/v1/token?grant_type=id_token";
            string body = "{\"provider\":\"google\",\"id_token\":\"" + Escape(idToken) + "\"}";
            return PostJson(url, body, accessToken: null, done);
        }

        /// <summary>Обновляет сессию по refresh-токену.</summary>
        public IEnumerator Refresh(string refreshToken, Action<Outcome, TokenResponse> done)
        {
            string url = $"{_config.Url}/auth/v1/token?grant_type=refresh_token";
            string body = "{\"refresh_token\":\"" + Escape(refreshToken) + "\"}";
            return PostJson(url, body, accessToken: null, done);
        }

        /// <summary>Вызывает sync_progress и отдаёт слитое состояние.</summary>
        public IEnumerator SyncProgress(string accessToken, SyncState state, Action<Outcome, SyncState> done)
        {
            string url = $"{_config.Url}/rest/v1/rpc/sync_progress";
            string body = "{\"payload\":" + JsonUtility.ToJson(state) + "}";

            yield return Send(url, body, accessToken, (outcome, text) =>
            {
                SyncState merged = null;
                if (outcome == Outcome.Ok && !string.IsNullOrEmpty(text))
                {
                    try
                    {
                        merged = JsonUtility.FromJson<SyncState>(text);
                    }
                    catch (Exception e)
                    {
                        // Неразбираемый ответ — не повод трогать локальные данные.
                        Debug.LogWarning($"[SupabaseClient] ответ не разобран: {e.Message}");
                        outcome = Outcome.Rejected;
                    }
                }
                done?.Invoke(outcome, merged);
            });
        }

        /// <summary>Удаляет аккаунт на сервере.</summary>
        public IEnumerator DeleteAccount(string accessToken, Action<Outcome> done)
        {
            string url = $"{_config.Url}/rest/v1/rpc/delete_account";
            yield return Send(url, "{}", accessToken, (outcome, _) => done?.Invoke(outcome));
        }

        private IEnumerator PostJson(string url, string body, string accessToken,
                                     Action<Outcome, TokenResponse> done)
        {
            yield return Send(url, body, accessToken, (outcome, text) =>
            {
                TokenResponse parsed = null;
                if (outcome == Outcome.Ok && !string.IsNullOrEmpty(text))
                {
                    try
                    {
                        parsed = JsonUtility.FromJson<TokenResponse>(text);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SupabaseClient] ответ токена не разобран: {e.Message}");
                        outcome = Outcome.Rejected;
                    }
                }
                done?.Invoke(outcome, parsed);
            });
        }

        private IEnumerator Send(string url, string body, string accessToken,
                                 Action<Outcome, string> done)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", _config.PublishableKey);
                if (!string.IsNullOrEmpty(accessToken))
                    request.SetRequestHeader("Authorization", "Bearer " + accessToken);

                yield return request.SendWebRequest();

                Outcome outcome;
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    outcome = Outcome.Retryable;
                }
                else if (request.responseCode == 401 || request.responseCode == 403)
                {
                    outcome = Outcome.Unauthorized;
                }
                else if (request.responseCode >= 500)
                {
                    outcome = Outcome.Retryable;
                }
                else if (request.responseCode >= 400)
                {
                    Debug.LogWarning($"[SupabaseClient] {request.responseCode} на {url}: " +
                                     $"{request.downloadHandler.text}");
                    outcome = Outcome.Rejected;
                }
                else
                {
                    outcome = Outcome.Ok;
                }

                done?.Invoke(outcome, request.downloadHandler.text);
            }
        }

        private static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
```

- [ ] **Step 2: Служба синхронизации**

Создать `Assets/Backend/SyncService.cs`:

```csharp
using System;
using System.Collections;
using Mikey.Pose;
using Mikey.UI.Profile;
using Mikey.UI.Progression;
using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Решает, когда синхронизироваться, и следит за тем, чтобы ни одна неудача
    /// не стоила игроку данных.
    ///
    /// Триггеров намеренно мало: успешный вход, уход приложения в фон и запись
    /// подхода. Никаких таймеров — резервная копия не стоит того, чтобы будить
    /// радиомодуль во время тренировки.
    ///
    /// Ошибки пользователю не показываются: неудавшийся бэкап не является
    /// игровым событием, и прерывать им тренировку неуместно. Отклонённый
    /// сервером запрос не повторяется — повтор по кругу это разряженная батарея,
    /// а не надёжность.
    /// </summary>
    public sealed class SyncService : MonoBehaviour
    {
        private SupabaseConfig _config;
        private SupabaseClient _client;
        private SupabaseSession _session;
        private IAuthGateway _auth;
        private ITutorialProgress _progress;

        private bool _syncing;
        private bool _dirty;

        /// <summary>Поднимается при смене состояния входа — UI перерисовывает себя.</summary>
        public event Action Changed;

        public bool IsSignedIn => _session != null && !string.IsNullOrEmpty(_session.RefreshToken);

        /// <summary>Доступен ли вход на этой платформе (в редакторе — нет).</summary>
        public bool CanSignIn => _auth != null && _auth.IsAvailable && _config != null;

        private void Awake()
        {
            _config = SupabaseConfig.Load();
            if (_config == null)
            {
                Debug.LogWarning("[SyncService] SupabaseConfig не найден; синхронизация выключена.");
                enabled = false;
                return;
            }

            _client = new SupabaseClient(_config);
            _auth = new AndroidGoogleAuth();
            _session = new SupabaseSession(TokenStore());
            _progress = GetComponent<ITutorialProgress>();
        }

        private static ITokenStore TokenStore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidTokenStore();
#else
            return new MemoryTokenStore();
#endif
        }

        /// <summary>Уход в фон — надёжный момент «пользователь закончил».</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
                RequestSync();
        }

        /// <summary>Начинает вход. Диалог системный, результат забираем опросом.</summary>
        public void SignIn()
        {
            if (!CanSignIn || _syncing)
                return;
            _auth.BeginSignIn(_config.GoogleWebClientId);
            StartCoroutine(AwaitSignIn());
        }

        /// <summary>Забывает сессию. Локальный прогресс не трогает.</summary>
        public void SignOut()
        {
            _session?.SignOut();
            Changed?.Invoke();
        }

        /// <summary>Просит синхронизацию. Безопасно звать часто.</summary>
        public void RequestSync()
        {
            if (!IsSignedIn || !isActiveAndEnabled)
                return;

            if (_syncing)
            {
                _dirty = true;
                return;
            }

            StartCoroutine(SyncRoutine());
        }

        /// <summary>Удаляет аккаунт и всё, что уехало на сервер.</summary>
        public void DeleteAccount()
        {
            if (!IsSignedIn || _syncing)
                return;
            StartCoroutine(DeleteRoutine());
        }

        private IEnumerator AwaitSignIn()
        {
            // Диалог живёт столько, сколько нужно человеку; ограничиваем ожидание,
            // чтобы корутина не висела вечно, если он ушёл из приложения.
            float deadline = Time.unscaledTime + 180f;

            while (Time.unscaledTime < deadline)
            {
                if (_auth.TryTakeError(out string message))
                {
                    Debug.Log($"[SyncService] вход не состоялся: {message}");
                    yield break;
                }

                if (_auth.TryTakeIdToken(out string idToken))
                {
                    yield return _client.ExchangeGoogleIdToken(idToken, (outcome, token) =>
                    {
                        if (outcome == SupabaseClient.Outcome.Ok && token != null)
                        {
                            _session.Adopt(token.access_token, token.refresh_token,
                                           token.expires_in, SupabaseSession.NowUnix());
                            Changed?.Invoke();
                        }
                        else
                        {
                            Debug.LogWarning($"[SyncService] обмен токена не удался: {outcome}");
                        }
                    });

                    if (IsSignedIn)
                        RequestSync();
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator EnsureFreshSession()
        {
            if (!_session.NeedsRefresh(SupabaseSession.NowUnix()))
                yield break;
            if (string.IsNullOrEmpty(_session.RefreshToken))
                yield break;

            yield return _client.Refresh(_session.RefreshToken, (outcome, token) =>
            {
                if (outcome == SupabaseClient.Outcome.Ok && token != null)
                {
                    _session.Adopt(token.access_token, token.refresh_token,
                                   token.expires_in, SupabaseSession.NowUnix());
                }
                else if (outcome == SupabaseClient.Outcome.Unauthorized ||
                         outcome == SupabaseClient.Outcome.Rejected)
                {
                    // Refresh-токен мёртв. Разлогиниваемся, но локальные данные —
                    // это данные игрока, и они остаются нетронутыми.
                    _session.SignOut();
                    Changed?.Invoke();
                }
            });
        }

        private IEnumerator SyncRoutine()
        {
            _syncing = true;
            _dirty = false;

            yield return EnsureFreshSession();

            if (_session.IsSignedIn)
            {
                ProfileUserData profile = ProfileUserDataStorage.Load();
                Level0Results level0 = Level0Results.Load();
                Level1Progress level1 = Level1Progress.Load();
                TutorialProgressState progress = _progress?.State ?? TutorialProgressState.NewPlayer;

                SyncState request = SyncPayload.Build(profile, progress, level0, level1);

                yield return _client.SyncProgress(_session.AccessToken, request, (outcome, merged) =>
                {
                    if (outcome != SupabaseClient.Outcome.Ok || merged == null)
                        return;

                    string stampBefore = profile.UpdatedAtIso;
                    TutorialProgressState applied = progress;
                    SyncPayload.Apply(merged, profile, level0, level1, ref applied);

                    level0.Save();
                    level1.Save();

                    // Профиль трогаем, только если Apply действительно принял
                    // серверную копию — и записываем БЕЗ нового штампа, иначе
                    // принятая чужая правка выглядела бы как своя свежая и это
                    // устройство навсегда стало бы «самым новым».
                    if (!string.Equals(profile.UpdatedAtIso, stampBefore, StringComparison.Ordinal))
                        ProfileUserDataStorage.SaveSynced(profile);

                    if (applied > progress)
                        _progress?.Advance(applied);
                });
            }

            _syncing = false;

            if (_dirty)
                RequestSync();
        }

        private IEnumerator DeleteRoutine()
        {
            _syncing = true;

            yield return EnsureFreshSession();

            if (_session.IsSignedIn)
            {
                yield return _client.DeleteAccount(_session.AccessToken, outcome =>
                {
                    if (outcome == SupabaseClient.Outcome.Ok)
                        _session.SignOut();
                    else
                        Debug.LogWarning($"[SyncService] удаление аккаунта не удалось: {outcome}");
                });
            }

            _syncing = false;
            Changed?.Invoke();
        }
    }
}
```

- [ ] **Step 3: Проверить компиляцию и что прежние тесты целы**

```bash
unity command run_tests --mode EditMode --filter "Mikey.Backend.Tests" --filter_type assembly --format json
```
Ожидается: PASS, 11 тестов (6 из Task 7 и 5 из Task 9).

- [ ] **Step 4: Коммит**

```bash
git add Assets/Backend/SupabaseClient.cs Assets/Backend/SyncService.cs Assets/Backend/*.meta
git commit -m "feat(backend): HTTP-клиент и служба синхронизации

Триггеры: вход, уход в фон, запись подхода. Ошибки не показываются
пользователю и не трогают локальные данные; отклонённый запрос не
повторяется."
```

---

### Task 13: Блок аккаунта на экране профиля

**Files:**
- Modify: `Assets/UI/MikeyApp.uxml` (экран `profileDetails`)
- Create: `Assets/Backend/AccountPanelController.cs`
- Test: `Assets/UI/Profile/Tests/AccountPanelUxmlTests.cs`

Замечание про место файла: контроллер живёт в сборке `Mikey.Backend`, а не в
`Mikey.UI.Profile`, хотя рисует экран профиля. Причина механическая: `Mikey.Backend`
уже ссылается на `Mikey.UI.Profile` (Task 5), и обратная ссылка образовала бы цикл,
который Unity компилировать откажется. Направление зависимости выбрано так, потому что
Backend знает про профиль по существу, а профиль про сеть знать не обязан.

**Interfaces:**
- Consumes: `SyncService` (Task 12).
- Produces: элементы UXML с именами `account-status`, `account-sign-in`, `account-sign-out`,
  `account-delete`, `account-consent`. Тест проверяет их наличие.

Замечание про согласие: возраст, вес и рост — персональные данные, и нижняя граница возраста в приложении 10 лет. Поэтому кнопка входа сопровождается текстом, прямо перечисляющим, что уезжает на сервер. Это не украшение: без него человек отправляет данные о своём теле, не зная об этом.

- [ ] **Step 1: Написать падающий тест**

Создать `Assets/UI/Profile/Tests/AccountPanelUxmlTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Блок аккаунта существует в вёрстке и содержит всё, что требует спека:
    /// состояние, вход, выход, удаление и текст согласия.
    /// </summary>
    public class AccountPanelUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";

        private static string Uxml => File.ReadAllText(UxmlPath);

        [Test]
        public void ProfileDetails_HasEveryAccountControl()
        {
            string uxml = Uxml;
            foreach (string name in new[]
                     {
                         "account-status", "account-sign-in", "account-sign-out",
                         "account-delete", "account-consent",
                     })
            {
                StringAssert.Contains($"name=\"{name}\"", uxml,
                    $"В вёрстке нет элемента '{name}'.");
            }
        }

        [Test]
        public void ConsentText_NamesTheBodyDataThatLeavesTheDevice()
        {
            string uxml = Uxml;
            int start = uxml.IndexOf("name=\"account-consent\"", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, "Текста согласия нет.");

            string tail = uxml.Substring(start, System.Math.Min(600, uxml.Length - start));
            StringAssert.Contains("возраст", tail,
                "Согласие обязано называть данные о теле: человек отправляет их о себе.");
            StringAssert.Contains("вес", tail);
            StringAssert.Contains("рост", tail);
        }
    }
}
```

- [ ] **Step 2: Убедиться, что тест падает**

```bash
unity command run_tests --mode EditMode --filter "AccountPanelUxmlTests" --filter_type testName --format json
```
Ожидается: FAIL — элементов в вёрстке нет.

- [ ] **Step 3: Добавить блок в вёрстку**

В `Assets/UI/MikeyApp.uxml` найти на экране `profileDetails` кнопку
`<ui:Button name="profile-details-save" …>` и **сразу после её закрывающего тега** вставить:

```xml
                    <!-- Блок аккаунта. Вход необязателен: без него приложение
                         работает полностью, просто без резервной копии. Текст
                         согласия перечисляет данные о теле поимённо — человек
                         должен знать, что отправляет их о себе. -->
                    <ui:VisualElement name="account-panel" class="pd-account">
                        <ui:Label name="account-status" text="Прогресс хранится только на этом телефоне"
                                  class="pd-account__status" />
                        <ui:Label name="account-consent"
                                  text="Вход через Google сохранит на сервере имя, пол, возраст, вес, рост и результаты тренировок, чтобы прогресс вернулся после переустановки."
                                  class="pd-account__consent" />
                        <ui:Button name="account-sign-in" class="pd-btn pd-btn--primary tap-target-lg">
                            <ui:Label text="Войти через Google" class="pd-btn__text" picking-mode="Ignore" />
                        </ui:Button>
                        <ui:Button name="account-sign-out" class="pd-btn pd-btn--ghost tap-target-lg">
                            <ui:Label text="Выйти" class="pd-btn__text" picking-mode="Ignore" />
                        </ui:Button>
                        <ui:Button name="account-delete" class="pd-btn pd-btn--danger tap-target-lg">
                            <ui:Label text="Удалить аккаунт" class="pd-btn__text" picking-mode="Ignore" />
                        </ui:Button>
                    </ui:VisualElement>
```

- [ ] **Step 4: Прогнать тест вёрстки**

```bash
unity command run_tests --mode EditMode --filter "AccountPanelUxmlTests" --filter_type testName --format json
```
Ожидается: PASS, 2 теста.

- [ ] **Step 5: Написать контроллер**

Создать `Assets/Backend/AccountPanelController.cs` (в сборке Backend — см. замечание к задаче):

```csharp
using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.Backend
{
    /// <summary>
    /// Блок аккаунта на экране profileDetails. Повторяет приём привязки,
    /// принятый в проекте: ждать rootVisualElement корутиной и подписываться на
    /// вход на экран через <see cref="IScreenNavigator"/>, потому что общий
    /// GameObject "UI" всегда включён и OnEnable здесь ничего не значит.
    ///
    /// Когда вход недоступен (редактор), блок скрывается целиком: показывать
    /// кнопку, которая заведомо ничего не сделает, — хуже, чем не показывать.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AccountPanelController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const string ScreenId = "profileDetails";

        private VisualElement _panel;
        private Label _status;
        private Button _signIn;
        private Button _signOut;
        private Button _delete;

        private SyncService _sync;
        private IScreenNavigator _navigator;
        private Coroutine _bindRoutine;
        private bool _bound;

        private void OnEnable()
        {
            if (_bound)
                return;
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            if (_bound)
            {
                if (_signIn != null) _signIn.clicked -= OnSignIn;
                if (_signOut != null) _signOut.clicked -= OnSignOut;
                if (_delete != null) _delete.clicked -= OnDelete;
                if (_sync != null) _sync.Changed -= Render;
            }

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            _panel = null;
            _status = null;
            _signIn = null;
            _signOut = null;
            _delete = null;
            _bound = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[AccountPanelController] UIDocument root недоступен.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            VisualElement root = document.rootVisualElement;
            _panel = root.Q<VisualElement>("account-panel");
            _status = root.Q<Label>("account-status");
            _signIn = root.Q<Button>("account-sign-in");
            _signOut = root.Q<Button>("account-sign-out");
            _delete = root.Q<Button>("account-delete");

            if (_panel == null)
            {
                Debug.LogError("[AccountPanelController] блок аккаунта не найден в вёрстке.", this);
                _bindRoutine = null;
                yield break;
            }

            _sync = GetComponent<SyncService>();

            if (_signIn != null) _signIn.clicked += OnSignIn;
            if (_signOut != null) _signOut.clicked += OnSignOut;
            if (_delete != null) _delete.clicked += OnDelete;
            if (_sync != null) _sync.Changed += Render;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            _bound = true;
            _bindRoutine = null;
            Render();
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId == ScreenId)
                Render();
        }

        private void OnSignIn() => _sync?.SignIn();

        private void OnSignOut() => _sync?.SignOut();

        private void OnDelete() => _sync?.DeleteAccount();

        private void Render()
        {
            if (_panel == null)
                return;

            bool available = _sync != null && _sync.CanSignIn;
            _panel.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            if (!available)
                return;

            bool signedIn = _sync.IsSignedIn;

            if (_status != null)
            {
                _status.text = signedIn
                    ? "Прогресс сохраняется в аккаунте Google"
                    : "Прогресс хранится только на этом телефоне";
            }

            if (_signIn != null)
                _signIn.style.display = signedIn ? DisplayStyle.None : DisplayStyle.Flex;
            if (_signOut != null)
                _signOut.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            if (_delete != null)
                _delete.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
```

- [ ] **Step 6: Проверить, что цикла сборок не возникло**

`Assets/UI/Profile/Mikey.UI.Profile.asmdef` **не трогать**: ссылки на `Mikey.Backend` там быть
не должно. Зависимость односторонняя — `Mikey.Backend` → `Mikey.UI.Profile`, и только так.
`Mikey.UI.SafeArea` уже в `references` сборки Backend (Task 5), отдельно добавлять не нужно.

Тест `AccountPanelUxmlTests.cs` остаётся в `Assets/UI/Profile/Tests/`: он читает вёрстку как
текстовый файл и ни на какую сборку не опирается.

Проверить:

```bash
unity --json cmd eval 'return "compiled";'
```
Ожидается: `"result": "compiled"`. Ошибка вида «Assembly with name ... has cyclic references»
означает, что в asmdef профиля всё-таки добавили ссылку на Backend — убрать её.

- [ ] **Step 7: Прогнать все тесты Backend и Profile**

```bash
unity command run_tests --mode EditMode --filter "Mikey.Backend.Tests" --filter "Mikey.UI.Profile.Tests" --filter_type assembly --format json
```
Ожидается: PASS, 16 тестов (6 SyncPayload + 5 SupabaseSession + 3 ProfileUserDataStorage + 2 AccountPanelUxml).

- [ ] **Step 8: Коммит**

```bash
git add Assets/UI/MikeyApp.uxml \
        Assets/Backend/AccountPanelController.cs Assets/Backend/AccountPanelController.cs.meta \
        Assets/UI/Profile/Tests/AccountPanelUxmlTests.cs Assets/UI/Profile/Tests/AccountPanelUxmlTests.cs.meta
git commit -m "feat(profile): блок аккаунта — вход, выход, удаление и согласие

Согласие называет данные о теле поимённо: человек должен знать, что
отправляет их о себе. Контроллер живёт в сборке Backend, чтобы не
образовался цикл ссылок между сборками."
```

---

### Task 14: Связывание сцены и проверка на устройстве

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

**Interfaces:**
- Consumes: `SyncService`, `AccountPanelController`.
- Produces: рабочее приложение.

- [ ] **Step 1: Написать падающий тест связывания сцены**

В `Assets/UI/SafeArea/Tests/SceneWiringTests.cs` добавить тест (файл уже существует и содержит
похожие проверки — следовать его стилю):

```csharp
        [Test]
        public void UiGameObject_HasBackendSyncAndAccountPanel()
        {
            string scene = System.IO.File.ReadAllText("Assets/Scenes/SampleScene.unity");
            StringAssert.Contains("Mikey.Backend.SyncService", scene,
                "На GameObject UI нет SyncService — синхронизация не запустится.");
            StringAssert.Contains("Mikey.Backend.AccountPanelController", scene,
                "На GameObject UI нет AccountPanelController — блок аккаунта не оживёт.");
        }
```

- [ ] **Step 2: Убедиться, что тест падает**

```bash
unity command run_tests --mode EditMode --filter "UiGameObject_HasBackendSyncAndAccountPanel" --filter_type testName --format json
```
Ожидается: FAIL.

- [ ] **Step 3: Добавить компоненты на GameObject `UI`**

Выполнить через открытый редактор:

```bash
unity --json cmd eval '
var go = GameObject.Find("UI");
if (go == null) return "UI GameObject не найден";
if (go.GetComponent<Mikey.Backend.SyncService>() == null)
    go.AddComponent<Mikey.Backend.SyncService>();
if (go.GetComponent<Mikey.Backend.AccountPanelController>() == null)
    go.AddComponent<Mikey.Backend.AccountPanelController>();
UnityEditor.EditorSceneManager.MarkSceneDirty(go.scene);
UnityEditor.EditorSceneManager.SaveScene(go.scene);
return "added: " + go.GetComponents<MonoBehaviour>().Length + " components";'
```
Ожидается: строка `added: N components`. Если ответ «UI GameObject не найден» — открыть
`Assets/Scenes/SampleScene.unity` в редакторе и повторить.

- [ ] **Step 4: Прогнать тест связывания**

```bash
unity command run_tests --mode EditMode --filter "UiGameObject_HasBackendSyncAndAccountPanel" --filter_type testName --format json
```
Ожидается: PASS.

- [ ] **Step 5: Прогнать весь EditMode**

```bash
unity command run_tests --mode EditMode --async_tests true --format json
```
Ожидается: провалов не больше, чем было до начала работы. Известный ранее падающий тест —
`Mikey.Fight.Tests.FightSceneTests.Fighters_WearTheirOwnModelAndAvatar`, к этой работе отношения не имеет.

- [ ] **Step 6: Собрать APK**

```bash
unity --json cmd --timeout 3600 eval 'AppAndroidBuild.Build(); return "build started";'
```
Затем дождаться в `C:\Users\user\AppData\Local\Unity\Editor\Editor.log` строки
`[AppAndroidBuild] BUILD OK`. Ожидается успешная сборка; ошибки Gradle на этом шаге почти
наверняка означают конфликт версий androidx из Task 10 — читать текст ошибки в логе.

- [ ] **Step 7: Проверить на устройстве**

Установить APK на телефон, аккаунт которого добавлен в тестовые пользователи Google
(см. хвосты в спеке — без этого Google откажет во входе), и пройти:

1. Открыть Профиль → Изменить данные. Блок аккаунта виден, статус «Прогресс хранится только на этом телефоне».
2. Нажать «Войти через Google», выбрать аккаунт. Статус меняется на «Прогресс сохраняется в аккаунте Google».
3. Пройти пару упражнений уровня 0, свернуть приложение.
4. В SQL Editor выполнить `select * from public.level0_results;` — строка с результатами появилась.
5. Удалить приложение, установить заново, войти тем же аккаунтом — результаты вернулись.

- [ ] **Step 8: Коммит**

```bash
git add Assets/Scenes/SampleScene.unity Assets/UI/SafeArea/Tests/SceneWiringTests.cs
git commit -m "feat(backend): SyncService и панель аккаунта на сцене"
```

---

## Что этот план сознательно не делает

- Не заводит лидерборды и сравнение с другими пользователями.
- Не пишет админку: данные смотрим через дашборд Supabase.
- Не выгружает записи позы (`SaveRecording`) на сервер — это биометрия.
- Не синхронизирует громкости звука — настройка конкретного устройства.
- Не делает интеграционного теста против живого Supabase: он требует секретов в CI и падает от сети.
- Не решает три хвоста до релиза из спеки (тестовые пользователи, релизный SHA-1,
  политика конфиденциальности) — это действия в консолях, а не код.
