# Panduan Setup dan Penggunaan Settings

Panduan ini sekarang dibagi dua:
- `Wizard Cepat`: jalur utama untuk user biasa.
- `Settings Lanjutan`: jalur detail untuk Cloud API, tunnel, dan field teknis lain.

Gunakan wizard jika Anda ingin aplikasi langsung jalan tanpa mengisi port/path secara manual.

## Wizard Cepat

Saat aplikasi pertama kali dibuka, wizard akan muncul otomatis jika setup belum lengkap.

Field yang diminta:
- `Nomor Bot WhatsApp`
  - nomor yang akan dipairing sebagai device bot
  - contoh: `6281234567890`
- `Nomor Owner/Admin`
  - nomor yang diberi hak owner
  - contoh: `6282223334444`
- `Groq API key`
- `Telegram bot token`
  - hanya jika Anda memilih mode Telegram

Setelah klik `Hubungkan & Jalankan`:
1. config dasar akan disimpan
2. sidecar Baileys dicoba dijalankan otomatis
3. aplikasi akan generate pairing code
4. Anda memasukkan pairing code itu ke WhatsApp pada nomor bot:
   - `Linked devices`
   - `Link with phone number`
5. dashboard dibuka setelah setup selesai

## Ringkasan cepat
- `Save Settings` menyimpan perubahan ke `config.json`.
- Tombol `Test` memakai nilai draft yang sedang tampil di form dan tidak menimpa `config.json` sampai Anda klik `Save Settings`.
- Tombol `Show/Hide` sekarang hanya mengubah tampilan field secret. Nilai asli tidak dihapus.
- Status di bagian atas akan berubah menjadi `Draft berubah, belum disimpan` jika ada perubahan yang belum disimpan.

## Sebelum mulai
- Pastikan file database `pos.db` Aronium tersedia.
- Untuk AI, siapkan minimal `Groq API key`.
- Untuk Telegram, siapkan token dari `BotFather`.
- Untuk WhatsApp Cloud API resmi, siapkan:
  - `Access Token`
  - `Phone Number ID`
  - `App Secret`
  - `Verify Token`
  - webhook subscription pada dashboard Meta Developers
- Untuk tunnel lokal, siapkan `cloudflared.exe` atau tool tunnel lain.
- Untuk Baileys lokal, siapkan `Node.js 18+`.

## 1. Status Draft
- Bagian paling atas menunjukkan apakah perubahan sudah disimpan.
- Jika tertulis `Draft berubah, belum disimpan`, test tombol masih aman dipakai, tetapi runtime aplikasi belum memakai config baru sampai disimpan.

## 2. AI Primary (Groq)

### API Key
- Fungsi: kunci utama untuk chat AI dan analisa natural language.
- Wajib: ya, jika Anda ingin fitur AI aktif.
- Contoh isi:
```text
gsk_abc123contohapikey
```

### Model
- Fungsi: model Groq yang dipakai saat runtime.
- Rekomendasi awal: `llama-3.3-70b-versatile`.

### Max Tokens
- Fungsi: batas panjang jawaban AI.
- Contoh: `500`

### Temperature
- Fungsi: kreativitas model.
- Contoh aman untuk operasional toko: `0.3` sampai `0.7`

### Tombol
- `Show/Hide`: tampilkan atau sembunyikan API key tanpa menghapus nilainya.
- `Test`: uji koneksi Groq memakai nilai form saat ini.

## 3. AI Fallback (Gemini)

### Enable fallback AI
- Fungsi: mengaktifkan provider cadangan jika Groq gagal.
- Opsional: ya.

### Fallback API Key
- Contoh isi:
```text
AIzaSyContohGeminiKey
```

### Fallback Model
- Rekomendasi awal: `gemini-1.5-flash`

## 4. Telegram

### Bot Token
- Fungsi: token bot Telegram.
- Wajib: jika Anda ingin channel Telegram aktif.
- Contoh isi:
```text
123456789:AAExampleTelegramBotToken
```

### Owner Chat IDs
- Fungsi: daftar chat ID owner dengan akses penuh.
- Format: pisahkan dengan koma.
- Contoh:
```text
123456789, 987654321
```

### Kasir Chat IDs
- Fungsi: daftar chat ID kasir dengan akses terbatas.
- Contoh:
```text
1122334455
```

## 5. WhatsApp

### Enable WhatsApp integration
- Fungsi: mengaktifkan integrasi WhatsApp secara umum.

### Transport Mode
- `CloudApi`: hanya jalur resmi Meta.
- `Baileys`: hanya jalur lokal Baileys.
- `Both`: Cloud API dan Baileys aktif bersamaan.

### Access Token
- Fungsi: token Graph API untuk WhatsApp Cloud API.
- Wajib jika mode memakai `CloudApi`.

### Phone Number ID
- Fungsi: ID nomor WhatsApp Cloud API.
- Contoh:
```text
1060642770465141
```

### App Secret
- Fungsi: validasi `X-Hub-Signature-256`.
- Wajib untuk mode production-ready Cloud API.

