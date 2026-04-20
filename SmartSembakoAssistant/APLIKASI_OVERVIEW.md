# 📖 SMART SEMBAKO ASSISTANT - Dokumentasi Lengkap

**Versi:** 4.0  
**Platform:** Windows Desktop (WPF .NET 8)  
**Bahasa:** C# 12  
**Tanggal:** 10 April 2026

---

## 📋 DAFTAR ISI

1. [Apa itu Smart Sembako Assistant?](#1-apa-itu-smart-sembako-assistant)
2. [Fitur Utama](#2-fitur-utama)
3. [Arsitektur Sistem](#3-arsitektur-sistem)
4. [Telegram Bot](#4-telegram-bot)
5. [AI Integration](#5-ai-integration)
6. [Database & Aronium](#6-database--aronium)
7. [API Keys & Konfigurasi](#7-api-keys--konfigurasi)
8. [Dashboard Admin](#8-dashboard-admin)
9. [Cara Kerja Fitur](#9-cara-kerja-fitur)
10. [Troubleshooting](#10-troubleshooting)

---

## 1. APA ITU SMART SEMBAKO ASSISTANT?

Smart Sembako Assistant adalah **aplikasi desktop AI-powered** untuk mengelola toko sembako secara cerdas melalui **Telegram Bot**. Aplikasi ini terintegrasi langsung dengan **Aronium POS** (Point of Sale) untuk membaca data stok, transaksi, dan membuat dokumen restock/inventory.

### 🎯 Tujuan Aplikasi
- ✅ **Monitor stok** toko secara real-time via Telegram
- ✅ **AI Assistant** yang bisa diajak ngobrol natural (bahasa Indonesia)
- ✅ **Restock otomatis** - buat dokumen pembelian di Aronium
- ✅ **Inventory correction** - koreksi stok via Telegram
- ✅ **Laporan harian** - omzet, profit, transaksi otomatis
- ✅ **Analisa bisnis** - AI bantu analisa penjualan

### 👥 Target Pengguna
- **Owner/Pemilik Toko**: Full access, bisa restock, inventory, lihat profit
- **Kasir**: Akses terbatas, hanya cek stok dan transaksi

---

## 2. FITUR UTAMA

### 🤖 Telegram Bot Features

| Command | Deskripsi | Akses |
|---------|-----------|-------|
| `/stok [nama]` | Cek stok produk | Owner & Kasir |
| `/laporan` | Laporan hari ini (omzet, profit, transaksi) | Owner & Kasir |
| `/restock [produk] [qty] [harga]` | Buat dokumen pembelian | Owner |
| `/inventory [produk] [qty]` | Koreksi stok (SET mode) | Owner |
| `/analisa` | Analisa bisnis lengkap | Owner |
| `/cek_modal` | Cek produk tanpa modal | Owner |
| `/rekomendasi_restock` | Rekomendasi produk perlu restock | Owner |
| `/notifikasi_stok` | Cek produk stok habis/minus | Owner |
| `/riwayat_restock` | Riwayat restock produk | Owner |
| `/riwayat_inventory` | Riwayat inventory produk | Owner |
| `/help` | Bantuan command | Owner & Kasir |

### 💬 Natural Language Chat
User bisa chat biasa seperti:
- "Stok beras berapa?"
- "Gimana penjualan hari ini?"
- "Produk apa yang paling laku?"
- "Restock minyak 50 harga 14000"

AI akan memahami intent dan merespon dengan data dari database.

### 📊 Dashboard Admin (Desktop UI)

| View | Fungsi |
|------|--------|
| **Dashboard** | Overview status bot, AI, database, revenue, profit, critical stock |
| **Stock Monitoring** | Tabel stok semua produk dengan filter & quick stats |
| **Activity Logs** | Log aktivitas sistem dengan filter level & category |
| **Settings** | Konfigurasi AI, Bot, Database, Notifikasi |

---

## 3. ARSITEKTUR SISTEM

### 🏗️ Komponen Utama

```
┌─────────────────────────────────────────────────────────┐
│                   Telegram Bot API                       │
│                    (Long Polling)                        │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│              TelegramBotService (C#)                     │
│  • Handle commands                                       │
│  • Natural language processing                           │
│  • Role-based access control                             │
└────────────────────────┬────────────────────────────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
┌────────▼────┐  ┌───────▼──────┐  ┌────▼────────┐
│ GroqService │  │ PosDbService │  │DatabaseService│
│ (AI/Gemini) │  │ (Aronium DB) │  │ (memory.db) │
└─────────────┘  └──────────────┘  └─────────────┘
```

### 📁 Struktur Folder

```
SmartSembakoAssistant/
├── Services/
│   ├── ConfigService.cs        ← Konfigurasi & enkripsi API keys
│   ├── DatabaseService.cs      ← SQLite CRUD (memory.db)
│   ├── LoggingService.cs       ← Logging system
│   ├── PosDbService.cs         ← Aronium integration
│   ├── TelegramBotService.cs   ← Telegram bot handler
│   ├── GroqService.cs          ← AI integration (Groq + Gemini)
│   └── BotController.cs        ← Bot lifecycle management
├── Views/
│   ├── DashboardView.xaml      ← Dashboard overview
│   ├── StockMonitoringView.xaml ← Stock table
│   ├── LogsView.xaml           ← Activity logs
│   └── SettingsView.xaml       ← Settings page
├── Models/
│   ├── AppConfig.cs            ← Configuration models
│   ├── Product.cs              ← Product & transaction models
│   └── Memory.cs               ← Conversation & log models
└── data/
    ├── memory.db               ← Local SQLite database
    └── logs/                   ← Log files
```

---

## 4. TELEGRAM BOT

### 🔧 Cara Kerja Bot

1. **Long Polling Mode** - Bot terus-menerus polling ke Telegram API untuk cek pesan baru
2. **Command Parsing** - Pesan dianalisis: apakah command (`/stok`, `/restock`) atau chat natural
3. **Role Check** - Cek apakah user punya akses (Owner/Kasir)
4. **Processing** - Eksekusi perintah (query DB, panggil AI, dll)
5. **Response** - Kirim balasan ke user via Telegram

### 🔐 Keamanan Bot

- **Chat ID Whitelist** - Hanya chat ID yang dikonfigurasi yang bisa akses bot
- **Role-Based Access** - Owner punya akses penuh, Kasir terbatas
- **API Key Enkripsi** - Telegram bot token dienkripsi dengan DPAPI (Windows)

### ⚙️ Konfigurasi Bot

Di **Settings** → **Telegram Bot**:
- **Bot Token** - Token dari @BotFather
- **Owner Chat IDs** - Chat ID pemilik toko (bisa lebih dari 1, pisah koma)
- **Mode** - SAFE (butuh konfirmasi) / NORMAL / FAST

---

## 5. AI INTEGRATION

### 🧠 AI Provider (Dual Provider System)

Aplikasi menggunakan **2 AI provider** dengan automatic fallback:

| Provider | Model | Penggunaan |
|----------|-------|------------|
| **Groq** (Primary) | llama-3.1-70b-versatile | Natural language, analisa, rekomendasi |
| **Gemini** (Fallback) | gemini-2.0-flash | Backup saat Groq error/limit |

### 🔄 Auto-Switch Mechanism

```
User kirim pesan
    ↓
Coba Groq API
    ↓ [Error/Timeout]
Retry 2x
    ↓ [Masih Error]
Coba Gemini API (Fallback)
    ↓ [Error/Timeout]
Rule-Based Response (tanpa AI)
```

###  AI Behavior Settings

| Setting | Fungsi | Default |
|---------|--------|---------|
| **Auto-switch** | Otomatis switch ke Fallback saat error | ✅ ON |
| **Cache** | Cache response AI (hemat API limit) | ✅ ON |
| **Retry Count** | Jumlah retry sebelum fallback | 2 |
| **Auto Recovery** | Coba Groq lagi setelah X menit | 5 menit |
| **Temperature** | Kreativitas AI (0 = deterministic, 1 = kreatif) | 0.7 |
| **Max Tokens** | Max panjang response AI | 1000 |

###  AI Anti-Hallucination

AI diprogram dengan **strict rules** untuk tidak mengarang data:
- ❌ Tidak boleh mengarang angka stok
- ❌ Tidak boleh mengarang riwayat transaksi
- ❌ Harus berdasarkan data dari database
- ✅ Jika data tidak ada, bilang "tidak ada data"

---

## 6. DATABASE & ARONIUM

### 📊 Database yang Digunakan

| Database | File | Fungsi |
|----------|------|--------|
| **Aronium POS** | `pos.db` | READ-ONLY untuk data produk, stok, transaksi |
| **Memory DB** | `data/memory.db` | Local SQLite untuk conversation, logs, config |

### 🔒 Aronium Integration

**PENTING:** Aplikasi hanya **READ-ONLY** untuk data Aronium, kecuali untuk membuat dokumen:

| Operasi | Access | Keterangan |
|---------|--------|------------|
| Baca produk/stok | ✅ READ | Query tabel Product, Stock |
| Baca transaksi | ✅ READ | Query tabel Document (Type 200 = Sales) |
| Buat dokumen Purchase | ✅ WRITE | Document Type 100 (Restock) |
| Buat dokumen Inventory | ✅ WRITE | Document Type 300 (Koreksi stok) |

### 📦 Restock Engine

Saat user command `/restock`:
1. Cari produk di database
2. Generate nomor dokumen berikutnya (YY-100-NNNNNN)
3. Insert ke tabel Document (Type 100 = Purchase)
4. Insert ke tabel DocumentItem (produk & qty)
5. Update tabel Stock (tambah stok)
6. Dokumen muncul di Aronium

### 📋 Inventory Engine

Saat user command `/inventory`:
1. **SET MODE** - Qty adalah TARGET stok akhir (bukan tambah/kurang)
2. Hitung selisih: `selisih = target - currentStock`
3. Buat dokumen Inventory Count (Type 300)
4. Update stok di tabel Stock
5. Dokumen muncul di Aronium

**Contoh:**
- Stok sekarang: 20
- Command: `/inventory gula 21`
- Selisih: +1
- Stok akhir: 21 ✅

---

## 7. API KEYS & KONFIGURASI

### 🔑 API Keys yang Diperlukan

| API | Fungsi | Dapatkan dari |
|-----|--------|---------------|
| **Groq API Key** | AI primary | https://console.groq.com |
| **Gemini API Key** | AI fallback | https://makersuite.google.com |
| **Telegram Bot Token** | Telegram bot | @BotFather di Telegram |

### 🔐 Enkripsi API Keys

Semua API key dienkripsi otomatis dengan **Windows DPAPI**:
- Key dienkripsi saat disimpan ke `config.json`
- Key didekripsi saat dimuat aplikasi
- Prefix `ENC:` menandai value terenkripsi
- Hanya bisa didekripsi di komputer yang sama

### ⚙️ Cara Setting API Keys

1. Buka aplikasi → **Settings**
2. Masukkan API key di masing-masing section:
   - **AI PRIMARY (Groq)** → Paste Groq API key
   - **AI FALLBACK (Gemini)** → Paste Gemini API key
   - **Telegram Bot** → Paste Bot Token dari @BotFather
3. Klik **🧪 Test** untuk test koneksi
4. Klik **💾 Save** untuk simpan

### 📂 File Konfigurasi

| File | Lokasi | Fungsi |
|------|--------|--------|
| `config.json` | Root folder | Konfigurasi utama (terenkripsi) |
| `config.template.json` | Root folder | Template konfigurasi |
| `data/memory.db` | Data folder | Local database |

---

## 8. DASHBOARD ADMIN

### 🖥️ Fitur Dashboard

| View | Deskripsi |
|------|-----------|
| **📊 Dashboard** | Overview status sistem, revenue, profit, critical stock |
| **📦 Stock Monitoring** | Tabel stok dengan quick stats (Aman/Rendah/Habis/Minus) |
| **📜 Activity Logs** | Log aktivitas dengan filter level & category |
| **⚙️ Settings** | Konfigurasi AI, Bot, Database |

### 🎮 Bot Control Panel

Di sidebar kiri:
- **▶ Start** - Jalankan bot
- **⏹ Stop** - Hentikan bot
- **↻ Restart** - Restart bot
- **Status indicator** - 🟢 Aktif / 🔴 Stop / ️ Error
- **Uptime** - Durasi bot running
- **Auto start on boot** - Auto-start saat aplikasi dibuka

### ⚡ Quick Actions

Di sidebar bawah:
- **💰 Omzet** - Lihat omzet hari ini
- **⚠️ Stok Minus** - Cek produk stok minus
- **📦 Restock** - Rekomendasi restock
- **🧪 Test Conn** - Test semua koneksi

---

## 9. CARA KERJA FITUR

### 📦 Restock - Step by Step

1. User ketik: `/restock gula 50 14000`
2. Bot parse: produk="gula", qty=50, harga=14000
3. Cari produk di database (fuzzy match)
4. Tampilkan konfirmasi:
   ```
   📦 KONFIRMASI RESTOCK
   Produk: Gula Pasir
   Qty: 50 Pcs
   Harga: Rp 14.000/pcs
   Total: Rp 700.000
   
   Lanjutkan? [✅ YA] [❌ BATAL]
   ```
5. User klik ✅ YA
6. Buat dokumen Purchase di Aronium
7. Stok bertambah otomatis
8. Response: "✅ Restock berhasil! Dokumen: 26-100-000042"

### 📋 Inventory - Step by Step

1. User ketik: `/inventory gula 21`
2. Bot parse: produk="gula", target=21
3. Cek stok sekarang: 20
4. Hitung selisih: 21 - 20 = +1
5. Tampilkan konfirmasi:
   ```
   📦 KONFIRMASI SET STOK
   Produk: Gula Pasir
   Stok Sekarang: 20
   Stok Target: 21
   Selisih: +1
   
   Lanjutkan? [✅ YA] [❌ BATAL]
   ```
6. User klik ✅ YA
7. Buat dokumen Inventory Count di Aronium
8. Stok di-set ke 21
9. Response: "✅ Inventory berhasil! Stok Baru: 21"

### 🤖 AI Chat - Step by Step

1. User ketik: "Stok beras berapa?"
2. Bot kirim ke Groq API dengan context:
   - System prompt (anti-hallucination rules)
   - Conversation history (8 pesan terakhir)
   - Data stok dari database
3. Groq response: "Stok beras saat ini adalah 150 kg."
4. Bot kirim ke user
5. Simpan conversation ke memory.db

### 📊 Laporan Harian - Otomatis

Setiap hari jam 07:00:
1. Scheduler trigger
2. Query data kemarin:
   - Revenue (Document Type 200)
   - Profit (dari margin produk)
   - Jumlah transaksi
   - Stok minus & rendah
3. Format pesan laporan
4. Kirim ke semua Owner chat IDs
5. Contoh:
   ```
   📊 LAPORAN HARIAN - 10/04/2026
   
   💰 Omzet Kemarin: Rp 1.250.000
   📈 Profit: Rp 125.000
   🧾 Transaksi: 25 nota
   
   🚨 Stok Minus: 3 produk
   ⚠️ Stok Rendah: 12 produk
   ```

---

## 10. TROUBLESHOOTING

### ❌ Bot Tidak Jalan

**Gejala:** Klik Start tapi status tetap "Stopped"

**Solusi:**
1. Cek **Bot Token** di Settings → Telegram Bot
2. Pastikan token valid dari @BotFather
3. Klik **🧪 Test** untuk test koneksi
4. Cek log di **Activity Logs** untuk error detail
5. Pastikan koneksi internet aktif

### ❌ AI Tidak Response

**Gejala:** Bot balas tapi AI error atau tidak ada response

**Solusi:**
1. Cek **Groq API Key** di Settings → AI PRIMARY
2. Pastikan API key valid (dapat dari console.groq.com)
3. Cek apakah limit API habis
4. Enable **AI Fallback (Gemini)** sebagai backup
5. Test koneksi dengan klik **🧪 Test AI**

### ❌ Stok Tidak Update di Aronium

**Gejala:** Restock/inventory berhasil tapi stok tidak berubah

**Solusi:**
1. Restart aplikasi Aronium (refresh database)
2. Cek dokumen di Aronium → Documents → Purchase/Inventory
3. Pastikan dokumen berhasil dibuat (cek Activity Logs)
4. Cek database path di Settings → Database

### ❌ Error "Database Not Found"

**Gejala:** Dashboard menunjukkan "Database: Not Connected"

**Solusi:**
1. Buka Settings → Database
2. Klik **Browse** dan cari file `pos.db`
3. Lokasi default: `C:\Users\[USER]\AppData\Local\Aronium\Data\pos.db`
4. Atau centang **Auto-detect**
5. Klik **Save**

### ❌ API Key Tidak Tersimpan

**Gejala:** Sudah save tapi API key hilang saat restart

**Solusi:**
1. Pastikan jalankan aplikasi sebagai **Administrator**
2. Cek apakah file `config.json` writable
3. DPAPI hanya work di Windows (tidak support cross-machine)
4. Jika pindah komputer, harus re-enter API keys

### 📞 Cara Lihat Log Error

1. Buka **Activity Logs** di aplikasi
2. Filter Level = **Error**
3. Cari pesan error terbaru
4. Atau cek file log di folder `data/logs/`

---

## 📞 SUPPORT & RESOURCES

### 🔗 Links Penting

| Resource | URL |
|----------|-----|
| Groq Console | https://console.groq.com |
| Gemini AI Studio | https://makersuite.google.com |
| Telegram BotFather | @BotFather di Telegram |
| Aronium Website | https://aronium.com |

### 📧 Kontak Developer

Untuk bug report atau fitur request, silakan hubungi developer.

---

**Last Updated:** 10 April 2026  
**Version:** 4.0  
**Status:** Production Ready ✅

---

**Happy Managing! 🏪**
