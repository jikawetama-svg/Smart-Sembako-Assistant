# Technical Docs

## Runtime components
- `SetupWizardView`: onboarding first-run untuk input minimal dan pairing code Baileys.
- `SetupReadinessService`: seed default config, deteksi setup belum lengkap, dan simpan setup cepat.
- `AutomationEngine`: shared command routing, AI flow, role checks, confirmation flow, outbox orchestration, scheduled automation, runtime health snapshot.
- `TelegramBotService`: Telegram polling adapter.
- `WhatsAppHandler`: desktop listener untuk Meta webhook dan inbound relay dari Baileys.
- `BaileysSidecarService`: process manager + HTTP client untuk Node.js sidecar Baileys lokal.
- `TunnelManager`: process manager untuk tunnel eksternal, default `cloudflared`.
- `BotController`: lifecycle orchestration dan background automation tick.
- `PosDbService`: akses data Aronium dan pembuatan dokumen purchase / inventory count.
- `GoogleSheetsService`: low-level Google Sheets header, replace, append, dedupe, upsert, chunking, dan retry.
- `GoogleSheetsSyncService`: sync tab prioritas `Dashboard`, `Stok_Kritis`, `Penjualan_Harian`, `Piutang`, dan `Pembelian`.
- `ConfigService`: JSON config + DPAPI encryption untuk secret.
- `DatabaseService`: memory/log/RBAC SQLite lokal plus inbound dedupe, outbound queue, delivery status, automation execution, dan runtime state.

## Endpoint lokal
- `GET /whatsapp/webhook`
- `POST /whatsapp/webhook`
- `POST /baileys/events/inbound`
- `GET /health/integrations`
- `GET /health`
- `GET /session/status`
- `POST /session/pairing/start`
- `POST /session/reset`
- `POST /messages/send`

## Config utama
- `Groq`
- `Telegram`
- `WhatsApp`
- `Baileys`
- `Tunnel`
- `Automation`
- `PosDb`
- `GoogleSheets`
- `Notifications`
- `App`
- `Setup`

## Catatan implementasi
- Telegram dan WhatsApp dikonversi ke `InboundMessage` lalu diproses oleh automation core yang sama.
- Outbound dikirim sebagai `OutboundMessage`, masuk ke persistent outbox, lalu di-dispatch oleh worker background.
- Restock dan inventory memakai pending confirmation persisten berbasis `/confirm` dan `/cancel`.
- WhatsApp signature validation memakai `X-Hub-Signature-256`; jika `AppSecret` kosong sistem dianggap mode local/test dan belum production-ready.
- Tunnel manager mendukung mode `cloudflared`, `external-process`, atau `manual`.
- Baileys berjalan sebagai sidecar Node dan berkomunikasi dengan desktop app lewat HTTP lokal.
- Wizard menyimpan jalur cepat ke mode `Baileys` dengan field utama `BotPhoneNumber`, `OwnerNumbers`, dan `Groq ApiKey`.
- Google Sheets sync memakai header migration, replace/upsert, dedupe key, retry 429/5xx, dan tidak menggagalkan daily summary jika sinkronisasi gagal.

## Dokumen operasional
- [SETTINGS_GUIDE.md](SETTINGS_GUIDE.md)
- [QUICK_START.md](QUICK_START.md)
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md)
