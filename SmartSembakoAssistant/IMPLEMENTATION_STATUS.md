# Status Implementasi Smart Sembako Assistant

Dokumen ini adalah sumber kebenaran status fitur saat ini.

## Working
- Build aplikasi .NET 8 WPF.
- Startup desktop app.
- Shared automation core untuk inbound Telegram, WhatsApp Cloud API, dan Baileys relay lokal.
- Telegram text flow.
- WhatsApp Cloud API:
  - `GET /whatsapp/webhook`
  - `POST /whatsapp/webhook`
  - signature validation jika `AppSecret` terisi
  - outbound text via Meta Graph API
- Persistent outbox SQLite dengan retry dan dead-letter.
- Inbound dedupe berbasis `message_id` / payload hash.
- Runtime state persisten:
  - pending confirmations
  - last webhook
  - outbox retry/failure
  - automation execution log
- Settings:
  - draft state
  - save config
  - show/hide secret tanpa menghapus nilai
  - test button memakai draft, tidak auto-save
- Tunnel manager dasar.
- Dokumentasi setup:
  - `SETTINGS_GUIDE.md`
  - `README.md`
  - `QUICK_START.md`

## Partial
- Telegram confirmation UX:
  - `/confirm` dan `/cancel` ada
  - inline callback Telegram belum dipulihkan ke UX lama
- Dashboard observability:
  - status runtime, outbox, webhook, Meta auth, dan Baileys summary ada
  - belum ada panel detail per-message
- Baileys lokal:
  - sidecar Node disiapkan
  - desktop app sudah punya lifecycle manager, health test, pairing trigger, inbound relay, dan outbound dispatch
  - belum diverifikasi end-to-end dengan sesi WhatsApp nyata di workspace ini
- Tunnel manager:
  - cocok untuk `cloudflared` dan URL manual
  - reconnect strategy masih sederhana

## Blocked by Config
- Groq / Gemini baru aktif jika API key valid diisi user.
- Telegram baru aktif jika bot token valid diisi user.
- WhatsApp Cloud API production-ready baru aktif jika:
  - `AccessToken`
  - `PhoneNumberId`
  - `VerifyToken`
  - `AppSecret`
  - `PublicWebhookUrl` atau tunnel aktif
  - `OwnerNumbers/KasirNumbers`
  sudah benar.
- Baileys baru aktif jika:
  - Node.js tersedia
  - dependency sidecar sudah di-install
  - path sidecar benar
  - sesi pairing sudah berhasil

## Placeholder
- OCR struk.
- Voice note processing.
- Google Sheets sync.
- Scheduler lanjutan di luar low stock + daily summary.
- Media handling WhatsApp/Baileys selain text/caption dasar.

## Planned
- Audit detail delivery/read receipt per provider.
- Reconnect dan lifecycle Baileys yang lebih tangguh.
- Validasi field yang lebih granular di UI.
- Restorasi inline callback Telegram jika benar-benar dibutuhkan.
- Export/import template automation dari UI.
