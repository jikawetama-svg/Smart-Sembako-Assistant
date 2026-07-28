-- =================================================================
-- SMART SEMBAKO ASSISTANT — SUPABASE CLOUD DELTA SYNC SCHEMA & RLS
-- Version: 5.1.0
-- Description: Tabel sync, agregasi transaksi, queue alert & RLS policies
-- =================================================================

-- 0. Tenant ringan untuk setup otomatis satu toko.
-- Aplikasi Desktop akan membuat row merchant/device secara otomatis saat sync.
CREATE TABLE IF NOT EXISTS public.merchants (
    id           TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    timezone     TEXT NOT NULL DEFAULT 'Asia/Jakarta',
    status       TEXT NOT NULL DEFAULT 'active',
    created_at   TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.merchant_devices (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    merchant_id    TEXT NOT NULL REFERENCES public.merchants(id) ON DELETE CASCADE,
    device_id      TEXT NOT NULL,
    label          TEXT,
    last_seen_at   TIMESTAMPTZ,
    revoked_at     TIMESTAMPTZ,
    created_at     TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE (merchant_id, device_id)
);

-- 1. Tabel Sync Produk (Delta Sync)
CREATE TABLE IF NOT EXISTS public.products_sync (
    id              TEXT PRIMARY KEY,
    merchant_id     TEXT,
    source_product_id TEXT,
    source_device_id TEXT,
    name            TEXT NOT NULL,
    stock           NUMERIC NOT NULL DEFAULT 0,
    unit            TEXT DEFAULT 'pcs',
    selling_price   NUMERIC DEFAULT 0,
    is_low_stock    BOOLEAN DEFAULT FALSE,
    category_name   TEXT,
    barcode         TEXT,
    synced_at       TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);

-- Index untuk mempercepat query pencarian produk oleh Cloud Bot
CREATE INDEX IF NOT EXISTS idx_products_sync_name ON public.products_sync (name);
CREATE INDEX IF NOT EXISTS idx_products_sync_barcode ON public.products_sync (barcode);
CREATE INDEX IF NOT EXISTS idx_products_sync_low_stock ON public.products_sync (is_low_stock) WHERE is_low_stock = TRUE;

-- 2. Tabel Ringkasan Transaksi Harian (Agregat)
CREATE TABLE IF NOT EXISTS public.transactions_summary (
    id                  TEXT PRIMARY KEY,
    merchant_id         TEXT,
    source_device_id    TEXT,
    date                DATE NOT NULL,
    total_revenue       NUMERIC DEFAULT 0,
    total_profit        NUMERIC DEFAULT 0,
    total_transactions  INTEGER DEFAULT 0,
    top_products_json   JSONB,
    synced_at           TIMESTAMPTZ DEFAULT NOW()
);

-- 3. Tabel Antrean Alert (Cloud Push / Sidecar Trigger)
CREATE TABLE IF NOT EXISTS public.alerts_queue (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    merchant_id     TEXT,
    type            TEXT NOT NULL, -- 'low_stock' | 'expiry' | 'anomaly'
    payload         JSONB NOT NULL,
    handled         BOOLEAN DEFAULT FALSE,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

-- Index antrean alert yang belum ditangani
CREATE INDEX IF NOT EXISTS idx_alerts_queue_unhandled ON public.alerts_queue (handled, created_at) WHERE handled = FALSE;

-- 4. Tabel Metadata Sinkronisasi
CREATE TABLE IF NOT EXISTS public.sync_metadata (
    key             TEXT PRIMARY KEY,
    merchant_id     TEXT,
    value           TEXT,
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);

-- 5. Tabel AI Memory (Conversational Memory per Telegram User)
CREATE TABLE IF NOT EXISTS public.conversations_memory (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    merchant_id     TEXT,
    user_id         BIGINT NOT NULL,
    role            TEXT NOT NULL, -- 'user' | 'assistant' | 'system'
    content         TEXT NOT NULL,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_conversations_memory_user ON public.conversations_memory (user_id, created_at DESC);

-- 6. Tabel Sync Riwayat Restock / Pembelian Barang
CREATE TABLE IF NOT EXISTS public.restock_sync (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    merchant_id     TEXT,
    source_device_id TEXT,
    product_name    TEXT NOT NULL,
    quantity        NUMERIC NOT NULL DEFAULT 0,
    unit            TEXT DEFAULT 'pcs',
    supplier_name   TEXT,
    purchase_price  NUMERIC DEFAULT 0,
    restock_date    TIMESTAMPTZ DEFAULT NOW(),
    synced_at       TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_restock_sync_product ON public.restock_sync (product_name);

-- 7. Tabel Sync Riwayat Koreksi Inventory
CREATE TABLE IF NOT EXISTS public.inventory_sync (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    merchant_id     TEXT,
    source_device_id TEXT,
    product_name    TEXT NOT NULL,
    quantity_before NUMERIC DEFAULT 0,
    quantity_after  NUMERIC DEFAULT 0,
    delta           NUMERIC DEFAULT 0,
    reason          TEXT,
    corrected_at    TIMESTAMPTZ DEFAULT NOW(),
    synced_at       TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_inventory_sync_product ON public.inventory_sync (product_name);

-- 8. Tabel Sync Pelanggan & Piutang
CREATE TABLE IF NOT EXISTS public.customers_sync (
    id                    TEXT PRIMARY KEY,
    merchant_id           TEXT,
    name                  TEXT NOT NULL,
    phone                 TEXT,
    total_debt            NUMERIC DEFAULT 0,
    last_transaction_date TIMESTAMPTZ,
    source_device_id      TEXT,
    synced_at             TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_customers_sync_name ON public.customers_sync (name);
CREATE INDEX IF NOT EXISTS idx_customers_sync_debt ON public.customers_sync (total_debt DESC);

-- 9. Tabel Sync Supplier
CREATE TABLE IF NOT EXISTS public.suppliers_sync (
    id               TEXT PRIMARY KEY,
    merchant_id      TEXT,
    name             TEXT NOT NULL,
    phone            TEXT,
    email            TEXT,
    address          TEXT,
    source_device_id TEXT,
    synced_at        TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_suppliers_sync_name ON public.suppliers_sync (name);

-- 10. Queue perintah Cloud -> Desktop.
-- Cloud Bot hanya membuat draft perintah. Desktop lokal yang mengeksekusi
-- saat aplikasi dibuka agar pos.db tetap menjadi sumber tulis utama.
CREATE TABLE IF NOT EXISTS public.agent_command_queue (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    merchant_id           TEXT,
    source_channel        TEXT NOT NULL DEFAULT 'telegram',
    source_chat_id        TEXT NOT NULL,
    source_user_id        TEXT,
    source_message_id     TEXT,
    command_text          TEXT NOT NULL,
    command_kind          TEXT NOT NULL,
    status                TEXT NOT NULL DEFAULT 'pending'
                          CHECK (status IN ('pending', 'processing', 'completed', 'failed', 'cancelled')),
    requires_local_app    BOOLEAN NOT NULL DEFAULT TRUE,
    error_message         TEXT,
    result_text           TEXT,
    claimed_by            TEXT,
    claimed_at            TIMESTAMPTZ,
    completed_at          TIMESTAMPTZ,
    created_at            TIMESTAMPTZ DEFAULT NOW(),
    updated_at            TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_agent_command_queue_pending
    ON public.agent_command_queue (merchant_id, status, created_at)
    WHERE status IN ('pending', 'processing');

-- 11. Tabel Store Brain (Business Memory per Store ID & User ID)
CREATE TABLE IF NOT EXISTS public.store_brain (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    merchant_id     TEXT,
    store_id        TEXT NOT NULL DEFAULT 'store_main',
    user_id         BIGINT NOT NULL DEFAULT 0,
    user_role       TEXT NOT NULL DEFAULT 'owner', -- 'owner' | 'admin' | 'kasir'
    category        TEXT NOT NULL DEFAULT 'preference', -- 'preference' | 'supplier' | 'pattern'
    key             TEXT NOT NULL,
    value           JSONB,
    updated_at      TIMESTAMPTZ DEFAULT NOW(),
    CONSTRAINT uq_store_brain_key UNIQUE (store_id, key)
);

CREATE INDEX IF NOT EXISTS idx_store_brain_store ON public.store_brain (store_id, user_id);

-- Compatibility untuk database lama: CREATE TABLE IF NOT EXISTS tidak menambah
-- kolom pada tabel yang sudah ada, jadi pastikan kolom cloud/tenant tersedia.
ALTER TABLE public.products_sync ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.products_sync ADD COLUMN IF NOT EXISTS source_product_id TEXT;
ALTER TABLE public.products_sync ADD COLUMN IF NOT EXISTS source_device_id TEXT;
ALTER TABLE public.transactions_summary ADD COLUMN IF NOT EXISTS id TEXT;
ALTER TABLE public.transactions_summary ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.transactions_summary ADD COLUMN IF NOT EXISTS source_device_id TEXT;
UPDATE public.transactions_summary
SET id = COALESCE(id, COALESCE(merchant_id, 'merchant_smart_sembako') || ':' || date::TEXT)
WHERE id IS NULL;
ALTER TABLE public.alerts_queue ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.sync_metadata ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.conversations_memory ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.restock_sync ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.restock_sync ADD COLUMN IF NOT EXISTS source_device_id TEXT;
ALTER TABLE public.inventory_sync ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.inventory_sync ADD COLUMN IF NOT EXISTS source_device_id TEXT;
ALTER TABLE public.customers_sync ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.customers_sync ADD COLUMN IF NOT EXISTS source_device_id TEXT;
ALTER TABLE public.suppliers_sync ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.suppliers_sync ADD COLUMN IF NOT EXISTS source_device_id TEXT;
ALTER TABLE public.agent_command_queue ADD COLUMN IF NOT EXISTS merchant_id TEXT;
ALTER TABLE public.store_brain ADD COLUMN IF NOT EXISTS merchant_id TEXT;

CREATE INDEX IF NOT EXISTS idx_products_sync_merchant_name ON public.products_sync (merchant_id, name);
CREATE INDEX IF NOT EXISTS idx_transactions_summary_merchant_date ON public.transactions_summary (merchant_id, date DESC);
CREATE INDEX IF NOT EXISTS idx_customers_sync_merchant_name ON public.customers_sync (merchant_id, name);
CREATE INDEX IF NOT EXISTS idx_suppliers_sync_merchant_name ON public.suppliers_sync (merchant_id, name);

-- =================================================================
-- ROW LEVEL SECURITY (RLS) POLICIES
-- =================================================================

-- Enable RLS di semua tabel
ALTER TABLE public.products_sync ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.transactions_summary ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.alerts_queue ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sync_metadata ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.conversations_memory ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.restock_sync ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.inventory_sync ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.customers_sync ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.suppliers_sync ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.agent_command_queue ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.store_brain ENABLE ROW LEVEL SECURITY;

-- Policy products_sync:
-- Service role (C# Desktop Sync Engine) memiliki akses FULL (ALL)
DROP POLICY IF EXISTS "service_role_full_access_products" ON public.products_sync;
CREATE POLICY "service_role_full_access_products" ON public.products_sync
    FOR ALL USING (auth.role() = 'service_role' OR auth.role() = 'authenticated');

-- Cloud Bot & Anon read access
DROP POLICY IF EXISTS "bot_read_products" ON public.products_sync;
CREATE POLICY "bot_read_products" ON public.products_sync
    FOR SELECT USING (TRUE);

-- Policy transactions_summary:
DROP POLICY IF EXISTS "service_role_full_access_transactions" ON public.transactions_summary;
CREATE POLICY "service_role_full_access_transactions" ON public.transactions_summary
    FOR ALL USING (auth.role() = 'service_role' OR auth.role() = 'authenticated');

DROP POLICY IF EXISTS "bot_read_transactions" ON public.transactions_summary;
CREATE POLICY "bot_read_transactions" ON public.transactions_summary
    FOR SELECT USING (TRUE);

-- Policy alerts_queue:
DROP POLICY IF EXISTS "service_role_full_access_alerts" ON public.alerts_queue;
CREATE POLICY "service_role_full_access_alerts" ON public.alerts_queue
    FOR ALL USING (auth.role() = 'service_role' OR auth.role() = 'authenticated');

DROP POLICY IF EXISTS "bot_read_alerts" ON public.alerts_queue;
CREATE POLICY "bot_read_alerts" ON public.alerts_queue
    FOR SELECT USING (TRUE);

-- Policy sync_metadata:
DROP POLICY IF EXISTS "service_role_full_access_metadata" ON public.sync_metadata;
CREATE POLICY "service_role_full_access_metadata" ON public.sync_metadata
    FOR ALL USING (auth.role() = 'service_role' OR auth.role() = 'authenticated');

DROP POLICY IF EXISTS "bot_read_metadata" ON public.sync_metadata;
CREATE POLICY "bot_read_metadata" ON public.sync_metadata
    FOR SELECT USING (TRUE);

-- Policy conversations_memory, restock_sync, inventory_sync:
ALTER TABLE public.conversations_memory ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.restock_sync ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.inventory_sync ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "full_access_conversations_memory" ON public.conversations_memory;
DROP POLICY IF EXISTS "full_access_restock_sync" ON public.restock_sync;
DROP POLICY IF EXISTS "full_access_inventory_sync" ON public.inventory_sync;
DROP POLICY IF EXISTS "full_access_customers_sync" ON public.customers_sync;
DROP POLICY IF EXISTS "full_access_suppliers_sync" ON public.suppliers_sync;
DROP POLICY IF EXISTS "full_access_agent_command_queue" ON public.agent_command_queue;
CREATE POLICY "full_access_conversations_memory" ON public.conversations_memory FOR ALL USING (TRUE);
CREATE POLICY "full_access_restock_sync" ON public.restock_sync FOR ALL USING (TRUE);
CREATE POLICY "full_access_inventory_sync" ON public.inventory_sync FOR ALL USING (TRUE);
CREATE POLICY "full_access_customers_sync" ON public.customers_sync FOR ALL USING (TRUE);
CREATE POLICY "full_access_suppliers_sync" ON public.suppliers_sync FOR ALL USING (TRUE);
CREATE POLICY "full_access_agent_command_queue" ON public.agent_command_queue FOR ALL USING (TRUE);
