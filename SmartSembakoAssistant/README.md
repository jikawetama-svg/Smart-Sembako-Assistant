# 🏪 Smart Sembako Assistant (SSA) v2.3

Aplikasi desktop Windows (WPF .NET 8) untuk membantu toko sembako dengan AI assistant via Telegram Bot.

---

## ✨ Fitur Utama

### 🤖 Telegram Bot
- **Natural Language Understanding** - Chat dengan bahasa Indonesia natural
- **Command Cepat** - `/stok`, `/laporan`, `/analisa`, `/restock`, `/help`
- **Interactive Buttons** - Inline keyboard untuk konfirmasi restock/inventory
- **Memory System** - AI ingat percakapan sebelumnya (short-term & long-term)
- **OCR Struk** - Kirim foto struk untuk parsing otomatis (coming soon)

### 🧠 AI Integration (Groq API)
- **Groq LLaMA 3.1 70B** - Model utama
- **Gemini Fallback** - Otomatis fallback jika Groq error
- **Profit Awareness** - AI mempertimbangkan margin & profit
- **Smart Recommendations** - Rekomendasi restock cerdas
- **Anti-Hallucination** - Aturan ketat agar AI tidak mengarang data

### 💾 Database Integration
- **Aronium pos.db** - Baca langsung dari database Aronium
- **Auto-detect Path** - Otomatis detect path database
- **Real-time Sync** - Data stok & transaksi real-time
- **Safe Restock/Inventory** - Membuat dokumen transaksi (Purchase/Inventory Count) alih-alih update stok langsung

### 📊 Dashboard Desktop
- **Status Monitoring** - Pantau status bot, AI, database
- **Quick Insights** - Revenue, profit, critical stock
- **Stock Monitoring** - Tabel produk dengan filter & search
- **Log & Analytics** - Export log ke CSV
- **Settings UI** - Konfigurasi lengkap dari UI

### 🔔 Notifikasi Otomatis
- **Stok Rendah** - Alert saat stok < 20, 10, 5
- **Expiry Warning** - Warning 30 hari & 7 hari sebelum expiry
- **Daily Summary** - Laporan harian (opsional)
- **Critical Stock Notification** - Command `/notifikasi_stok` untuk cek stok habis/minus

### 📦 Restock & Inventory Engine
- **Restock via Telegram** - `/restock <produk> <qty> [harga]`
- **Quick Inventory** - `/inventory <produk> <qty>` (bisa negatif untuk kurangi stok)
- **Bulk Restock** - `/restock produk1 qty1 harga1, produk2 qty2 harga2`
- **History Tracking** - `/riwayat_restock <produk>` & `/riwayat_inventory <produk>`
- **Auto-Recommendation** - `/rekomendasi_restock` untuk saran restock otomatis

---

## 📋 Requirements

### Minimum
- **OS**: Windows 10/11 (64-bit)
- **RAM**: 4 GB
- **Storage**: 500 MB free space
- **Internet**: Required untuk AI & Telegram

