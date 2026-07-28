# 🚀 Master Panduan Deployment Cloud & Setup Supabase

Dokumen ini berisi panduan komprehensif untuk mendepolikan **Smart Sembako Cloud Bot** ke **Render.com** (pilihan utama), **Hugging Face Spaces**, atau **VPS Ubuntu**, serta panduan konfigurasi **Supabase Cloud Database**.

---

## 📋 Daftar Isi

1. [Arsitektur Deployment Cloud](#1-arsitektur-deployment-cloud)
2. [Langkah 1: Setup Database & Security di Supabase](#langkah-1-setup-database--security-di-supabase)
3. [Langkah 2: Deployment Cloud Bot ke Render.com (Utama)](#langkah-2-deployment-cloud-bot-ke-rendercom-utama)
4. [Langkah 3: Registrasi Telegram Webhook](#langkah-3-registrasi-telegram-webhook)
5. [Langkah 4: Deployment Alternatif (Hugging Face / VPS)](#langkah-4-deployment-alternatif-hugging-face--vps)
6. [Verifikasi Data & TroubleShooting](#verifikasi-data--troubleshooting)

---

## 1. Arsitektur Deployment Cloud

```
┌────────────────────────────────────────────────────────┐
│             C# POS Desktop (Kasir Toko)                │
│  - Supabase.Enabled = true                             │
│  - Auto-Sync interval: 15 menit                        │
└───────────────────────────┬────────────────────────────┘
                            │
                            │ (1-Way Push / UPSERT)
                            ▼
┌────────────────────────────────────────────────────────┐
│             Supabase Cloud Database                    │
│  - Project URL: https://<project>.supabase.co          │
│  - Service Role Key (Akses Write dari C#)              │
│  - Anon Key (Akses Read dari Python Bot)               │
└───────────────────────────▲────────────────────────────┘
                            │
                            │ (Read-Only SELECT)
                            │
┌───────────────────────────┴────────────────────────────┐
│         Python Bot Runtime (Render Web Service)        │
│  - Public URL: https://smart-sembako-backend.onrender.com
│  - Port Binding: Dynamic (${PORT:-10000})              │
│  - Health Check: GET / & HEAD / (HTTP 200 OK)          │
└───────────────────────────▲────────────────────────────┘
                            │
                            │ (Telegram Webhook /webhook/telegram)
                            ▼
┌────────────────────────────────────────────────────────┐
│                 Telegram Bot (@BotFather)              │
└────────────────────────────────────────────────────────┘
```

---

## Langkah 1: Setup Database & Security di Supabase

### A. Membuat Project Supabase
1. Login ke [Supabase Dashboard](https://supabase.com).
2. Klik **New Project**, tentukan nama (misal `smart-sembako-db`) dan password database.
3. Pilih Region terdekat (misal `Singapore ap-southeast-1`).

### B. Menjalankan SQL Schema
1. Masuk ke menu **SQL Editor** di dashboard Supabase.
2. Buka berkas schema projek: `data/supabase_schema.sql`.
3. Copy dan Paste seluruh skrip SQL, lalu klik **Run**.
4. Pastikan tabel `products_sync` dan `transactions_sync` berhasil dibuat.

### C. Mengambil Credentials
Catat variabel berikut dari menu **Settings ➔ API**:
- **Project URL**: `https://<your-project-id>.supabase.co`
- **service_role Key**: Digunakan untuk aplikasi C# Desktop (`config.json`).
- **anon / public Key**: Digunakan untuk Cloud Bot Python (`bot_runtime/.env`).

---

## Langkah 2: Deployment Cloud Bot ke Render.com (Utama)

### A. Konfigurasi `bot_runtime/Dockerfile`
Render menetapkan port dinamis via environment variable `PORT`. Gunakan Dockerfile yang telah diperbarui:

```dockerfile
FROM python:3.11-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
EXPOSE 10000
CMD ["sh", "-c", "uvicorn main:app --host 0.0.0.0 --port ${PORT:-10000}"]
```

### B. Penambahan Health Check Endpoint (`bot_runtime/main.py`)
Render melakukan verifikasi HTTP health check ke root URL (`/`). Handler ini wajib ada di `main.py`:

```python
@app.get("/")
@app.head("/")
@app.get("/bot-health")
async def health_check():
    return {
        "status": "healthy",
        "bot_app": settings.APP_NAME,
        "supabase_configured": bool(settings.SUPABASE_URL and settings.SUPABASE_KEY),
        "telegram_configured": bool(settings.TELEGRAM_BOT_TOKEN or os.getenv("TELEGRAM_BOT_TOKEN"))
    }
```

### C. Konfigurasi Environment Variables di Render Dashboard
Buka [Render Dashboard](https://dashboard.render.com), buat **Web Service** baru dari repository Git Anda, lalu tambahkan variabel berikut pada menu **Environment**:

```text
SUPABASE_URL=https://YOUR_PROJECT_ID.supabase.co
SUPABASE_KEY=YOUR_SUPABASE_SERVICE_ROLE_OR_ANON_KEY
MERCHANT_ID=merchant_smart_sembako
TENANT_ISOLATION_REQUIRED=false
TELEGRAM_BOT_TOKEN=YOUR_TELEGRAM_BOT_TOKEN
TELEGRAM_SECRET_TOKEN=CHANGE_ME_RANDOM_SECRET
OWNER_TELEGRAM_IDS=123456789
GEMINI_API_KEY=YOUR_GEMINI_API_KEY
GROQ_API_KEY=YOUR_GROQ_API_KEY
PORT=10000
```

---

## Langkah 3: Registrasi Telegram Webhook

Setelah Web Service di Render aktif (`https://smart-sembako-backend.onrender.com`), daftarkan Webhook ke Telegram API:

### Perintah Curl (Linux/Mac/PowerShell):
```bash
curl -X POST "https://api.telegram.org/bot<TELEGRAM_BOT_TOKEN>/setWebhook?url=https://smart-sembako-backend.onrender.com/webhook/telegram&secret_token=<TELEGRAM_SECRET_TOKEN>"
```

### Memeriksa Status Webhook:
```bash
curl "https://api.telegram.org/bot<TELEGRAM_BOT_TOKEN>/getWebhookInfo"
```
**Respons Sukses**:
```json
{
  "ok": true,
  "result": {
    "url": "https://smart-sembako-backend.onrender.com/webhook/telegram",
    "has_custom_certificate": false,
    "pending_update_count": 0
  }
}
```

---

## Langkah 4: Deployment Alternatif (Hugging Face / VPS)

### A. Deploy ke Hugging Face Spaces
1. Buat Space baru tipe **Docker** di Hugging Face.
2. Push isi folder `bot_runtime/` ke repository Space tersebut.
3. Tambahkan Secrets di menu **Settings ➔ Repository Secrets** (`SUPABASE_URL`, `TELEGRAM_BOT_TOKEN`, dll).

### B. Deploy ke VPS Ubuntu (Systemd Service)
1. Clone repo di VPS: `git clone https://github.com/jikawetama-svg/smart-sembako-backend.git`.
2. Buat service systemd `/etc/systemd/system/smart-sembako-bot.service`:
   ```ini
   [Unit]
   Description=Smart Sembako Cloud Bot Service
   After=network.target

   [Service]
   User=root
   WorkingDirectory=/root/smart-sembako-backend/bot_runtime
   ExecStart=/usr/local/bin/uvicorn main:app --host 0.0.0.0 --port 8000
   Restart=always
   EnvironmentFile=/root/smart-sembako-backend/bot_runtime/.env

   [Install]
   WantedBy=multi-user.target
   ```
3. Aktifkan service: `systemctl enable --now smart-sembako-bot`.

---

## Verifikasi Data & Troubleshooting

1. **Jalankan Aplikasi Desktop C#**: Buka aplikasi C# POS Desktop, klik tombol **Sync Delta Cloud** di Dashboard.
2. **Uji Perintah Telegram**: Buka bot di Telegram, ketik `/start` atau `CEK STOK KAPAL API MIX`.
3. **Analisis Log Render**: Jika bot tidak merespons, periksa log di Render Dashboard untuk memastikan tidak ada kesalahan autentikasi Supabase atau masalah koneksi LLM.