### Verify Token
- Fungsi: token verifikasi webhook Meta.
- Contoh:
```text
ssa-verify-token
```

### Webhook Port
- Fungsi: port listener lokal desktop.
- Default:
```text
8090
```

### Public Base URL
- Fungsi: base URL publik yang mengarah ke listener lokal.
- Contoh:
```text
https://ssa-toko-anda.trycloudflare.com
```

### Outbound Max Retries
- Fungsi: jumlah percobaan kirim ulang ke provider.
- Contoh:
```text
5
```

### Initial Retry Delay (s)
- Fungsi: delay awal retry. Delay berikutnya akan bertambah secara eksponensial.
- Contoh:
```text
15
```

### Owner Numbers
- Fungsi: nomor owner untuk otorisasi dan broadcast sistem.
- Format: digit only, tanpa `+`, dipisahkan koma.
- Contoh:
```text
628123456789, 6282223334444
```

### Kasir Numbers
- Fungsi: nomor kasir untuk akses terbatas.
- Contoh:
```text
628111000222
```

### Tombol
- `Test Meta`: validasi `Access Token` + `Phone Number ID`.
- `Test Webhook`: cek apakah kombinasi `Verify Token`, `Public Base URL`, dan `App Secret` sudah siap.
- `Test Outbound`: kirim pesan test ke nomor owner pertama, jika ada.

### URL webhook final
- Endpoint verifikasi dan inbound:
```text
{Public Base URL}/whatsapp/webhook
```
- Contoh:
```text
https://ssa-toko-anda.trycloudflare.com/whatsapp/webhook
```

## 6. WhatsApp Baileys Lokal

Baileys adalah mode lokal berbasis Node.js sidecar. Ini bukan jalur resmi WhatsApp Business API.

### Enable Baileys local sidecar
- Aktifkan jika Anda ingin memakai transport lokal Baileys.

### Bot Phone Number
- Fungsi: nomor yang dipairing menjadi bot WhatsApp.
- Ini berbeda dari nomor owner/admin.
- Contoh:
```text
6281234567890
```

### Auto-start Node sidecar from desktop app
- Jika aktif, desktop app akan mencoba menjalankan sidecar Node otomatis saat runtime dimulai.

### Node Binary Path
- Contoh Windows:
```text
C:\Program Files\nodejs\node.exe
```
- Bisa juga `node` jika PATH sistem sudah benar.

### Sidecar Entry Path
- Default yang sudah disiapkan repo:
```text
Integrations\BaileysSidecar\index.js
```

### Working Directory
- Default:
```text
Integrations\BaileysSidecar
```

### Session Path
- Fungsi: lokasi sesi login Baileys.
- Contoh:
```text
data\baileys-session
```

### Local API Port
- Fungsi: port HTTP lokal sidecar Baileys.
- Default:
```text
8091
```

### Owner Numbers / Kasir Numbers
- Format sama seperti WhatsApp Cloud API.

### Tombol
- `Test Baileys`: aplikasi akan mencoba menyalakan sidecar lalu mengecek endpoint lokal.
- `Start Pairing`: generate pairing code memakai `Bot Phone Number`.
- Jika pairing gagal, gunakan `Reset Sesi` dari wizard lalu generate ulang.

### Langkah awal Baileys
Untuk user biasa:
1. Pakai wizard.
2. Isi `Nomor Bot WhatsApp`, `Nomor Owner/Admin`, dan `Groq API key`.
3. Klik `Hubungkan & Jalankan`.
4. Masukkan pairing code ke WhatsApp.

Untuk mode manual / advanced:
1. Buka terminal di folder project `SmartSembakoAssistant`.
2. Masuk ke folder sidecar:
```powershell
cd Integrations\BaileysSidecar
```
3. Install dependency:
```powershell
npm install
```
4. Isi field Baileys di Settings.
5. Klik `Save Settings`.
6. Klik `Test Baileys`.
7. Jika sidecar belum jalan otomatis, jalankan manual:
```powershell
node index.js
```

## 7. Tunnel

### Enable built-in tunnel manager
- Aktifkan jika Anda ingin desktop app menjalankan proses tunnel sendiri.

### Provider
- `cloudflared`: rekomendasi utama.
- `external-process`: provider lain yang Anda kendalikan sendiri.
- `manual`: aplikasi tidak menjalankan proses apa pun, Anda isi URL publik manual.

### Binary Path
- Contoh `cloudflared.exe`:
```text
C:\Tools\cloudflared\cloudflared.exe
```

### Args Template
- Contoh bawaan:
```text
tunnel --url http://localhost:{port}
```
- Jika tool Anda butuh format lain, ubah di sini.

### Current / Manual Public URL
- Isi jika Anda memakai tunnel manual atau sudah punya reverse proxy sendiri.
- Contoh:
```text
https://ssa-toko-anda.trycloudflare.com
```

### Tombol
- `Test Tunnel`: memastikan listener lokal merespons health endpoint.

## 8. Automation Engine

### Enable template-based automation
- Mengaktifkan rule/template bawaan.