### Software
- **.NET 8.0 Runtime** - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Telegram Account** - Untuk buat bot
- **Groq API Key** - [Get free key](https://console.groq.com/)
- **(Opsional) Gemini API Key** - [Get free key](https://aistudio.google.com/)

---

## 🚀 Setup & Installation

### 1. Download & Extract
```bash
# Extract folder ke lokasi yang Anda inginkan
# Contoh: C:\SmartSembakoAssistant\
```

### 2. Buat Telegram Bot
1. Buka Telegram, cari **@BotFather**
2. Kirim `/newbot`
3. Ikuti instruksi sampai dapat **Bot Token**
4. Simpan token tersebut

### 3. Dapatkan Groq API Key
1. Buka [https://console.groq.com/](https://console.groq.com/)
2. Sign up / Login
3. Buat API key baru di dashboard
4. Copy API key tersebut

### 4. Konfigurasi Aplikasi
1. Jalankan `SmartSembakoAssistant.exe`
2. Klik **⚙️ Settings** di sidebar kiri
3. Isi konfigurasi berikut:

**Groq AI Settings:**
- API Key: Paste Groq API key Anda
- Model: `llama-3.1-70b-versatile` (default)
- Fallback Gemini API Key: (opsional)

**Telegram Bot Settings:**
- Bot Token: Paste token dari BotFather
- Allowed Chat IDs: Chat ID Telegram Anda (untuk Owner access)

**Database Settings:**
- pos.db Path: Klik **Browse** atau auto-detect
- Biasanya otomatis terdetect jika Aronium terinstall

4. Klik **💾 Save Settings**
5. Klik **🔍 Test All Connections** untuk verifikasi
6. Restart aplikasi jika diperlukan

### 5. Start Bot
1. Klik **▶️ Start Bot** di sidebar
2. Bot akan berjalan dalam background
3. Test dengan kirim `/start` ke bot Telegram Anda

---

## 📖 Cara Penggunaan

### Chat Natural Language
Anda bisa chat dengan bahasa natural, contoh:
- "Stok beras berapa?"
- "Berapa penjualan hari ini?"
- "Produk apa yang paling laku?"
- "Rekomendasi restock minggu ini"

### Command Cepat
| Command | Deskripsi | Akses |
|---------|-----------|-------|
| `/start` | Mulai bot & bantuan | Semua |
| `/help` | Tampilkan bantuan | Semua |
| `/stok [nama]` | Cek stok (dengan search) | Semua |
| `/laporan` | Laporan hari ini | Semua |
| `/restock [produk] [qty] [harga]` | Restock produk | Owner |
| `/inventory [produk] [qty]` | Koreksi stok (bisa negatif) | Owner |
| `/riwayat_restock [produk]` | Lihat riwayat restock | Owner |
| `/riwayat_inventory [produk]` | Lihat riwayat inventory | Owner |
| `/rekomendasi_restock` | Saran restock otomatis | Owner |
| `/notifikasi_stok` | Cek stok habis/minus | Owner |
| `/analisa` | Analisa bisnis lengkap | Owner |
| `/cek_modal` | Cek produk tanpa modal | Owner |
| `/laporan_kasir` | Performa kasir | Owner |
| `/dead_stock` | Barang tidak laku > 14 hari | Owner |

### Bulk Restock
Format: `/restock produk1 qty1 harga1, produk2 qty2 harga2`
Contoh: `/restock kapal api mix 50 16000, minyak goreng 30 14000`

---

## 📁 Struktur Folder

```
SmartSembakoAssistant/
├── SmartSembakoAssistant.exe    # Executable utama
├── config.json                  # Konfigurasi (auto-generated)
├── config.template.json         # Template konfigurasi
├── data/
│   ├── memory.db               # Database memory & log
│   └── logs/                   # Folder log files
├── Models/                      # Data models
├── Services/                    # Business logic services
│   ├── ConfigService.cs
│   ├── DatabaseService.cs
│   ├── LoggingService.cs
│   ├── PosDbService.cs          # Aronium integration + Restock/Inventory Engine
│   ├── GroqService.cs           # AI service
│   └── TelegramBotService.cs    # Telegram bot handler
├── Views/                       # UI Views
│   ├── DashboardView.xaml
│   ├── StockMonitoringView.xaml
│   ├── LogsView.xaml
│   └── SettingsView.xaml
└── README.md                    # Dokumentasi ini
```

---

## ⚙️ Konfigurasi Lanjutan

### config.json
File konfigurasi lengkap (bisa edit manual jika perlu):

```json
{
  "Groq": {
    "ApiKey": "gsk_...",
    "Model": "llama-3.1-70b-versatile",
    "FallbackApiKey": "",
    "FallbackModel": "gemini-1.5-flash",
    "TimeoutSeconds": 30,
    "MaxTokens": 500,
    "Temperature": 0.7
  },
  "Telegram": {
    "BotToken": "123456:ABC...",
    "AllowedChatIds": [123456789],
    "OwnerChatIds": [123456789],
    "KasirChatIds": [],
    "RateLimitSeconds": 5,
    "EnableVoiceNotes": false
  },
  "PosDb": {
    "DatabasePath": "C:\\Users\\...\\pos.db",
    "AutoDetect": true
  },
  "Notifications": {
    "StockThresholds": [
      { "Level": 20, "Priority": "Low" },
      { "Level": 10, "Priority": "Medium" },
      { "Level": 5, "Priority": "High" }
    ],
    "CheckIntervalMinutes": 5
  }
}
```

---

## 🔒 Keamanan

- **API Keys Terenkripsi** - Menggunakan Windows DPAPI
- **Whitelist Chat ID** - Batasi siapa yang bisa akses bot
- **Role-Based Access** - Owner vs Kasir
- **Rate Limiting** - Cegah spam
- **No Auto-Order** - Tidak ada order otomatis ke supplier
- **Konfirmasi User** - Restock/Inventory butuh konfirmasi inline button

---

## 🛠️ Troubleshooting

### Bot tidak bisa start
- Pastikan Bot Token benar
- Cek koneksi internet
- Lihat log untuk detail error

### pos.db not found
- Pastikan Aronium terinstall
- Cek path di Settings → Database
- Gunakan auto-detect atau browse manual

### AI tidak meresponse
- Cek Groq API key
- Pastikan ada kuota/pulsa di Groq
- Cek fallback Gemini (jika dikonfigurasi)

### Error 401/404 pada AI
- **401 Unauthorized**: API Key Groq salah/expired. Update di Settings.
- **404 Not Found**: Model Gemini tidak ditemukan. Cek konfigurasi fallback.

### Aplikasi crash
- Pastikan .NET 8 Runtime terinstall
- Cek Windows Event Viewer untuk detail
- Hapus folder `data` dan restart aplikasi

---

## 📊 Build from Source (Untuk Developer)

### Prerequisites
- Visual Studio 2022 / VS Code
- .NET 8 SDK
- Git

### Build Steps
```bash
# Clone repository
git clone <repo-url>
cd SmartSembakoAssistant

# Restore dependencies
dotnet restore

# Build
dotnet build --configuration Release

# Run
dotnet run

# Publish (untuk deployment)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

---

## 🗺️ Roadmap

### Phase 1 (✅ Selesai)
- ✅ Dashboard WPF
- ✅ Telegram Bot
- ✅ Groq AI Integration
- ✅ pos.db Integration
- ✅ Memory System
- ✅ Settings UI

### Phase 2 (✅ Selesai)
- ✅ Restock Engine (Purchase Document)
- ✅ Quick Inventory Engine (Inventory Count Document)
- ✅ History Tracking (Restock & Inventory)
- ✅ Auto-Recommendation Restock
- ✅ Critical Stock Notification
- ✅ Bulk Restock Support

### Phase 3 (✅ Selesai)
- ✅ Role-Based Access (Owner/Kasir)
- ✅ Anti-Hallucination Prompt Engineering
- ✅ Fix DocumentTypeId Mapping
- ✅ Enhanced Error Handling

### Phase 4 (Coming Soon)
- ⏳ OCR Struk dengan Tesseract
- ⏳ Google Sheets Integration
- ⏳ Background Scheduler (Auto-notif)
- ⏳ Installer + Auto-start Windows

### Phase 5 (Future)
- Voice note support
- Supplier database
- Multi-cabang support
- Advanced analytics & charts

---

## 📞 Support

Untuk pertanyaan atau issue:
- Cek log di aplikasi (tab Log & Analitik)
- Export log ke CSV untuk analisa
- Lihat dokumentasi di file ini

---

## 📄 License

Proprietary - Smart Sembako Assistant v2.3

---

**Dibuat dengan ❤️ untuk UMKM Indonesia**

*Versi: 2.3 | Tanggal: April 2026*