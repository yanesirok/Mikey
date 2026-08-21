-- Безопасное чтение числа из jsonb. Возвращает 0 на всём, что не является
-- числом в допустимом диапазоне: на мусоре, на пропущенном ключе, на NaN и на
-- значении, которое переполнило бы целевой тип.
--
-- Приведение идёт через numeric: он не переполняется, поэтому проверка
-- диапазона успевает отработать раньше, чем возникнет ошибка. Именно порядок
-- «сначала диапазон, потом узкий тип» и закрывает дефект — обратный порядок
-- роняет транзакцию целиком вместе с чужим прогрессом.
create or replace function public.safe_int(v jsonb, lo int, hi int)
returns int language sql immutable as $$
  select case
    when v is null or jsonb_typeof(v) = 'null' then 0
    when jsonb_typeof(v) = 'number'
         and (v #>> '{}') ~ '^-?[0-9]{1,15}(\.[0-9]+)?$'
         and (v #>> '{}')::numeric between lo and hi
      then trunc((v #>> '{}')::numeric)::int
    when jsonb_typeof(v) = 'string'
         and (v #>> '{}') ~ '^-?[0-9]{1,15}(\.[0-9]+)?$'
         and (v #>> '{}')::numeric between lo and hi
      then trunc((v #>> '{}')::numeric)::int
    else 0
  end;
$$;

create or replace function public.safe_real(v jsonb, lo real, hi real)
returns real language sql immutable as $$
  select case
    when v is null or jsonb_typeof(v) = 'null' then 0
    when jsonb_typeof(v) in ('number', 'string')
         and (v #>> '{}') ~ '^-?[0-9]{1,15}(\.[0-9]+)?$'
         and (v #>> '{}')::numeric between lo::numeric and hi::numeric
      then (v #>> '{}')::numeric::real
    else 0
  end;
$$;

revoke all on function public.safe_int(jsonb, int, int) from public;
revoke all on function public.safe_real(jsonb, real, real) from public;

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

  -- Штамп приходит из недоверенного источника. Устройство со сбитыми вперёд
  -- часами иначе заморозило бы профиль навсегда: его «будущее» время никогда
  -- не будет перекрыто настоящей правкой.
  incoming_ts := least(incoming_ts, now());

  -- Профиль: правки — последняя запись побеждает; прогресс туториала — максимум.
  insert into public.profiles as pr
    (id, display_name, gender, age, weight_kg, height_cm, tutorial_progress, profile_updated_at)
  values
    (uid,
     coalesce(p->>'display_name', 'Mikey'),
     coalesce(p->>'gender', ''),
     public.safe_int (p->'age',               10,  100),
     public.safe_real(p->'weight_kg',         30,  300),
     public.safe_int (p->'height_cm',        100,  250),
     public.safe_int (p->'tutorial_progress',  0,   10),
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
     public.safe_int (l0->'pushup_reps',           0, 100000),
     public.safe_int (l0->'squat_reps',            0, 100000),
     public.safe_int (l0->'yokogeri_slow_reps',    0, 100000),
     public.safe_int (l0->'yokogeri_best_zone',    0,     10),
     public.safe_real(l0->'wallsit_seconds',       0,  86400),
     public.safe_real(l0->'yokogeri_hold_seconds', 0,  86400),
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
    values (uid, tid, public.safe_int(t->'clean_reps', 0, 100000), now())
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
