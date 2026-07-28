-- SMART SEMBAKO ASSISTANT — MULTI-TENANT SECURITY MIGRATION
-- Run once in Supabase SQL Editor AFTER taking a backup.
-- This migration intentionally fails closed: an authenticated user can only
-- access a merchant that is explicitly present in merchant_members.

begin;

create table if not exists public.merchants (
  id text primary key check (id ~ '^[a-z0-9][a-z0-9_-]{2,63}$'),
  display_name text not null,
  timezone text not null default 'Asia/Jakarta',
  status text not null default 'active' check (status in ('active', 'suspended')),
  created_at timestamptz not null default now()
);

create table if not exists public.merchant_members (
  merchant_id text not null references public.merchants(id) on delete cascade,
  user_id uuid not null references auth.users(id) on delete cascade,
  role text not null check (role in ('owner', 'admin', 'cashier', 'viewer')),
  telegram_user_id bigint,
  is_active boolean not null default true,
  created_at timestamptz not null default now(),
  primary key (merchant_id, user_id),
  unique (merchant_id, telegram_user_id)
);

create table if not exists public.merchant_devices (
  id uuid primary key default gen_random_uuid(),
  merchant_id text not null references public.merchants(id) on delete cascade,
  device_id text not null,
  label text,
  last_seen_at timestamptz,
  revoked_at timestamptz,
  created_at timestamptz not null default now(),
  unique (merchant_id, device_id)
);

-- Helper functions run with a fixed search_path to avoid search-path attacks.
create or replace function public.is_merchant_member(target_merchant_id text)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.merchant_members m
    join public.merchants mt on mt.id = m.merchant_id and mt.status = 'active'
    where m.merchant_id = target_merchant_id
      and m.user_id = auth.uid()
      and m.is_active = true
  );
$$;

create or replace function public.can_write_merchant(target_merchant_id text)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.merchant_members m
    where m.merchant_id = target_merchant_id
      and m.user_id = auth.uid()
      and m.is_active = true
      and m.role in ('owner', 'admin')
  );
$$;

-- Add tenant/audit columns to every operational table. Existing records are
-- deliberately marked 'legacy_unassigned' and must be assigned before go-live.
alter table public.products_sync add column if not exists merchant_id text references public.merchants(id);
alter table public.products_sync add column if not exists source_product_id text;
alter table public.products_sync add column if not exists source_device_id text;
alter table public.transactions_summary add column if not exists id text;
alter table public.transactions_summary add column if not exists merchant_id text references public.merchants(id);
alter table public.transactions_summary add column if not exists source_device_id text;
alter table public.restock_sync add column if not exists merchant_id text references public.merchants(id);
alter table public.restock_sync add column if not exists source_device_id text;
alter table public.inventory_sync add column if not exists merchant_id text references public.merchants(id);
alter table public.inventory_sync add column if not exists source_device_id text;
alter table public.customers_sync add column if not exists merchant_id text references public.merchants(id);
alter table public.customers_sync add column if not exists source_device_id text;
alter table public.suppliers_sync add column if not exists merchant_id text references public.merchants(id);
alter table public.suppliers_sync add column if not exists source_device_id text;
alter table public.agent_command_queue add column if not exists merchant_id text references public.merchants(id);
alter table public.conversations_memory add column if not exists merchant_id text references public.merchants(id);
alter table public.store_brain add column if not exists merchant_id text references public.merchants(id);
alter table public.sync_metadata add column if not exists merchant_id text references public.merchants(id);
alter table public.alerts_queue add column if not exists merchant_id text references public.merchants(id);

-- Legacy records are quarantined, never exposed by RLS, until an owner moves
-- them deliberately to a real merchant after checking the backup.
insert into public.merchants (id, display_name, status)
values ('legacy_unassigned', 'Legacy data — requires assignment', 'suspended')
on conflict (id) do nothing;
update public.products_sync set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.transactions_summary set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.restock_sync set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.inventory_sync set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.customers_sync set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.suppliers_sync set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.agent_command_queue set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.conversations_memory set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.store_brain set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.sync_metadata set merchant_id = 'legacy_unassigned' where merchant_id is null;
update public.alerts_queue set merchant_id = 'legacy_unassigned' where merchant_id is null;

-- After backup verification, replace <merchant_id> and move only the records
-- that genuinely belong to that merchant from the quarantine tenant.
-- update public.products_sync set merchant_id = '<merchant_id>' where merchant_id = 'legacy_unassigned';

-- Product IDs are globally namespaced by Desktop as merchant_id:source_product_id.
create unique index if not exists uq_products_sync_merchant_source
  on public.products_sync (merchant_id, source_product_id)
  where source_product_id is not null;