### Enable low stock alerts
- Owner akan menerima broadcast stok kritis.

### Enable daily summary
- Mengaktifkan ringkasan harian otomatis.

### Daily Summary Time
- Format:
```text
07:00
```

## 9. Database

### pos.db Path
- Isi path manual jika auto-detect tidak menemukan file Aronium.
- Contoh:
```text
C:\Users\NamaUser\AppData\Local\Aronium\Data\pos.db
```

### Auto-detect pos.db path
- Jika aktif, aplikasi akan mencoba mencari `pos.db` otomatis.

## 10. Tombol bawah

### Test All Connections
- Menampilkan ringkasan konfigurasi draft saat ini:
  - Groq
  - Gemini
  - Telegram
  - WhatsApp mode
  - Cloud API readiness
  - Baileys readiness
  - Tunnel
  - Database

### Save Settings
- Menulis semua perubahan ke `config.json`.
- Setelah save, MainWindow akan refresh config lokal selama bot tidak sedang berjalan.

## Contoh konfigurasi minimal

### Telegram + Groq saja
```json
{
  "Groq": {
    "ApiKey": "gsk_abc123",
    "Model": "llama-3.3-70b-versatile"
  },
  "Telegram": {
    "BotToken": "123456789:AAExample",
    "OwnerChatIds": [123456789],
    "KasirChatIds": [1122334455]
  }
}
```

### WhatsApp Cloud API resmi
```json
{
  "WhatsApp": {
    "Enabled": true,
    "Mode": "CloudApi",
    "AccessToken": "EAAG...",
    "PhoneNumberId": "1060642770465141",
    "AppSecret": "abcdef123456",
    "VerifyToken": "ssa-verify-token",
    "LocalWebhookPort": 8090,
    "PublicWebhookUrl": "https://ssa-toko-anda.trycloudflare.com",
    "OwnerNumbers": ["628123456789"],
    "KasirNumbers": ["628111000222"]
  },
  "Tunnel": {
    "Enabled": true,
    "Provider": "cloudflared",
    "BinaryPath": "C:\\Tools\\cloudflared\\cloudflared.exe",
    "ArgsTemplate": "tunnel --url http://localhost:{port}"
  }
}
```

### WhatsApp Baileys lokal
```json
{
  "WhatsApp": {
    "Enabled": true,
    "Mode": "Baileys",
    "LocalWebhookPort": 8090
  },
  "Baileys": {
    "Enabled": true,
    "NodeBinaryPath": "C:\\Program Files\\nodejs\\node.exe",
    "SidecarEntryPath": "Integrations\\BaileysSidecar\\index.js",
    "WorkingDirectory": "Integrations\\BaileysSidecar",
    "SessionPath": "data\\baileys-session",
    "LocalApiPort": 8091,
    "AutoStart": true,
    "OwnerNumbers": ["628123456789"]
  }
}
```

## Cara start aplikasi
1. Jalankan build:
```powershell
dotnet build SmartSembakoAssistant.sln
```
2. Jalankan app:
```powershell
dotnet run --project SmartSembakoAssistant\SmartSembakoAssistant.csproj
```
3. Isi Settings.
4. Klik `Save Settings`.
5. Klik `Test` yang relevan.
6. Klik `Start Bot`.

## Membaca Dashboard
- `Runtime On`: runtime aktif.
- `WA webhook aktif/mati`: listener desktop untuk Meta/Baileys inbound.
- `Meta auth siap`: field Cloud API inti sudah lengkap.
- `Baileys aktif/siap/mati`: status sidecar.
- `Signature aktif/local-test`: `AppSecret` terisi atau belum.
- `Outbox`: jumlah pesan antre.
- `Last webhook` dan `Last sent`: observability dasar.

## Troubleshooting umum

### API key / token hilang saat klik Hide
- Versi terbaru sudah memperbaiki ini.
- Jika masih terjadi, cek apakah Anda belum menjalankan build lama.

### Test berhasil tetapi runtime belum memakai config baru
- Tombol `Test` tidak menyimpan config.
- Klik `Save Settings` setelah yakin hasil test benar.

### Test Webhook bilang belum siap
- Cek `Verify Token`.
- Cek `Public Base URL` atau `Tunnel`.
- Isi `App Secret` jika ingin production-ready.

### Cloud API tidak bisa kirim pesan
- Pastikan `Access Token` masih valid.
- Pastikan `Phone Number ID` benar.
- Pastikan nomor tujuan sesuai aturan Cloud API Anda.

### Baileys tidak reachable
- Pastikan `npm install` sudah dijalankan di `Integrations\BaileysSidecar`.
- Pastikan `Node Binary Path` benar.
- Pastikan `Local API Port` tidak bentrok.

### pos.db tidak terbaca
- Matikan `Auto-detect`.
- Isi path manual ke file `pos.db`.

## Dokumen terkait
- [README.md](README.md)
- [QUICK_START.md](QUICK_START.md)
- [TECHNICAL_DOCS.md](TECHNICAL_DOCS.md)
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md)
