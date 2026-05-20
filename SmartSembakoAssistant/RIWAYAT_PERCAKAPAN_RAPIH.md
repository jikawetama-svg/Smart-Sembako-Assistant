# 📋 Riwayat Percakapan & Status Implementasi
## Smart Sembako Assistant

> **Dokumen ini** merangkum topik diskusi utama dan mencocokkannya dengan kondisi proyek saat ini.  
> **Catatan:** untuk mekanisme inventory final yang berlaku di kode, gunakan `QUICK_INVENTORY.md` sebagai sumber kebenaran.  
> **Terakhir diperbarui:** Mei 2026

---

## 🗂️ Daftar Isi

1. [Latar Belakang](#1-latar-belakang)
2. [Topik yang Dibahas](#2-topik-yang-dibahas)
3. [Status Implementasi per Fitur](#3-status-implementasi-per-fitur)
4. [Checklist Lengkap](#4-checklist-lengkap)
5. [Fitur Belum Diimplementasi](#5-fitur-belum-diimplementasi)
6. [Roadmap Selanjutnya](#6-roadmap-selanjutnya)

---

## 1. Latar Belakang

Percakapan ini membahas pengembangan **Smart Sembako Assistant (SSA)** — aplikasi desktop WPF .NET 8 yang terintegrasi dengan **Aronium POS** (`pos.db`) dan dikendalikan via **Telegram Bot + AI (Groq/Gemini)**.

Fokus percakapan: membangun sistem manajemen stok cerdas yang **anti-error**, **semi-otomatis**, dan **tidak tergantung penuh pada AI**.

---

## 2. Topik yang Dibahas

### 🔹 A. Masalah Awal — Restock & Inventory Masuk sebagai Revenue/Profit
Sistem awal salah menghitung profit karena tidak memfilter `DocumentTypeId`. Solusi:

| DocumentTypeId | Jenis | Masuk Revenue? |
|---|---|---|
| `100` | Purchase / Restock | ❌ TIDAK |
| `200` | Sales / Penjualan | ✅ YA |
| `300` | Inventory Count | ❌ TIDAK |
| `400` | Loss | ❌ TIDAK |

**Query profit yang benar:**
```sql
SELECT SUM(Total) as Revenue FROM Document WHERE DocumentTypeId = 200
```

---

### 🔹 B. Mekanisme Quick Inventory vs Restock

| Fitur | Tujuan | DocumentTypeId | Efek |
|---|---|---|---|
| **Restock** | Tambah stok (barang datang) | `100` | `stok += qty` |
| **Inventory** | Koreksi stok (SET target) | `300` | `selisih = target - stok_sekarang` |

**Rule Sakti:**
- Kalau barang **DATANG** → `RESTOCK`
- Kalau stok **SALAH** → `INVENTORY` (koreksi)

**Logika inventory yang benar:**
```
selisih = input - currentStock
insert Inventory (qty = selisih)
```
*Bukan: `newStock = currentStock + input` ← INI SALAH*

---

### 🔹 C. Warning "!" di Aronium — Normal atau Tidak?
- Tanda `!` = ada koreksi manual (normal, bukan error)
- Tombol **FIX** mengubah **histori**, bukan hanya angka sekarang → bisa jadi minus
- **Saran:** Jangan klik FIX kecuali data benar-benar kacau

---

### 🔹 D. Smart Stock Engine — Arsitektur Anti Kacau

```
Telegram / UI
     ↓
Intent Parser
     ↓
Stock Engine (CORE)
 ├── Validator
 ├── Decision Engine
 ├── Safety Guard
 └── DB Writer
     ↓
Aronium DB (pos.db)
```

**Pembagian jenis aksi:**
- 🟢 **RESTOCK** → Purchase (Type 100), qty positif, stok bertambah
- 🔵 **SALES** → Sales (Type 200), kurangi stok
- 🟡 **INVENTORY** → Koreksi (Type 300), qty = selisih

**Safety Guard:**
```
if (abs(selisih) > 50) → minta konfirmasi
if (resultStock < 0) → peringatkan user
if (lonjakan > normal * 3) → flag ANOMALI
```

---

### 🔹 E. AI Stock Guard — 4 Fitur Utama

| Fitur | Mekanisme |
|---|---|
| **Cegah Salah Input** | Hitung selisih, validasi lonjakan, minta konfirmasi |
| **Rekomendasi Restock** | `avgDaily = penjualan 7 hari / 7`, `rekomendasi = avgDaily × 7` |
| **Deteksi Stok Minus** | `SELECT * FROM Product WHERE Stock < 0` |
| **Notifikasi Harian** | Scheduler pagi jam 07:00 — omzet, stok minus, rekomendasi |

---

### 🔹 F. Laporan Restock Plan (Excel/Google Sheets)

Tanpa integrasi supplier yang wajib. Format sheet:

**Sheet: Restock Plan**

| Produk | Stok | Min | Avg/Hari | Rekomendasi | Harga Estimasi | Total |
|---|---|---|---|---|---|---|
| Minyak | 5 | 20 | 8 | 50 | 13.000 | 650.000 |

**Sheet: Data Kosong**

| Produk | Masalah |
|---|---|
| Gula | MinStock belum diisi |
| Sabun | Tidak ada histori penjualan |

> **Catatan:** Supplier bersifat opsional (fase 2). Fase 1 cukup stok + penjualan + rekomendasi.

---

### 🔹 G. Arsitektur Bot vs AI — Pembagian Peran

```
USER (Telegram)
     ↓
Command Router
     ↓
Validation Engine ← aturan bisnis
     ↓
Confirmation Layer ← human check
     ↓
Execution Engine ← insert ke DB
     ↓
Event Bus
     ↓
AI + Analytics ← insight & rekomendasi
     ↓
Google Sheets / Report
```

**Aturan WAJIB:**
- ⚡ **BOT = ENGINE** → cepat, stabil, langsung ke DB
- 🧠 **AI = ADVISOR** → analisa, warning, insight
- ❌ AI **TIDAK BOLEH** write ke DB langsung

**Command Trigger:**

| Jenis | Contoh |
|---|---|
| 🟢 Manual Command | `/stok`, `/restock`, `/inventory`, `/laporan` |
| 🟡 Event Trigger | Stok < minimum, stok minus, produk tidak laku 7 hari |
| 🔴 Alert Trigger | Stok minus, selisih inventory besar, harga modal = 0 |
| 🔵 Schedule Trigger | Pagi (laporan harian), malam (ringkasan), mingguan |

---

### 🔹 H. Mode Operasi Bot

| Mode | Perilaku |
|---|---|
| **SAFE** | Semua aksi wajib konfirmasi + validasi aktif |
| **NORMAL** | Konfirmasi hanya untuk anomali |
| **FAST** | Langsung eksekusi (untuk owner expert) |

---

### 🔹 I. Sistem AI + Fallback (Auto-Switch)

```
Coba Groq API
    ↓ [Error/Timeout]
Retry 2x
    ↓ [Masih Error]
Coba Gemini (Fallback)
    ↓ [Error]
Rule-Based Response (tanpa AI)
```

**Fitur yang tetap jalan tanpa AI:**
- ✅ `/stok`, `/restock`, `/inventory`, `/laporan`
- ✅ Rekomendasi restock (pakai rumus)
- ✅ Deteksi stok minus (SQL)
- ✅ Generate Excel/CSV
- ✅ Notifikasi harian

---

### 🔹 J. SOP AI — Aturan & Hak Akses

**AI boleh:**
- ✅ Membaca data yang dikirim sistem (produk, stok, penjualan, pelanggan)
- ✅ Analisa, rekomendasi, rangkum
- ✅ Jawab pertanyaan natural language

**AI DILARANG:**
- ❌ Menulis ke database
- ❌ Mengubah stok
- ❌ Membuat transaksi
- ❌ Menebak data yang tidak ada
- ❌ Query database langsung

**Data yang boleh diakses AI (dikirim oleh BOT):**
- 🧾 Produk: nama, stok, harga jual, harga modal, kategori
- 💰 Penjualan: omzet, jumlah transaksi, produk laris, waktu
- 👥 Pelanggan: nama, HP (jika ada), email (jika ada), total pembelian
- 🧾 Dokumen: tipe, tanggal, total, item
- 🏪 Profil toko: nama, alamat, no HP

---

### 🔹 K. Fitur Laporan & Analisa yang Diminta

| Fitur | Mekanisme |
|---|---|
| Penjualan hari ini | Query `DocumentTypeId = 200 AND DATE = TODAY` → generate Excel |
| Detail pelanggan | `JOIN Customer c ON c.Id = d.CustomerId` |
| Belanja per orang | `JOIN DocumentItem + Product` |
| Pelanggan paling loyal | `GROUP BY c.Name ORDER BY SUM(Total) DESC LIMIT 5` |
| Export Excel | Multi-sheet (.xlsx) via bot, dikirim ke Telegram |

---

### 🔹 L. Pengaturan Sistem yang Harus Ada di SSA (Settings)

| Kategori | Setting |
|---|---|
| **Report** | `daily_report_time`, `weekly_report_day`, `auto_send_report` |
| **Notification** | `stock_alert`, `stock_minimum`, `negative_stock_alert`, `daily_summary` |
| **Stock Control** | `safe_mode`, `inventory_max_change`, `allow_negative_stock` |
| **AI** | `ai_enabled`, `fallback_mode`, `max_tokens`, `cache_enabled` |
| **Role** | `owner_id`, `kasir_id`, `restrict_profit_view` |

---

### 🔹 M. Dashboard Admin — Fitur yang Diminta

| View | Fungsi |
|---|---|
| **Home/Overview** | Status AI, status bot, omzet hari ini, stok minus |
| **Settings AI** | API Key, model, temperature, toggle auto-switch/cache |
| **Settings Bot** | Token Telegram, chat ID owner, mode SAFE/NORMAL/FAST |
| **Settings Sistem** | Jam laporan, notifikasi ON/OFF, min stok |
| **Stock Monitor** | Tabel stok semua produk dengan indikator warna |
| **AI Monitor** | Status AI, jumlah request, error count |
| **Log Aktivitas** | Log semua aksi user (waktu, user, aksi) |
| **Log Error** | Waktu + jenis error |
| **Role Management** | Owner ID, Kasir ID, hak akses |

**Fitur wajib di dashboard:**
- 🔥 Test Connection (AI, Telegram, DB)
- 🔥 Restart Service (AI, Bot)
- 🔥 Backup DB
- 🔥 Export Log ke Excel

---

## 3. Status Implementasi per Fitur

### 🟢 Sudah Terimplementasi (Sesuai Percakapan)

| Fitur | Detail |
|---|---|
| **Restock Engine** | Membuat dokumen Purchase (Type 100), stok bertambah, konfirmasi dulu |
| **Inventory Engine** | SET MODE — hitung selisih, buat Inventory Count (Type 300) |
| **Cek Stok** | `/stok [nama]` — query langsung ke pos.db |
| **Laporan Harian** | Omzet, profit, jumlah transaksi (filter DocumentTypeId = 200) |
| **AI Dual Provider** | Groq primary + Gemini fallback dengan auto-switch |
| **Role-Based Access** | Owner (full access) vs Kasir (terbatas) |
| **Anti-Hallucination** | AI tidak bisa mengarang data |
| **Konfirmasi Sebelum Eksekusi** | Tampil konfirmasi sebelum restock/inventory |
| **Safety Guard** | Validasi selisih besar, lonjakan aneh |
| **Notifikasi Stok Minus** | `/notifikasi_stok` |
| **Riwayat Restock & Inventory** | `/riwayat_restock`, `/riwayat_inventory` |
| **Analisa Bisnis (AI)** | `/analisa` — analisa natural language |
| **Mode AI Auto-Switch** | Groq → Gemini → Rule-based |
| **Cache AI** | Hemat token API |
| **Dashboard Admin (GUI)** | Dashboard, Stock Monitoring, Logs, Settings (UI sudah ada) |
| **Enkripsi API Key** | DPAPI Windows |
| **Chat Whitelist** | Hanya Chat ID yang dikonfigurasi bisa akses |
| **Natural Language Chat** | AI memahami pertanyaan bahasa Indonesia |
| **History Tracking** | Riwayat restock & inventory per produk |
| **DocumentTypeId Filter** | Revenue hanya dari Type 200 |
| **Log Aktivitas** | Logging semua event + error |

---

### 🟡 Sebagian / Partial (Ada tapi Belum Sempurna)

| Fitur | Status | Keterangan |
|---|---|---|
| **Confirmation UX (inline)** | Partial | `/confirm` & `/cancel` ada, tapi inline callback Telegram belum dipulihkan |
| **Dashboard Observability** | Partial | Status runtime ada, tapi belum ada panel detail per-message |
| **Rekomendasi Restock** | Partial | `/rekomendasi_restock` ada, tapi belum ada AI analysis kompleks |
| **Scheduler Laporan** | Partial | Low stock check & daily summary ada, tapi scheduler lanjutan belum |
| **Settings Report** | Partial | Beberapa setting ada, tapi `daily_report_time` dan `weekly_report_day` belum ada di UI |
| **Role Management** | Partial | Owner/Kasir ID bisa dikonfigurasi, tapi Role Management panel belum ada di UI |
| **AI Monitor Panel** | Partial | Status AI ada di dashboard, tapi jumlah request & error count belum ditampilkan |

---

### 🔴 Belum Diimplementasi (dari Percakapan)

| Fitur | Fase | Prioritas |
|---|---|---|
| **Google Sheets Integration** | Phase 4 | Tinggi |
| **Export Excel (.xlsx) otomatis** | Phase 4 | Tinggi |
| **OCR Struk (foto struk → data)** | Phase 4 | Sedang |
| **Background Scheduler Lanjutan** | Phase 4 | Sedang |
| **Auto Laporan (jam 07:00)** | Phase 4 | Tinggi |
| **AI Generate Query dari Natural Language** | Phase 4 | Sedang |
| **Laporan Pelanggan per Orang** | Phase 4 | Sedang |
| **Supplier Database (opsional)** | Phase 5 | Rendah |
| **Voice Note Processing** | Phase 5 | Rendah |
| **Multi-Cabang Support** | Phase 5 | Rendah |
| **Advanced Analytics & Charts** | Phase 5 | Rendah |
| **Rollback / Undo Last Action** | Backlog | Sedang |
| **Dry Run (--preview mode)** | Backlog | Rendah |
| **Anti Double Input (3 detik)** | Backlog | Rendah |
| **Audit System (deteksi kehilangan)** | Backlog | Sedang |

---

## 4. Checklist Lengkap

### ✅ Mekanisme Inti Bot

- [x] `/stok [nama]` — cek stok produk
- [x] `/restock [produk] [qty] [harga]` — tambah stok (Purchase)
- [x] `/inventory [produk] [qty]` — koreksi stok (SET mode, bukan tambah)
- [x] `/laporan` — laporan hari ini
- [x] `/analisa` — analisa bisnis via AI
- [x] `/cek_modal` — cek produk tanpa harga modal
- [x] `/rekomendasi_restock` — rekomendasi produk perlu restock
- [x] `/notifikasi_stok` — produk stok habis/minus
- [x] `/riwayat_restock [produk]` — riwayat restock
- [x] `/riwayat_inventory [produk]` — riwayat inventory
- [x] `/help` — daftar perintah
- [ ] `/laporan_pelanggan` — laporan siapa yang belanja hari ini
- [ ] `/detail [nama_pelanggan]` — belanja apa aja per pelanggan
- [ ] `/pelanggan_loyal` — pelanggan paling sering beli
- [ ] `/undo` — batalkan aksi terakhir (rollback)
- [ ] `/preview` / dry-run mode

---

### ✅ Mekanisme Stok

- [x] Restock = `DocumentTypeId = 100` (Purchase), qty positif, stok **bertambah**
- [x] Inventory = `DocumentTypeId = 300`, qty = **selisih** (bukan tambah)
- [x] Filter laporan hanya dari `DocumentTypeId = 200` (Sales)
- [x] Konfirmasi sebelum eksekusi restock/inventory
- [x] Hitung selisih inventory: `selisih = target - currentStock`
- [x] Safety guard: validasi lonjakan stok
- [ ] Anti double input (block command sama dalam 3 detik)
- [ ] Deteksi stok anomali otomatis (lonjakan > 3x normal)
- [ ] Audit stok (stok sistem vs perhitungan histori)
- [ ] Auto normalisasi stok kacau

---

### ✅ Mekanisme AI

- [x] Groq API sebagai AI primary
- [x] Gemini sebagai AI fallback
- [x] Auto-switch saat AI error/limit
- [x] Retry 2x sebelum fallback
- [x] Rule-based fallback tanpa AI
- [x] Cache hasil AI (hemat token)
- [x] Anti-hallucination rules (tidak boleh mengarang data)
- [x] AI hanya READ data, tidak bisa WRITE ke DB
- [x] AI hanya menerima data yang dikirim sistem (bukan query langsung)
- [ ] AI monitoring panel (jumlah request, error count, last response time)
- [ ] Auto-recovery AI setelah 5 menit fallback

---

### ✅ Dashboard Admin (GUI)

- [x] Dashboard overview (status AI, bot, DB, omzet, stok minus)
- [x] Stock Monitoring (tabel stok semua produk)
- [x] Activity Logs (log aktivitas dengan filter)
- [x] Settings (AI, Bot, Database)
- [x] Bot Control Panel (Start/Stop/Restart)
- [x] Test Connection (AI, Telegram, DB)
- [ ] AI Monitor Panel (jumlah request, error, response time)
- [ ] Role Management Panel (UI untuk atur Owner/Kasir ID)
- [ ] Report Settings Panel (jadwal laporan, format)
- [ ] Notification Settings Panel (stok minimum, toggle alert)
- [ ] Backup DB dari UI
- [ ] Export Log ke Excel dari UI
- [ ] Restart Service dari UI

---

### ✅ Pengaturan / Settings

- [x] AI API Key (Groq + Gemini)
- [x] AI temperature, max tokens
- [x] Telegram Bot Token
- [x] Owner Chat IDs
- [x] Mode bot (SAFE / NORMAL / FAST)
- [x] Database path (pos.db)
- [x] AI auto-switch toggle
- [x] AI cache toggle
- [ ] `daily_report_time` (jam laporan harian)
- [ ] `weekly_report_day` (hari laporan mingguan)
- [ ] `auto_send_report` toggle
- [ ] `stock_minimum` (threshold stok minimum)
- [ ] `negative_stock_alert` toggle
- [ ] `allow_negative_stock` toggle
- [ ] `inventory_max_change` (batas maksimum perubahan inventory)

---

### ✅ Sistem Laporan & Export

- [x] Laporan harian via Telegram (omzet, profit, transaksi)
- [x] Log aktivitas di UI
- [ ] Auto laporan jam 07:00 pagi
- [ ] Export Excel (.xlsx) dari bot
- [ ] Google Sheets sync
- [ ] Laporan penjualan per pelanggan
- [ ] Laporan produk terlaris (via bot, bukan hanya AI analisa)
- [ ] Weekly report otomatis
- [ ] Monthly report otomatis

---

### ✅ Keamanan & Akses

- [x] API key terenkripsi (DPAPI Windows)
- [x] Chat ID whitelist
- [x] Role-based access (Owner/Kasir)
- [x] AI tidak bisa akses DB langsung
- [ ] Role management via UI (bukan hanya config.json)
- [ ] Log siapa saja yang akses bot (audit trail)

---

## 5. Fitur Belum Diimplementasi

### 🚨 Prioritas Tinggi (Phase 4)

| # | Fitur | Keterangan |
|---|---|---|
| 1 | **Auto Laporan Harian (07:00)** | Scheduler mengirim laporan otomatis setiap pagi |
| 2 | **Export Excel (.xlsx) dari Bot** | User minta laporan → bot generate file → kirim via Telegram |
| 3 | **Google Sheets Integration** | Sync data ke Google Sheets otomatis |
| 4 | **Laporan Pelanggan** | Siapa yang belanja hari ini, detail per pelanggan |
| 5 | **Notification Settings di UI** | Panel setting untuk threshold stok, jadwal laporan |

### ⚠️ Prioritas Sedang (Backlog)

| # | Fitur | Keterangan |
|---|---|---|
| 6 | **OCR Struk** | Upload foto struk → ekstrak data otomatis (Tesseract) |
| 7 | **Rollback / Undo** | Batalkan aksi terakhir |
| 8 | **AI Monitoring Panel** | Tampilkan penggunaan AI (request, error, response time) |
| 9 | **Audit Stok** | Deteksi selisih antara stok sistem dan histori |
| 10 | **Anti Double Input** | Block command yang sama dalam 3 detik |

### 🔵 Prioritas Rendah (Phase 5)

| # | Fitur | Keterangan |
|---|---|---|
| 11 | **Supplier Database** | Integrasi harga supplier untuk rekomendasi restock |
| 12 | **Voice Note** | Proses pesan suara dari user |
| 13 | **Multi-Cabang** | Support beberapa lokasi toko |
| 14 | **Advanced Analytics** | Chart & visualisasi data penjualan |
| 15 | **Dry Run Mode** | Preview aksi sebelum eksekusi (`--preview`) |

---

## 6. Roadmap Selanjutnya

### Phase 4 (Coming Soon)
```
[ ] OCR struk dengan Tesseract
[ ] Google Sheets integration
[ ] Background scheduler lanjutan
[ ] Auto laporan harian jam 07:00
[ ] Export Excel dari bot
[ ] Laporan pelanggan detail
[ ] Notification settings di UI
[ ] AI monitoring panel
[ ] Installer + auto-start Windows
```

### Phase 5 (Future)
```
[ ] Voice note support
[ ] Supplier database
[ ] Multi-cabang support
[ ] Advanced analytics & charts
[ ] WhatsApp integration (opsional)
[ ] Dry run / preview mode
[ ] Rollback / undo aksi
```

---

## 📌 Catatan Penting dari Percakapan

> **Prinsip #1:** Bot adalah MESIN utama. AI adalah OTAK/ADVISOR. AI tidak pernah menulis ke DB.

> **Prinsip #2:** Restock ≠ Inventory. Restock untuk tambah stok (barang datang). Inventory untuk koreksi selisih saja.

> **Prinsip #3:** Sistem yang baik bukan yang paling pintar, tapi yang **tidak pernah mati**. Semua fitur inti harus jalan walau AI limit.

> **Prinsip #4:** Supplier tidak wajib di fase awal. Fokus ke stok + penjualan + rekomendasi dulu, supplier bisa masuk fase 2.

> **Prinsip #5:** Jangan klik tombol FIX di Aronium kecuali data benar-benar kacau. Tanda "!" bukan error, itu indikator koreksi manual.

---

*Dokumen ini dirapikan dari catatan percakapan lama dan dicocokkan dengan kode proyek aktual.*  
*Referensi: `APLIKASI_OVERVIEW.md`, `IMPLEMENTATION_STATUS.md`, `AGENT.md`, `FITUR_BELUM_ADA_BOT_TELEGRAM.md`*
