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
   '00000000-0000-0000-0000-000000000000', 'authenticated','authenticated','b@example.test','',now(),now());

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
        and technique_id = 'stance-zenkutsu') is distinct from 4 then
    raise exception 'уровень 1 понизился';
  end if;

  select count(*) into n from public.level1_progress
   where user_id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if n <> 1 then raise exception 'пустой technique_id не был пропущен, строк: %', n; end if;

  insert into check_log values ('OK: слияние берёт максимум и уважает штамп времени');
end $$;

-- ---- Регрессия (Task 2, ревью R1 №3): кривое число в профиле не должно ронять
-- остальной синк. age=8 нарушает диапазон 10..100, safe_int обязан занулить его,
-- а не отвергнуть весь вызов — иначе squat_reps из того же payload не сохранится.
select public.sync_progress('{
  "profile": {"age":8,"profile_updated_at":"2026-08-21T10:00:00Z"},
  "level0":  {"squat_reps":50}
}'::jsonb);

do $$
declare r record;
begin
  select * into r from public.profiles where id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if r.age <> 0 then
    raise exception 'кривой возраст не занулился -> %', r.age;
  end if;

  select * into r from public.level0_results where user_id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if r.squat_reps <> 50 then
    raise exception 'кривое число в профиле уронило остальной синк -> %', r.squat_reps;
  end if;

  insert into check_log values ('OK: кривое число в профиле не роняет остальной синк');
end $$;

-- ---- Регрессия (Task 2, ревью R1 №2): NaN не должен навсегда выигрывать greatest.
-- Старый рекорд wallsit_seconds=45 обязан уцелеть, а не превратиться в NaN/0.
select public.sync_progress('{"level0":{"wallsit_seconds":"NaN"}}'::jsonb);

do $$
declare r record;
begin
  select * into r from public.level0_results where user_id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if r.wallsit_seconds <> 45 then
    raise exception 'NaN испортил рекорд wallsit_seconds -> %', r.wallsit_seconds;
  end if;
  insert into check_log values ('OK: NaN не проходит и не портит рекорд');
end $$;

-- ---- Регрессия (Task 2, ревью R1 №1): будущий штамп не должен морозить профиль
-- навсегда. now() заморожен внутри транзакции, поэтому проверка устроена не как
-- «после будущего штампа настоящая правка проходит» (в одной транзакции это
-- недоказуемо), а как «сохранённый штамп не больше now()».
select public.sync_progress('{
  "profile": {"display_name":"ИзБудущего","profile_updated_at":"2030-01-01T00:00:00Z"}
}'::jsonb);

do $$
declare r record;
begin
  select * into r from public.profiles where id = 'aaaaaaaa-0000-0000-0000-000000000001';
  if r.profile_updated_at > now() then
    raise exception 'будущий штамп сохранён как есть -> %', r.profile_updated_at;
  end if;
  insert into check_log values ('OK: будущий штамп капается до now(), профиль не заморожен');
end $$;

-- ---- Пользователь B не видит и не трогает данные A ----
set local request.jwt.claims = '{"sub":"bbbbbbbb-0000-0000-0000-000000000002","role":"authenticated"}';

select public.sync_progress('{"profile":{"display_name":"Б"},"level0":{"pushup_reps":999},"level1":[]}'::jsonb);

do $$
begin
  if (select pushup_reps from public.level0_results
      where user_id = 'aaaaaaaa-0000-0000-0000-000000000001') is distinct from 20 then
    raise exception 'ИЗОЛЯЦИЯ НАРУШЕНА: синк B изменил данные A';
  end if;
  if (select pushup_reps from public.level0_results
      where user_id = 'bbbbbbbb-0000-0000-0000-000000000002') is distinct from 999 then
    raise exception 'данные B не записались';
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
