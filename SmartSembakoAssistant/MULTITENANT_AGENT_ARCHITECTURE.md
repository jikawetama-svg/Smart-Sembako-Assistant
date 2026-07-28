# Smart Sembako Agent Platform — Rancangan Multi-Tenant, Aman, dan Selaras

Status: blueprint implementasi dan keputusan arsitektur per 28 Juli 2026.

## Tujuan

Membuat Smart Sembako Assistant dapat dipasang pada banyak toko dan banyak perangkat tanpa data, AI memory, notifikasi, ataupun akses pengguna saling bercampur. Desktop POS tetap menjadi sumber kebenaran transaksi; Cloud Bot menjadi interface yang aman, cepat, dan tersedia saat Desktop tidak aktif.

Setiap identitas harus selalu membawa:

```text
merchant_id  = identitas toko/tenant, contoh merchant_toko_teh_asiah
user_id      = identitas akun Supabase Auth
device_id    = identitas instalasi Desktop, contoh kasir-utama-01
channel_id   = Telegram/WhatsApp user yang telah dipetakan ke user toko
```

Tidak ada query, memory, notifikasi, atau sync tanpa `merchant_id`.

## Arsitektur target

```text
 Telegram / WhatsApp / Desktop UI
              │ identity + role
              ▼
      Smart Sembako Agent Gateway
   intent → policy → planner → tools → reflection
              │          │
              │          └── approval untuk aksi tulis
              ▼
       Supabase RLS / merchant boundary
              ▲
              │ snapshot read-only
 Desktop POS primary ── sync signed/authenticated ──► Cloud tables
              │
         Aronium POS (source of truth)
```

### Peran komponen

| Komponen | Tanggung jawab | Boleh menulis data POS? |
|---|---|---|
| Desktop POS primary | Transaksi, inventory, pembelian, OCR, sinkronisasi snapshot | Ya, melalui alur POS yang ada |
| Desktop read-only | Dashboard dan asisten lokal tanpa mengirim snapshot | Tidak |
| Cloud Bot | Membaca snapshot, menjawab, membuat draft/approval | Tidak |
| Supabase | Isolasi tenant, audit, memory, queue, status sync | Ya, hanya melalui RLS/RPC terotorisasi |
| LLM | Memahami bahasa dan merangkai jawaban berdasarkan fakta tool | Tidak pernah akses database langsung |

## Perubahan yang sudah diimplementasikan

- `SupabaseSettings` memiliki `MerchantId`, `DeviceId`, `EnforceTenantIsolation`, dan `SyncMode`.
- Desktop menolak sync saat tenant isolation aktif tetapi MerchantId/JWT belum ada.
- Snapshot produk mengirim `merchant_id`, `source_device_id`, dan `source_product_id`; ID cloud produk dinamespace menjadi `merchant_id:product_id`.
- Ringkasan transaksi memiliki ID tenant-aware `merchant_id:yyyy-mm-dd`.
- Perangkat `read_only` tidak diizinkan melakukan sync write.
- Cloud Bot fail-closed jika `MERCHANT_ID` belum dikonfigurasi dan menyuntikkan filter merchant ke query tools, memory, dan Store Brain.
- Sinyal failover Desktop → Cloud kini membawa `X-Merchant-ID`, jadi runtime cloud tidak menerima takeover dari tenant lain.
- Migrasi RLS tersedia di `data/supabase_multitenant_migration.sql`.
- Aturan jawaban Cloud diselaraskan: anti-halusinasi, stok minus tetap minus, akses role-aware, dan Cloud tidak menjanjikan aksi tulis.

## Setup tenant baru

1. Buat satu baris `merchants` untuk toko.
2. Buat user melalui Supabase Auth, lalu masukkan user tersebut ke `merchant_members` dengan role `owner`, `admin`, `cashier`, atau `viewer`.
3. Hubungkan Telegram user ID ke `merchant_members.telegram_user_id` setelah owner memverifikasi pengguna.
4. Buat `merchant_devices` untuk setiap instalasi Desktop.
5. Beri Desktop token JWT milik user/device yang menjadi anggota merchant tersebut; jangan pakai service-role key di aplikasi desktop.
6. Isi konfigurasi Desktop:

```json
"Supabase": {
  "Enabled": true,
  "MerchantId": "merchant_toko_teh_asiah",
  "DeviceId": "kasir-utama-01",
  "JwtToken": "<JWT Supabase user/device>",
  "EnforceTenantIsolation": true,
  "SyncMode": "primary"
}
```

