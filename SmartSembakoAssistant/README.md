# 🏪 Smart Sembako Assistant (SSA)

**Smart Sembako Assistant** adalah aplikasi asisten POS (Point of Sale) & kecerdasan buatan (AI) hybrid untuk toko sembako dan retail. Aplikasi ini mengintegrasikan kasir desktop **Aronium POS** (SQLite), **Supabase Cloud Database**, **Telegram Bot**, dan **WhatsApp Business (Cloud API / Baileys Sidecar)**.

---

## 🌟 Fitur Utama

- ⚡ **Offline-First & Cloud Sync**: Data transaksi dan stok dari kasir lokal Aronium POS disinkronisasikan secara otomatis (*one-way push*) ke Supabase Cloud.
- 🤖 **Multi-Channel AI Bot**: Terhubung ke Telegram dan WhatsApp untuk menjawab kueri stok, laporan omset, dan analisis penjualan secara real-time via LLM (Groq Llama 3.3 70B & Gemini 2.5 Flash).
- 📦 **Quick Inventory & Restock Engine**: Koreksi stok fisik (`/inventory`) dan penambahan stok pembelian (`/restock`) langsung via chat Telegram/WhatsApp dengan pembuatan dokumen otomatis di Aronium POS.
- 🧾 **OCR Faktur Pembelian**: Pindai foto struk/faktur supplier (`/struk`) untuk auto-input pembelian produk ke sistem.
- 💬 **WhatsApp Dual Transport**: Mendukung WhatsApp Cloud API resmi Meta dan WhatsApp Baileys sidecar lokal tanpa biaya API.

---

## 🏗️ Arsitektur Sistem

```
┌────────────────────────────────────────────────────────┐
│               Aplikasi C# Desktop POS                  │
│  - Aronium POS Database (pos.db SQLite)                │
│  - Baileys Node.js Sidecar (WhatsApp Lokal)            │
│  - Cloudflare Tunnel Client                            │
└───────────────────────────┬────────────────────────────┘
                            │
                            │ (1-Way Delta Push / UPSERT)
                            ▼
┌────────────────────────────────────────────────────────┐
│                Supabase Cloud Database                │
│  - products_sync (Katalog & Stok Toko)                 │
│  - transactions_sync (Riwayat Penjualan)              │
└───────────────────────────▲────────────────────────────┘
                            │
                            │ (Read-Only Consumer / SELECT)
                            │
┌───────────────────────────┴────────────────────────────┐
│             Python Cloud Bot (Render.com)              │
│  - FastAPI + MasterAgent LLM Engine                    │
│  - Health Check Endpoint (HTTP 200 OK)                 │
└───────────────────────────▲────────────────────────────┘
                            │
                            │ (Real-Time Webhook)
                            ▼
┌────────────────────────────────────────────────────────┐
│             Pengguna / Owner / Kasir Toko              │
│                 (Telegram & WhatsApp)                  │
└────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start (Panduan Cepat)

### 1. Prasyarat Sistem
- **Windows 10/11** dengan **.NET 8.0 SDK**
- **Aronium POS** terinstall dengan database `pos.db`
- **Node.js 18+** (jika menggunakan fitur WhatsApp Baileys)

### 2. Menjalankan Aplikasi C# Desktop
```powershell
# Restore & Build Project
dotnet build SmartSembakoAssistant.sln --configuration Release

# Menjalankan Aplikasi
dotnet run --project SmartSembakoAssistant\SmartSembakoAssistant.csproj
```

### 3. Konfigurasi Awal (Wizard Setup)
Saat pertama kali dijalankan, wizard konfigurasi akan membimbing Anda untuk mengisi:
1. **Groq API Key**: `gsk_...` (AI LLM Utama)
2. **Telegram Bot Token**: `123456789:ABC...` (Dari @BotFather)
3. **Supabase Credentials**: URL & Service Role Key (Untuk Cloud Sync)

---

## 📚 Dokumentasi Lengkap

Untuk panduan mendalam dan referensi teknis, silakan merujuk ke dokumen berikut:

- 📖 **[DOCUMENTATION.md](DOCUMENTATION.md)**: Panduan lengkap arsitektur sistem, konfigurasi settings, engine inventory/restock, OCR struk, dan WhatsApp Cloud API.
- 🚀 **[DEPLOYMENT.md](DEPLOYMENT.md)**: Panduan deployment Python Cloud Bot ke Render.com, HuggingFace Spaces, VPS, serta setup Supabase Cloud Database.
- 📜 **[changelog.json](changelog.json)**: Riwayat pembaruan dan rilis versi aplikasi.

---

## 📁 Struktur Direktori Repository

```text
SmartSembakoAssistant/
├── SmartSembakoAssistant/     # Project Utama C# WPF Desktop (.NET 8.0)
│   ├── Controls/              # UI Custom Components
│   ├── Helpers/               # Runtime Paths, Encryption, & Utilities
│   ├── Integrations/          # BaileysSidecar (Node.js WhatsApp)
│   ├── Models/                # Data Models & Config Data Structures
│   ├── Services/              # Core Services (SyncService, GroqService, PosDbService, dll)
│   ├── Views/                 # WPF Views (DashboardView, SettingsView, dll)
│   ├── config.json            # Active Runtime Configuration
│   └── config.template.json   # Template Configuration
├── bot_runtime/               # Python FastAPI Cloud Bot (Render / HF Space)
│   ├── main.py                # Webhook & Root Health Check Handlers
│   ├── config.py              # Environment Variables Loader
│   ├── Dockerfile             # Container configuration (Dynamic PORT)
│   └── requirements.txt       # Python Dependencies
├── data/                      # Schema SQL Supabase & Mock Data
├── DOCUMENTATION.md           # Unified Master Technical & User Guide
├── DEPLOYMENT.md              # Unified Master Cloud Deployment Guide
└── SmartSembakoAssistant.sln  # Visual Studio Solution File
```

---

## 📄 Lisensi
Hak Cipta © 2026 Smart Sembako Assistant Team. Dikembangkan untuk efisiensi operasional retail sembako modern.
