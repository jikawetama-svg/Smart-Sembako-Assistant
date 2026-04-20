# 🚀 Quick Start Guide - Smart Sembako Assistant v2.3

## 5 Menit Setup & Running!

### Step 1: Build & Run (1 menit)

```bash
cd "D:\HOME\n8n Ai AGent\SmartSembakoAssistant"
dotnet run
```

Aplikasi akan terbuka dengan dashboard.

---

### Step 2: Dapatkan API Keys (2 menit)

#### Telegram Bot Token
1. Buka Telegram, cari **@BotFather**
2. Kirim pesan: `/newbot`
3. Ikuti instruksi (nama bot, username)
4. Copy **Bot Token** (contoh: `123456789:ABCdefGHIjklMNOpqrsTUVwxyz`)

#### Groq API Key
1. Buka: https://console.groq.com/
2. Sign up (gratis)
3. Klik "API Keys" di sidebar
4. Create API Key
5. Copy key (contoh: `gsk_abc123...`)

---

### Step 3: Konfigurasi (2 menit)

**Cara 1: Via UI (Recommended)**
1. Di aplikasi, klik **⚙️ Settings**
2. Isi **Groq API Key**
3. Isi **Telegram Bot Token**
4. Isi **Owner Chat ID** (Chat ID Telegram Anda)
5. Klik **💾 Save Settings**
6. Klik **🔍 Test All Connections**
7. Jika semua ✅, restart aplikasi

**Cara 2: Edit config.json**
1. Buka `config.json` di text editor
2. Ganti:
   ```json
   "Groq": {
     "ApiKey": "gsk_YOUR_KEY_HERE"
   },
   "Telegram": {
     "BotToken": "YOUR_BOT_TOKEN_HERE",
     "OwnerChatIds": [YOUR_CHAT_ID]
   }
   ```
3. Save & restart aplikasi

---

### Step 4: Start Bot (1 menit)

1. Klik **▶️ Start Bot** di sidebar
2. Bot akan berjalan dalam background
3. Test dengan kirim `/start` ke bot Telegram Anda

**Test Commands**:
```
/stok          - Lihat stok rendah
/laporan       - Laporan hari ini
/restock minyak 50 14000 - Restock produk
/inventory minyak 100 - Koreksi stok
/analisa       - Analisa bisnis
```

**Test Natural Language**:
```
"Stok beras berapa?"
"Berapa penjualan hari ini?"
"Produk apa yang paling laku?"
```

---

## ✅ Checklist Setelah Setup

- [ ] Aplikasi berjalan tanpa error
- [ ] Groq API key terkonfigurasi
- [ ] Telegram Bot Token terkonfigurasi
- [ ] Owner Chat ID terkonfigurasi
- [ ] Test All Connections = semua ✅
- [ ] Bot reply di Telegram
- [ ] pos.db terdetect (jika Aronium terinstall)
- [ ] Restock/Inventory engines berfungsi

---

## 🐛 Troubleshooting Cepat

### Bot tidak start
```
❌ Error: Bot token invalid
✅ Solution: Cek token dari BotFather, pastikan tidak ada spasi
```

### AI tidak meresponse
```
❌ Error: Groq API quota exceeded
✅ Solution: Cek quota di https://console.groq.com/ atau enable Gemini fallback
```

### pos.db not found
```
⚠️ Warning: Database not found
✅ Solution: Settings → Database → Browse ke pos.db atau auto-detect
```

### Error 401/404 pada AI
```
❌ Error: 401 Unauthorized atau 404 Not Found
✅ Solution: Cek Groq API key atau Gemini model name di Settings
```

---

## 📂 Folder Structure (Setelah Run)

```
SmartSembakoAssistant/
├── SmartSembakoAssistant.exe    # (Setelah publish)
├── config.json                  # Konfigurasi Anda
├── config.template.json         # Template
├── data/
│   └── memory.db               # Auto-created saat pertama run
├── bin/
│   └── Debug\net8.0-windows/   # Build output
├── README.md
├── TECHNICAL_DOCS.md
├── QUICK_START.md
└── RESTOCK.md
```

---

## 🎯 Next Steps

Setelah bot berjalan:

1. **Monitor Dashboard**
   - Lihat revenue & profit
   - Pantau critical stock
   - Cek recent conversations

2. **Explore Features**
   - `/stok [nama]` - Search produk
   - `/restock [p] [q] [h]` - Restock produk
   - `/inventory [p] [q]` - Koreksi stok
   - `/riwayat_restock [p]` - Lihat riwayat
   - `/rekomendasi_restock` - Saran otomatis
   - `/notifikasi_stok` - Cek stok kritis

3. **Customize**
   - Adjust notification thresholds
   - Change AI model
   - Configure allowed chat IDs

4. **Deploy to Production**
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   ```
   Copy hasil publish ke folder production

---

## 📞 Butuh Bantuan?

1. Cek **Logs** tab di aplikasi
2. Export log ke CSV
3. Lihat **TECHNICAL_DOCS.md** untuk detail
4. Cek **README.md** untuk user guide
5. Cek **RESTOCK.md** untuk restock engine
6. Cek **QUICK_INVENTORY.md** untuk inventory engine

---

**That's it! Anda sudah siap! 🎉**