7. Isi Render Cloud Bot untuk tenant yang sama:

```env
MERCHANT_ID=merchant_toko_teh_asiah
TENANT_ISOLATION_REQUIRED=true
OWNER_TELEGRAM_IDS=<id-owner-terverifikasi>
STORE_TIMEZONE=Asia/Jakarta
```

8. Jalankan `data/supabase_multitenant_migration.sql`, lakukan bootstrap merchant/member, lalu verifikasi RLS memakai akun dari merchant berbeda.

## Aturan perangkat agar tidak konflik

### Satu toko, beberapa perangkat

- Hanya satu Desktop diberi `SyncMode: primary` untuk satu database POS/write stream.
- Perangkat kedua memakai `read_only` sampai mekanisme queue/event sync dua-arah tersedia.
- Gunakan `device_id` unik, jangan salin file konfigurasi mentah ke perangkat lain.
- Jangan pernah memakai database Aronium yang sama dari dua komputer melalui folder jaringan tanpa dukungan resmi Aronium.

### Beberapa toko

- Setiap toko wajib memiliki `merchant_id` berbeda.
- Semua tabel cloud, user role, memory chat, scheduler lock, dan notification queue harus difilter RLS oleh merchant.
- Satu token bot Telegram sebaiknya digunakan untuk satu merchant pada tahap sekarang. Untuk satu bot melayani banyak merchant, gunakan Gateway tenant resolver berbasis `merchant_members.telegram_user_id` terlebih dahulu.

## Mekanisme AI ala agent yang direkomendasikan

Ini mengambil pola baik dari agent runtime seperti Hermes, tanpa membiarkan LLM mengambil aksi bebas.

```text
1. Identity Guard     → validasi channel user, merchant, role
2. Intent Router      → klasifikasi deterministik untuk command penting
3. Planner            → daftar data/tool yang dibutuhkan
4. Tool Executor      → query scoped merchant, paralel bila aman
5. Reflection         → cek data kosong, stale, anomali, otorisasi
6. Policy Gate        → read/draft/approval/execute
7. LLM Presenter      → bahasa natural dari fakta tervalidasi
8. Audit Event        → simpan intent, tools, latency, hasil, actor
```

### Tingkat aksi

| Tingkat | Contoh | Aturan |
|---|---|---|
| Read | stok, laporan, piutang | role dan RLS harus valid |
| Draft | draft restock, strategi promosi | tidak mengubah POS |
| Approval | restock besar, koreksi stok, pembayaran piutang | minta konfirmasi owner + token aksi sekali pakai |
| Execute | tulis ke POS | hanya Desktop primary setelah approval diverifikasi |

LLM hanya boleh berada pada Intent Router (opsional) dan Presenter. Ia tidak boleh menerima service key, melakukan SQL, memilih tenant, atau mengeksekusi aksi tulis secara langsung.

## Kontrak respons AI yang sama untuk Desktop dan Cloud

Kedua runtime wajib menerapkan aturan berikut.

1. Gunakan hanya fakta hasil tool/database; jangan menciptakan angka atau riwayat.
2. Stok negatif ditampilkan sebagai **minus**, bukan nol atau aman.
3. Bedakan data kosong, sync usang, akses ditolak, dan kegagalan koneksi.
4. Data profit/modal/piutang mengikuti RBAC; kasir tidak mendapat data owner-only.
5. Respons singkat, Bahasa Indonesia natural, bullet untuk daftar; tidak memakai tabel markdown panjang.
6. Jawaban follow-up memakai memory tenant + user yang sama saja.
7. Cloud menjelaskan bahwa perubahan data dilakukan di Desktop; jangan berpura-pura berhasil restock.
8. Setiap jawaban bisnis yang memakai snapshot menampilkan waktu sync bila lebih tua dari ambang yang ditetapkan.

## Gap yang masih ada dan prioritas pengerjaan

### P0 — Wajib sebelum produksi multi-toko

- [ ] Jalankan migrasi RLS dan pindahkan data legacy dari `legacy_unassigned` ke merchant yang tepat.
- [ ] Ganti desktop service-role key dengan JWT tenant terbatas; service role hanya boleh berada pada backend privat/Edge Function.
- [ ] Buat flow verifikasi channel: Telegram/WhatsApp tidak otomatis menjadi owner hanya karena mengirim pesan.
- [ ] Tambahkan test isolasi: user toko A tidak bisa membaca/menulis semua tabel toko B.
- [ ] Tambahkan `sync_health` yang memeriksa last sync, device primary, lag, dan schema version.
- [ ] Pastikan ringkasan penjualan tetap disinkronkan walaupun produk tidak berubah.

