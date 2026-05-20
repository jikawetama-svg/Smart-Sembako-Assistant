# Quick Start

Versi singkat. Detail lengkap ada di [SETTINGS_GUIDE.md](SETTINGS_GUIDE.md).

## 1. Jalankan aplikasi
```powershell
cd "D:\HOME\smart sembako\Smart-Sembako-Assistant"
dotnet build SmartSembakoAssistant.sln
dotnet run --project SmartSembakoAssistant\SmartSembakoAssistant.csproj
```

## 2. Ikuti wizard setup
Isi hanya data inti:
- `Nomor Bot WhatsApp`
- `Nomor Owner/Admin`
- `Groq API key`
- `Telegram bot token` jika Anda memilih Telegram

## 3. Hubungkan WhatsApp
- Klik `Hubungkan & Jalankan`.
- Jika memakai mode default `Baileys`, aplikasi akan:
  - mencoba menyalakan sidecar otomatis
  - mengecek dependency Node
  - generate pairing code
- Buka WhatsApp pada nomor bot:
  - `Linked devices`
  - `Link with phone number`
  - masukkan pairing code yang tampil di wizard

## 4. Setelah pairing
- Aplikasi akan kembali ke dashboard.
- Runtime akan dicoba start otomatis.
- Jika belum penuh aktif, cek:
  - `Dashboard`
  - `Settings`
  - `IMPLEMENTATION_STATUS.md`

## Jika ingin Cloud API resmi
- Buka `Settings`.
- Pilih mode `CloudApi` atau `Both`.
- Isi `Access Token`, `Phone Number ID`, `App Secret`, `Verify Token`, dan `Public Webhook URL`.

## Checklist singkat
- Wizard selesai
- `Groq API key` terisi
- `Nomor bot` dan `Nomor owner` benar
- Pairing WhatsApp berhasil
- `pos.db` terdeteksi atau diisi manual
- Dashboard menunjukkan runtime aktif
