# Smart Sembako Assistant

Smart Sembako Assistant adalah desktop automation engine WPF .NET 8 untuk toko sembako. Jalur tercepat sekarang memakai wizard first-run: isi `nomor bot WhatsApp`, `nomor owner/admin`, dan `Groq API key`, lalu aplikasi akan menyiapkan Baileys lokal, pairing code, dan runtime.

## Dokumen utama
- [SETTINGS_GUIDE.md](SETTINGS_GUIDE.md)  
  Panduan cepat wizard, pengisian Settings, pairing code Baileys, API key, token, webhook, dan tunnel.
- [QUICK_START.md](QUICK_START.md)  
  Langkah singkat build, run, save config, test, dan start bot.
- [TECHNICAL_DOCS.md](TECHNICAL_DOCS.md)  
  Ringkasan komponen runtime dan endpoint lokal.

## Alur tercepat
1. Jalankan aplikasi.
2. Wizard setup akan muncul otomatis.
3. Isi:
   - nomor bot WhatsApp
   - nomor owner/admin
   - Groq API key
   - token Telegram jika Telegram dipakai
4. Klik `Hubungkan & Jalankan`.
5. Jika memakai Baileys, masukkan pairing code yang muncul ke WhatsApp pada nomor bot.

## Fitur inti yang sudah aktif
- Shared automation core untuk Telegram, WhatsApp Cloud API, dan relay Baileys lokal.
- Wizard setup cepat untuk mode default Baileys.
- Natural language chat dengan Groq dan fallback Gemini.
- Command inti stok, laporan, restock, inventory, history, dan analisa.
- Confirmation flow persisten via `/confirm` dan `/cancel`.
- Persistent outbox, inbound dedupe, runtime state SQLite, dan observability dasar.
- Tunnel manager desktop-first untuk webhook WhatsApp.
- Settings UI dengan draft state, test button non-destructive, dan show/hide secret yang aman.
- OCR struk pembelian dengan review queue.
- Google Sheets export untuk tab prioritas laporan owner.

## Mode WhatsApp
- `CloudApi`: mode resmi Meta Graph API.
- `Baileys`: mode lokal via Node.js sidecar.
- `Both`: dua mode aktif bersamaan.

## Build
```powershell
dotnet build SmartSembakoAssistant.sln
dotnet run --project SmartSembakoAssistant\SmartSembakoAssistant.csproj
```

## Catatan penting
- Mode default untuk user lokal adalah `Baileys`.
- Nomor `Bot WhatsApp` berbeda dari nomor `Owner/Admin`.
- Pairing code muncul otomatis di wizard dan juga bisa digenerate ulang dari `Settings`.
- Dashboard Meta Developers yang Anda pakai adalah jalur resmi untuk Cloud API, tetapi tetap perlu `Access Token`, `Phone Number ID`, `Verify Token`, `App Secret`, dan subscription webhook yang benar.
- Baileys disediakan sebagai opsi lokal tambahan dan bukan WhatsApp Business API resmi.
- Voice note masih fitur lanjutan; OCR, Google Sheets, dan scheduler dasar sudah tersedia lewat flow aplikasi.
