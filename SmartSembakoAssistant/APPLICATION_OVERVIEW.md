# 📘 Smart Sembako Assistant - Application Overview

**Smart Sembako Assistant** adalah aplikasi desktop berbasis WPF .NET 8 yang dirancang khusus untuk **toko sembako** guna membantu manajemen stok, transaksi, dan operasional harian dengan bantuan AI dan integrasi database Aronium.

---

##  Apa Itu Smart Sembako Assistant?

Smart Sembako Assistant (SSA) adalah **AI-powered assistant** yang terhubung langsung dengan database POS Aronium (`pos.db`) dan berkomunikasi melalui **Telegram Bot**. Aplikasi ini memungkinkan pemilik/kasir toko untuk:

- ✅ Memantau stok produk secara real-time
- ✅ Melakukan restock dengan cepat
- ✅ Melihat laporan penjualan dan profit
- ✅ Mendapat rekomendasi otomatis
- ✅ Berinteraksi dengan sistem melalui chat natural (bahasa Indonesia)

---

## 🎯 Fungsi Utama

### 1. **Bot Telegram Integration**
- User berinteraksi dengan sistem melalui Telegram bot
- Support command (`/stok`, `/restock`, `/inventory`, `/laporan`) dan chat natural
- Role-based access: Owner vs Kasir

### 2. **AI-Powered Assistant**
- Menggunakan **Groq API** (LLaMA 3.1 70B) sebagai AI utama
- Fallback ke **Gemini API** saat Groq error/limit
- Anti-hallucination prompt engineering
- Memory system (short-term & long-term conversation history)

### 3. **Database Aronium Integration**
- READ-ONLY access ke `pos.db` untuk data produk, stok, transaksi
- WRITE access hanya via dokumen resmi (Purchase, Inventory Count, Sales)
- Auto-detect path database Aronium

### 4. **Dashboard & Monitoring**
- Real-time monitoring stok, revenue, profit
- Quick stats: aman, rendah, habis, minus
- Activity logs dengan filter & export

### 5. **Settings & Configuration**
- Konfigurasi AI (Groq + Gemini)
- Konfigurasi Telegram Bot
- Database settings
- Notification preferences

---

## 🚀 Fitur Lengkap

### 🔹 Bot Telegram Commands

| Command | Deskripsi | Role |
|---------|-----------|------|
| `/stok [nama]` | Cek stok produk | Owner, Kasir |
| `/laporan` | Laporan hari ini | Owner, Kasir |
| `/restock [produk] [qty] [harga]` | Restock produk | Owner |
| `/inventory [produk] [target]` | Koreksi stok (SET mode) | Owner |
| `/analisa` | Analisa bisnis lengkap | Owner |
| `/rekomendasi_restock` | Rekomendasi restock otomatis | Owner |
| `/notifikasi_stok` | Cek produk stok habis/minus | Owner |
| `/riwayat_restock` | Riwayat restock | Owner |
| `/riwayat_inventory` | Riwayat inventory | Owner |
| `/help` | Bantuan | Owner, Kasir |

### 🔹 Chat Natural (Bahasa Indonesia)
User bisa chat biasa seperti:
- "Stok beras berapa?"
- "Gimana penjualan hari ini?"
- "Produk apa yang paling laku?"

### 🔹 Quick Inventory (SET Mode)
- `/inventory gula 50` → Set stok gula menjadi 50 (bukan tambah 50)
- Logic: `selisih = target - currentStock`
- Membuat dokumen Inventory Count di Aronium

### 🔹 Bulk Operations
- `/restock produk1 qty1 harga1, produk2 qty2 harga2`
- `/inventory produk1 target1, produk2 target2`

### 🔹 Dashboard Desktop
- Status cards: Bot, AI, Database, Memory
- Quick Insights: Revenue, Profit, Critical Stock
- Recent conversations
- Auto-refresh 30 detik

### 🔹 Stock Monitoring
- Quick stats bar: 🟢Aman 🟡Rendah 🔴Habis ️Minus
- Search & filter
- DataGrid dengan semua info produk

### 🔹 Activity Logs
- Filter by level & category
- Summary stats
- Export CSV

### 🔹 Settings
- AI Configuration (Groq + Gemini)
- Telegram Bot Token & Chat IDs
- Database Path (auto-detect)
- Notification Settings

---

## 🔧 Aspek Teknis

### 🖥️ Technology Stack
| Component | Technology |
|-----------|-----------|
| Framework | WPF .NET 8 |
| Language | C# 12 |
| Telegram Bot | Telegram.Bot library (long polling) |
| AI Provider | Groq API (LLaMA 3.1 70B) + Gemini Fallback |
| Database | SQLite (pos.db Aronium + memory.db) |
| Config | JSON + DPAPI Encryption |

