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
