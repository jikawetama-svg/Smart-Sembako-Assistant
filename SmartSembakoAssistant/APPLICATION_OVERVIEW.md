# Application Overview

Smart Sembako Assistant adalah desktop automation engine untuk operasional toko sembako.

## Arsitektur saat ini
- Desktop app menjadi orchestration layer utama, tanpa `n8n`.
- Telegram, WhatsApp Cloud API, dan Baileys lokal memakai automation core yang sama.
- AI hanya untuk reasoning dan natural response; operasi stok tetap lewat service dan dokumen Aronium.
- WhatsApp inbound diterima melalui local webhook host di desktop.
- Baileys lokal dijalankan sebagai Node sidecar yang dikelola desktop app.
- Tunnel manager opsional dipakai untuk mengekspos webhook ke public URL.
- Semua outbound channel masuk ke persistent outbox SQLite sebelum dikirim ke provider.
- Pending confirmation, status delivery, dan runtime health disimpan lokal agar tidak hilang saat restart.

## Fungsi bisnis
- Cek stok dan laporan.
- Restock dan inventory correction dengan confirmation flow.
- Riwayat restock dan inventory.
- Analisa bisnis, dead stock, laporan kasir, produk tanpa modal.
- Low stock alert dan daily summary berbasis automation flags.

## Peran user
- Owner: full access.
- Kasir: akses terbatas sesuai command dan channel mapping.

## UI utama
- Dashboard status runtime.
- Monitoring stok.
- Reports dan analytics.
- AI chat.
- Logs.
- Settings untuk AI, Telegram, WhatsApp, Baileys, tunnel, database, dan automation.

## Referensi pengguna
- [SETTINGS_GUIDE.md](SETTINGS_GUIDE.md)
- [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md)