-- Daily summaries need a merchant-aware key, because `date` alone is not unique.
update public.transactions_summary
set id = merchant_id || ':' || date::text
where id is null and merchant_id is not null;
alter table public.transactions_summary alter column id set not null;
alter table public.transactions_summary drop constraint if exists transactions_summary_pkey;
alter table public.transactions_summary add primary key (id);

alter table public.store_brain drop constraint if exists uq_store_brain_key;
create unique index if not exists uq_store_brain_merchant_key
  on public.store_brain (merchant_id, store_id, key);
create index if not exists idx_products_sync_merchant_name on public.products_sync (merchant_id, name);
create index if not exists idx_customers_sync_merchant_name on public.customers_sync (merchant_id, name);
create index if not exists idx_suppliers_sync_merchant_name on public.suppliers_sync (merchant_id, name);
create index if not exists idx_agent_command_queue_merchant_status on public.agent_command_queue (merchant_id, status, created_at);
create index if not exists idx_transactions_summary_merchant_date on public.transactions_summary (merchant_id, date desc);
create index if not exists idx_memory_merchant_user on public.conversations_memory (merchant_id, user_id, created_at desc);

alter table public.merchants enable row level security;
alter table public.merchant_members enable row level security;
alter table public.merchant_devices enable row level security;

-- Remove legacy permissive policies before installing tenant-scoped policies.
drop policy if exists "bot_read_products" on public.products_sync;
drop policy if exists "service_role_full_access_products" on public.products_sync;
drop policy if exists "bot_read_transactions" on public.transactions_summary;
drop policy if exists "service_role_full_access_transactions" on public.transactions_summary;
drop policy if exists "bot_read_alerts" on public.alerts_queue;
drop policy if exists "service_role_full_access_alerts" on public.alerts_queue;
drop policy if exists "bot_read_metadata" on public.sync_metadata;
drop policy if exists "service_role_full_access_metadata" on public.sync_metadata;
drop policy if exists "full_access_conversations_memory" on public.conversations_memory;
drop policy if exists "full_access_restock_sync" on public.restock_sync;
drop policy if exists "full_access_inventory_sync" on public.inventory_sync;
drop policy if exists "full_access_customers_sync" on public.customers_sync;
drop policy if exists "full_access_suppliers_sync" on public.suppliers_sync;
drop policy if exists "full_access_agent_command_queue" on public.agent_command_queue;

create policy "member_read_merchant" on public.merchants for select using (public.is_merchant_member(id));
create policy "member_read_members" on public.merchant_members for select using (public.is_merchant_member(merchant_id));
create policy "owner_manage_members" on public.merchant_members for all using (public.can_write_merchant(merchant_id)) with check (public.can_write_merchant(merchant_id));
create policy "member_read_devices" on public.merchant_devices for select using (public.is_merchant_member(merchant_id));
create policy "owner_manage_devices" on public.merchant_devices for all using (public.can_write_merchant(merchant_id)) with check (public.can_write_merchant(merchant_id));

-- Same safe policy shape for all tenant data tables.
do $$
declare tbl text;
begin
  foreach tbl in array array['products_sync','transactions_summary','restock_sync','inventory_sync','customers_sync','suppliers_sync','agent_command_queue','conversations_memory','store_brain','sync_metadata','alerts_queue']
  loop
    execute format('alter table public.%I enable row level security', tbl);
    execute format('drop policy if exists tenant_read on public.%I', tbl);
    execute format('drop policy if exists tenant_write on public.%I', tbl);
    execute format('drop policy if exists tenant_update on public.%I', tbl);
    execute format('drop policy if exists tenant_delete on public.%I', tbl);
    execute format('create policy tenant_read on public.%I for select using (public.is_merchant_member(merchant_id))', tbl);
    execute format('create policy tenant_write on public.%I for insert with check (public.can_write_merchant(merchant_id))', tbl);
    execute format('create policy tenant_update on public.%I for update using (public.can_write_merchant(merchant_id)) with check (public.can_write_merchant(merchant_id))', tbl);
    execute format('create policy tenant_delete on public.%I for delete using (public.can_write_merchant(merchant_id))', tbl);
  end loop;
end $$;

commit;

-- Bootstrap (run separately; replace placeholders):
-- insert into public.merchants (id, display_name) values ('merchant_toko_teh_asiah', 'Toko Sembako Teh Asiah');
-- insert into public.merchant_members (merchant_id, user_id, role, telegram_user_id)
-- values ('merchant_toko_teh_asiah', '<auth.users UUID>', 'owner', <telegram user id>);
