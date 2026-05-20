# Analisis Bot & Rencana Bulk Single-Document

## Status Aplikasi Saat Ini ✅

### 🤖 Mekanisme Bot Telegram
| Fitur | Status | Catatan |
|---|---|---|
| Long polling | ✅ Aktif | `TelegramBotService` pakai `StartReceiving` |
| Chat ID whitelist | ✅ Ada | `OwnerChatIds`, `KasirChatIds`, `AllowedChatIds` |
| Inline keyboard (YA/BATAL) | ✅ Ada | Muncul otomatis saat ada konfirmasi |
| Photo/voice/sticker handler | ✅ Ada | Balas dengan pesan informatif |
| Duplicate event guard | ✅ Ada | `TryRegisterInboundEventAsync` |
| Outbox retry + dead letter | ✅ Ada | `RunOutboxLoopAsync` tiap 2 detik |

### 📱 Mekanisme Bot WhatsApp
| Fitur | Status | Catatan |
|---|---|---|
| WhatsApp Cloud API | ✅ Ada | `WhatsAppHandler.cs` |
| Baileys (sidecar Node.js) | ✅ Ada | `BaileysSidecarService.cs` |
| Tunnel (ngrok/cloudflare) | ✅ Ada | `TunnelManager.cs` |
| Auto-detect mode | ✅ Ada | `WhatsAppModes.Normalize()` |

### 📟 Command yang Berfungsi
| Command | Status | Catatan |
|---|---|---|
| `/start`, `/help` | ✅ | Daftar command |
| `/stok [nama]` | ✅ | Cari + indikator LOW/OK/OUT |
| `/laporan` | ✅ | Omzet, profit, transaksi hari ini |
| `/confirm` / `/cancel` | ✅ | Dengan inline keyboard Telegram |
| `/restock` (single) | ✅ | 1 dokumen pembelian |
| `/restock` (bulk, koma) | ✅ | N dokumen terpisah |
| `/inventory` (single) | ✅ | 1 dokumen inventory count |
| `/inventory` (bulk, koma) | ✅ | N dokumen terpisah |
| `/analisa` | ✅ | Omzet, profit, dead stock |
| `/pelanggan`, `/supplier`, `/user` | ✅ | Cari data |
| `/penjualan` | ✅ | Ringkasan penjualan produk |
| `/dokumen` | ✅ | Detail per nomor dokumen |
| `/cek_modal` | ✅ | Produk tanpa harga modal |
| `/laporan_kasir` | ✅ | Performa kasir |
| `/dead_stock` | ✅ | Barang >14 hari tidak laku |
| `/riwayat_restock` | ✅ | Histori per produk |
| `/riwayat_inventory` | ✅ | Histori per produk |
| `/rekomendasi_restock` | ✅ | Saran restock AI |
| `/notifikasi_stok` | ✅ | Stok kritis |
| Chat natural | ✅ | Via Groq LLM |

---

## 🔍 Temuan dari Riwayat Percakapan

Berdasarkan riwayat chat bot, semua command berjalan dengan baik. Satu isu penting yang terlihat:

**Bulk restock saat ini membuat N dokumen terpisah** → setelah `/confirm` untuk 10 item:
```
- 2B PENCIL: OK (26-100-000077)
- APETITO: OK (26-100-000078)
- 76 apel 1pk: OK (26-100-000079)
...dst (10 dokumen berbeda)
```

**Yang diinginkan user**: 1 dokumen saja (misal `26-100-000077`) yang berisi semua 10 item.

---

## 🔧 Rencana Implementasi: Bulk Single-Document

### Arsitektur Perubahan

```
PosDbService
  ├── CreatePurchaseDocumentAsync()       ← existing: 1 produk, 1 dok
  ├── [NEW] CreateBulkPurchaseDocumentAsync()  ← 1 dok, N item
  ├── CreateInventoryCountDocumentAsync()  ← existing: 1 produk, 1 dok
  └── [NEW] CreateBulkInventoryCountDocumentAsync()  ← 1 dok, N item
  
AutomationEngine
  ├── ExecuteBulkRestockAsync()    ← MODIFY: panggil metode bulk baru
  └── ExecuteBulkInventoryAsync()  ← MODIFY: panggil metode bulk baru
```

### Detail Method Baru di PosDbService

#### `CreateBulkPurchaseDocumentAsync(items, userId)`
- Terima list `(productId, qty, price)`
- Buka 1 koneksi + 1 transaksi SQLite
- Insert 1 header `Document` (TypeId = 1, Purchase)
- Insert N `DocumentItem` (1 per produk)
- Update N baris `Stock` (tambah qty masing-masing)
- Commit 1x → 1 nomor dokumen
- Return: `BulkDocumentResult { DocumentNumber, DocumentId, ItemResults[] }`

#### `CreateBulkInventoryCountDocumentAsync(items, userId)`
- Terima list `(productId, targetStock)`
- Buka 1 koneksi + 1 transaksi SQLite
- Insert 1 header `Document` (TypeId = 3, Inventory Count)
- Insert N `DocumentItem` (selisih per produk)
- Set N baris `Stock` ke target masing-masing
- Commit 1x → 1 nomor dokumen

### Perubahan AutomationEngine

`ExecuteBulkRestockAsync`: ganti dari loop `foreach` → panggil `CreateBulkPurchaseDocumentAsync`.

Response bot akan berubah dari:
```
Bulk restock selesai: 10/10 produk berhasil.
- 2B PENCIL: OK (26-100-000077)
- APETITO: OK (26-100-000078)
...
```
Menjadi:
```
Bulk restock selesai: 10/10 produk berhasil.
📄 Dokumen: 26-100-000077
- 2B PENCIL: 10 @ Rp 0
- APETITO: 20 Pak @ Rp 0
...
```

---

## ⚠️ Pertimbangan Penting

> [!WARNING]
> Saat ini `CreateInventoryCountDocumentAsync` menolak jika `selisih == 0` (stok tidak berubah).
> Untuk bulk, produk yang stoknya sama perlu di-skip (sudah ada di `QueueBulkInventoryAsync`), tapi tidak boleh membatalkan seluruh dokumen.

> [!IMPORTANT]
> Total dokumen di header harus dihitung ulang = SUM semua item total.
> Untuk Inventory: `total = SUM(selisih * harga)`, mungkin bisa negatif (koreksi turun).

> [!NOTE]
> `GenerateNextDocumentNumberAsync` sudah ada retry logic untuk race condition — tetap digunakan.

---

