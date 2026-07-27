-- =================================================================
-- SMART SEMBAKO ASSISTANT — SUPABASE CLOUD DELTA SYNC SCHEMA & RLS
-- Version: 5.1.0
-- Description: Tabel sync, agregasi transaksi, queue alert & RLS policies
-- =================================================================

-- 1. Tabel Sync Produk (Delta Sync)
CREATE TABLE IF NOT EXISTS public.products_sync (
    id              TEXT PRIMARY KEY,
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
    date                DATE PRIMARY KEY,
    total_revenue       NUMERIC DEFAULT 0,
    total_profit        NUMERIC DEFAULT 0,
    total_transactions  INTEGER DEFAULT 0,
    top_products_json   JSONB,
    synced_at           TIMESTAMPTZ DEFAULT NOW()
);

-- 3. Tabel Antrean Alert (Cloud Push / Sidecar Trigger)
CREATE TABLE IF NOT EXISTS public.alerts_queue (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
    value           TEXT,
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);

-- =================================================================
-- ROW LEVEL SECURITY (RLS) POLICIES
-- =================================================================

-- Enable RLS di semua tabel
ALTER TABLE public.products_sync ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.transactions_summary ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.alerts_queue ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sync_metadata ENABLE ROW LEVEL SECURITY;

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
