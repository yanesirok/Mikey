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