### P1 — Akurasi dan parity fitur

- [ ] Sinkronkan `restock_sync`, `inventory_sync`, pelanggan/piutang, dan `top_products_json` dari Desktop; tool Cloud sudah ada tetapi sumber datanya belum lengkap.
- [ ] Tambahkan timestamp data, `sync_version`, dan checksum snapshot untuk mendeteksi stale/partial sync.
- [ ] Implementasikan `restock_forecast` nyata: konsumsi harian, stok minimum, lead time supplier, safety stock, kemasan, dan modal.
- [ ] Hilangkan tool planner yang belum terdaftar (`predict_restock`, analisis profit) atau implementasikan secara nyata.
- [ ] Samakan command reference dan help antara Desktop, Telegram Cloud, WhatsApp, dan UI.

### P2 — Security & reliability

- [ ] Gunakan Supabase Edge Function untuk endpoint sync; validasi JWT, device aktif, schema version, timestamp, dan idempotency key sebelum upsert.
- [ ] Buat `audit_events`: merchant, actor, device, channel, intent, tool, approval, result, trace ID, latency.
- [ ] Terapkan rate limit per merchant/user/channel, anti-replay nonce untuk action approval, dan rotasi secret.
- [ ] Scheduler harus memakai distributed lock atomik (RPC/advisory lock), bukan read-then-write Store Brain yang berisiko race condition.
- [ ] Backup terenkripsi, retention policy memory, dan prosedur revoke user/device.

### P3 — Agent product capabilities

- [ ] Memory jangka panjang berupa fakta terstruktur yang disetujui owner, bukan seluruh transcript.
- [ ] Workflow tutup toko: sync → validasi kas → backup → laporan → approval → notifikasi.
- [ ] Supplier agent: draft pesanan berdasarkan forecast, tidak mengirim pesan supplier tanpa approval.
- [ ] Insight penjualan: promo, bundling, dead stock, dan repeat customer dengan confidence + sumber data.
- [ ] Local LLM fallback untuk intent sederhana dan narasi offline; tool/data tetap dari POS lokal.

## Migrasi aman dari sistem saat ini

1. Backup database Supabase dan konfigurasi terenkripsi Desktop.
2. Matikan scheduler/notifikasi agar tidak ada duplikasi selama migrasi.
3. Jalankan SQL migrasi; data lama masuk `legacy_unassigned` dan tidak dapat dibaca user biasa.
4. Buat merchant dan owner, lalu pindahkan hanya data toko tersebut dari quarantine tenant.
5. Atur Desktop primary dengan MerchantId, DeviceId, JWT; uji sync pada satu produk dummy.
6. Atur Render `MERCHANT_ID`; cek `/bot-health` harus `tenant_isolation_ready: true`.
7. Uji dengan dua akun: owner toko A dapat melihat datanya; akun toko B harus menerima RLS denial/hasil kosong tanpa kebocoran.
8. Baru hidupkan kembali scheduler dan webhook.

## Risiko desain yang harus dihindari

- Jangan mengandalkan filter Python/C# saja; filter itu hanya lapisan tambahan. RLS adalah batas keamanan wajib.
- Jangan membagikan service-role key ke installer, `.env` pengguna, Telegram, atau client desktop.
- Jangan memetakan user ke merchant melalui nama toko bebas atau input LLM.
- Jangan membiarkan dua device primary mengirim snapshot stok dari sumber yang berbeda tanpa event/version conflict resolver.
- Jangan menyatakan “stok aman” jika query gagal atau snapshot tidak pernah tersinkronisasi.

## Definition of Done

Sistem siap multi-toko ketika seluruh checklist berikut terpenuhi:

- Semua tabel operasional memuat `merchant_id` dan RLS lulus test cross-tenant.
- Semua perangkat mempunyai `device_id`; hanya primary aktif untuk sync write.
- Semua channel user diverifikasi dan memiliki role membership.
- Cloud dan local menghasilkan respons fakta/role/sync-status yang konsisten.
- Semua aksi tulis memiliki approval dan audit trail.
- Dashboard menunjukkan status sinkronisasi, device primary, lag, dan error terakhir per merchant.