### 📁 Project Structure
```
SmartSembakoAssistant/
├── Models/           # Data models (AppConfig, Product, Memory, etc.)
├── Services/         # Business logic
│   ├── ConfigService.cs       # Configuration management
│   ├── DatabaseService.cs     # SQLite CRUD
│   ├── LoggingService.cs      # Logging system
│   ├── PosDbService.cs        # Aronium integration
│   ├── GroqService.cs         # AI integration
│   ├── TelegramBotService.cs  # Telegram bot handler
│   └── BotController.cs       # Bot lifecycle management
├── Views/            # WPF UserControls
│   ├── DashboardView.xaml
│   ├── StockMonitoringView.xaml
│   ├── LogsView.xaml
│   └── SettingsView.xaml
└── MainWindow.xaml   # Main window with sidebar
```

### 🔐 Security
- **API Keys**: Encrypted with Windows DPAPI
- **Config File**: `config.json` with encrypted sensitive data
- **Chat IDs**: Whitelist for bot access control
- **Role-Based Access**: Owner vs Kasir permissions

### 🤖 AI Integration

#### Primary AI: Groq
- Model: `llama-3.1-70b-versatile`
- Temperature: 0.7 (conversation), 0.3 (parsing)
- Max Tokens: 1000
- Timeout: 30 seconds

#### Fallback AI: Gemini
- Model: `gemini-2.0-flash`
- Auto-switch saat Groq error/timeout
- Retry logic: 2 attempts per provider

#### AI Behavior Settings
- Auto-switch to fallback on error
- Cache AI responses (hemat API limit)
- Retry count: 0-3
- Auto recovery: 5 minutes

### 📊 Database Schema

#### Aronium Database (pos.db)
| Table | Description |
|-------|-------------|
| `Product` | Produk toko (nama, stok, harga, dll) |
| `Document` | Transaksi (Sales, Purchase, Inventory) |
| `DocumentItem` | Detail transaksi |
| `Stock` | Stok per produk & warehouse |
| `User` | User kasir/operator |
| `Warehouse` | Gudang |

#### Memory Database (memory.db)
| Table | Description |
|-------|-------------|
| `conversations` | Chat history dengan AI |
| `long_term_memory` | Patterns & habits |
| `logs` | Activity logs |

### 🔄 Document Types
| ID | Type | Description |
|----|------|-------------|
| 1 | Purchase | Restock dari supplier |
| 2 | Sales | Transaksi penjualan |
| 3 | Inventory Count | Koreksi stok |
| 4 | Refund | Retur dari pelanggan |
| 5 | Stock Return | Retur ke supplier |
| 6 | Loss | Barang hilang/rusak |

---

## ⚙️ Konfigurasi (config.json)

```json
{
  "groq": {
    "apiKey": "encrypted_value",
    "model": "llama-3.1-70b-versatile",
    "fallbackApiKey": "encrypted_value",
    "fallbackModel": "gemini-2.0-flash",
    "maxTokens": 1000,
    "temperature": 0.7
  },
  "telegram": {
    "botToken": "encrypted_value",
    "ownerChatIds": [123456789],
    "kasirChatIds": [987654321],
    "rateLimitSeconds": 5
  },
  "posDb": {
    "databasePath": "auto",
    "autoDetect": true
  }
}
```

---

## 🚀 Cara Setup & Run

### Prerequisites
- .NET 8 SDK
- Telegram Bot Token (dari @BotFather)
- Groq API Key (dari console.groq.com)
- Database Aronium (`pos.db`)

### Setup
1. Clone repository
2. Edit `config.json` dengan API keys Anda
3. Build: `dotnet build --configuration Release`
4. Run: `dotnet run`

### Publish (Single File)
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

---

## 📈 Version History

| Version | Date | Description |
|---------|------|-------------|
| 4.0.0 | 10/04/2026 | Complete UI overhaul - Modern dark sidebar |
| 3.1.0 | 10/04/2026 | AI Fallback & Custom API Key settings |
| 3.0.0 | 10/04/2026 | UI Redesign v4.0 |
| 2.4.0 | 10/04/2026 | Inventory Logic Fix (ADD → SET) |
| 2.3.1 | 09/04/2026 | Minor fixes (Bulk Inventory Parser, Reset Logic) |
| 2.3.0 | 09/04/2026 | Phase 3 Complete (Role-Based, Anti-Hallucination) |
| 2.2.0 | 08/04/2026 | Restock Engine Fix & Quick Inventory |
| 2.1.1 | 05/04/2026 | Database Compatibility Fix |
| 2.1.0 | 05/04/2026 | Phase 1 Complete |

---

## 📞 Support & Resources

- [Groq API Docs](https://console.groq.com/docs)
- [Telegram Bot API](https://core.telegram.org/bots/api)
- [SQLite Documentation](https://www.sqlite.org/docs.html)
- [WPF Documentation](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)

---

**Last Updated:** 10 April 2026  
**Current Version:** 4.0.0  
**Status:** Production Ready ✅

---

**Happy Managing! 🏪✨**
