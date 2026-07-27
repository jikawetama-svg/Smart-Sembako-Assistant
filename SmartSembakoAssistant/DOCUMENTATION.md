# 📖 Master Dokumentasi Teknis & Panduan Penggunaan Smart Sembako Assistant

Dokumen ini merupakan sumber kebenaran tunggal (*Single Source of Truth*) yang mengkonsolidasikan seluruh aspek teknis, operasional, arsitektur, konfigurasi settings, dan panduan fitur **Smart Sembako Assistant (SSA)**.

---

## 📋 Daftar Isi

1. [Arsitektur Sistem & Komponen Utamas](#1-arsitektur-sistem--komponen-utama)
2. [Panduan Setup & Settings Lanjutan](#2-panduan-setup--settings-lanjutan)
3. [Engine Operasional Toko](#3-engine-operasional-toko)
   - [A. Quick Inventory Engine (`/inventory`)](#a-quick-inventory-engine-inventory)
   - [B. Quick Restock Engine (`/restock`)](#b-quick-restock-engine-restock)
   - [C. OCR Faktur & Struk Supplier (`/struk`)](#c-ocr-faktur--struk-supplier-struk)
4. [WhatsApp Integration (Cloud API & Baileys Sidecar)](#4-whatsapp-integration-cloud-api--baileys-sidecar)
5. [Sistem Observabilitas & Outbound Guard](#5-sistem-observabilitas--outbound-guard)
6. [Command Reference AI Prompting](#6-command-reference-ai-prompting)

---

## 1. Arsitektur Sistem & Komponen Utama

Smart Sembako Assistant dibangun menggunakan pendekatan **Hybrid Distributed System**:

### A. Aplikasi Desktop C# WPF (.NET 8.0)
- **POS Database (`pos.db`)**: Berinteraksi langsung dengan SQLite milik Aronium POS.
- **Config Service**: Mengelola `config.json` dengan enkripsi DPAPI untuk API Keys.
- **Sync Service**: Melakukan pengunggahan (*one-way push*) delta stok dan transaksi ke Supabase Cloud setiap 15 menit.
- **Automation & Scheduler Engine**: Mengirimkan notifikasi stok kritis dan laporan harian ke WhatsApp/Telegram.

### B. Cloud Database (Supabase)
- **Tabel `products_sync`**: Menyimpan snapshot katalog produk, barcoding, unit, harga jual, dan stok saat ini.
- **Tabel `transactions_sync`**: Menyimpan data penjualan harian untuk analisis LLM.

### C. Python Cloud Bot (`bot_runtime/` FastAPI)
- Menerima webhook dari Telegram (`/webhook/telegram`).
- Bertindak sebagai *Read-Only Consumer* yang membaca data stok dari Supabase untuk menjawab pertanyaan pengguna secara 24/7 tanpa tergantung pada status hidup/mati PC kasir toko.

---

## 2. Panduan Setup & Settings Lanjutan

### A. AI Engine Settings (Groq & Gemini)
- **Groq API Key (Utama)**: Digunakan untuk model LLM berkecepatan tinggi (`llama-3.3-70b-versatile`). Set `Temperature` pada `0.3` untuk akurasi operasional.
- **Gemini Fallback (Cadangan)**: Jika kuota Groq habis/error, sistem otomatis berpindah ke Gemini (`gemini-2.5-flash`).

### B. Telegram Bot Setup
1. Buat bot baru via `@BotFather` di Telegram dan dapatkan **Bot Token**.
2. Isi `Telegram.BotToken` pada Settings.
3. Daftarkan `OwnerChatIds` (Chat ID Telegram pemilik toko) agar mendapat hak akses perintah administrator.

### C. Supabase Cloud Sync Setup
Buka tab **Supabase** pada Settings di Aplikasi C#:
- `Enabled`: `true`
- `Url`: `https://your-project.supabase.co`
- `ApiKey`: Gunakan `service_role key` (JWT Token) agar aplikasi C# Desktop dapat meng-upsert data stok ke cloud database.

---

## 3. Engine Operasional Toko

### A. Quick Inventory Engine (`/inventory`)

Perintah `/inventory` digunakan untuk melakukan penyesuaian stok akhir (*stock opname*) agar sesuai dengan jumlah fisik di toko.

#### Rule Utama:
1. **`Stock.Quantity`** di database Aronium SQLite adalah sumber kebenaran stok saat ini.
2. Perintah `/inventory <nama_produk> <stok_target>` menerima **stok akhir yang diinginkan**, bukan jumlah tambahan.

#### Alur Transaksi:
```text
User: /inventory Sasa 1000 107
Bot : KONFIRMASI INVENTORY
      Produk : Sasa 1000
      Dari   : 105 Pcs -> Ke: 107 Pcs (Selisih: +2 Pcs)
User: /confirm
Bot : INVENTORY SELESAI
      Dokumen: 26-300-000098 | Stok: 105 -> 107 Pcs
```

#### Struktur Data di Database Aronium:
- **Document**: `DocumentTypeId = 3` (Inventory Count), format nomor `YY-300-NNNNNN`.
- **DocumentItem**: `Quantity = target stock` (misal 107), `ExpectedQuantity = stok sebelum` (misal 105).
- **Stock.Quantity**: Di-update langsung ke `targetStock`.

---

### B. Quick Restock Engine (`/restock`)

Perintah `/restock` digunakan untuk mencatat pembelian stok barang masuk dari supplier.

#### Alur Transaksi:
1. User kirim `/restock <nama_produk> <jumlah_tambah>`.
2. Sistem membuat dokumen **Purchase Document** (`DocumentTypeId = 2`).
3. `Stock.Quantity` bertambah sebesar `jumlah_tambah`.

---

### C. OCR Faktur & Struk Supplier (`/struk`)

Fitur OCR memungkinkan input otomatis barang masuk hanya dengan mengunggah foto struk/faktur.

#### Cara Kerja:
1. Kirim foto struk ke bot Telegram dengan caption `/struk`.
2. Engine OCR Tesseract (menggunakan `tessdata/ind.traineddata`) me-read teks faktur.
3. Bot mencocokkan nama barang supplier dengan nama barang di Aronium via `OcrProductMapping`.
4. Pengguna mengonfirmasi item yang terdeteksi, dan sistem otomatis membuat dokumen **Purchase** di Aronium POS.

---

## 4. WhatsApp Integration (Cloud API & Baileys Sidecar)

Aplikasi mendukung 2 jalur WhatsApp yang dapat diaktifkan via `WhatsApp.Mode`:

### A. WhatsApp Cloud API (Resmi Meta)
- **Port Webhook Lokal**: Default `8090` (`http://localhost:8090/whatsapp/webhook`).
- **Cloudflare Tunnel**: Untuk menghubungkan Meta ke PC lokal, gunakan `cloudflared`:
  ```bash
  cloudflared tunnel --url http://localhost:8090 --http-host-header localhost:8090
  ```
- **App Secret**: Wajib diisi pada Settings untuk validasi signature `X-Hub-Signature-256`.

### B. WhatsApp Baileys Sidecar (Lokal Node.js)
- **Node Sidecar**: Berjalan di port `8091` (`Integrations/BaileysSidecar/index.js`).
- **Pairing Code**: Jalankan `Start Pairing` pada Settings untuk mendapatkan 8-digit kode pairing tanpa perlu scan QR.

---

## 5. Sistem Observabilitas & Outbound Guard

- **Outbound Guard**: Mencegah spam dan pengiriman ulang pesan yang gagal secara tak terbatas (*exponential backoff*).
- **Manual Outbox Clear**: Tombol `Clear Pending WA Outbox` di Dashboard View untuk membatalkan semua antrean pesan keluar yang tersangkut.
- **Runtime Diagnostics**: Menampilkan status aktif instance desktop, machine name, serta kesehatan koneksi sidecar.

---

## 6. Command Reference AI Prompting

Gunakan perintah khusus ini di lingkungan pengembangan untuk mengarahkan AI:

| Command | Fungsi | Contoh Penggunaan |
| :--- | :--- | :--- |
| **`/plan`** | Merancang fitur baru & roadmap arsitektur | `/plan buat roadmap fitur supplier database` |
| **`/code`** | Menghasilkan kode modular production-ready | `/code buatkan SupplierService modular` |
| **`/debug`** | Menganalisis error & memberikan snippet perbaikan | `/debug kenapa scheduler tidak me-load config` |
| **`/fast`** | Menjawab pertanyaan teknis secara ringkas | `/fast ringkas perubahan di SyncService.cs` |

---
*Dokumentasi ini diselaraskan dengan Smart Sembako Assistant v6.2.1.*
