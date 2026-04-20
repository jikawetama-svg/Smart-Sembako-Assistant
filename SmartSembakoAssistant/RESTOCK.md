# 📦 RESTOCK ENGINE - SMART SEMBAKO ASSISTANT v2.3

## 🎯 TUJUAN
Membuat fitur **RESTOCK** via Telegram Bot yang:
- ✅ **Aman**: Tidak mengubah stok langsung, tapi membuat dokumen transaksi (Type 1 - Purchase)
- ✅ **Audit Trail**: Semua restock tercatat dan bisa dilacak di Aronium
- ✅ **Integrasi Aronium**: Meniru mekanisme Aronium Lite (Document + DocumentItem)
- ✅ **User-Friendly**: Perintah sederhana dari Telegram
- ✅ **Konfirmasi**: Anti-salah input dengan sistem konfirmasi Inline Keyboard

---

## 🧠 ARSITEKTUR SISTEM

```
Telegram Bot → Intent Parser → Validator → Restock Engine → Database Aronium
                                                              ↓
                                                    Konfirmasi User (Inline Button)
                                                              ↓
                                                    INSERT Document (Type 1)
                                                    INSERT DocumentItem
                                                    UPDATE Stock
                                                              ↓
                                                    Response Sukses
```

---

## 📊 STRUKTUR DATABASE ARONIUM (VERIFIED)

Berdasarkan analisis database `pos.db` dan screenshot Aronium Lite Anda:

### **1. Tabel Document (Header Transaksi)**
| Kolom | Tipe | Keterangan |
|-------|------|------------|
| Id | INTEGER PK | Auto-increment |
| Number | TEXT | Nomor dokumen (format: `26-100-000001`) |
| Date | DATE | Tanggal transaksi |
| DocumentTypeId | INTEGER FK | **1 = Purchase** (Restock) |
| Total | NUMERIC | Total nilai transaksi |
| UserId | INTEGER FK | User yang membuat (Default: 1) |
| DateCreated | DATETIME | Waktu dibuat |
| PaidStatus | INTEGER | 0 = Tidak Dibayar (Default) |

### **2. Tabel DocumentItem (Detail Transaksi)**
| Kolom | Tipe | Keterangan |
|-------|------|------------|
| Id | INTEGER PK | Auto-increment |
| DocumentId | INTEGER FK | Reference ke Document |
| ProductId | INTEGER FK | Reference ke Product |
| Quantity | NUMERIC | Jumlah item (Positif untuk Restock) |
| Price | NUMERIC | Harga Modal per unit |
| Total | NUMERIC | Total (Qty × Price) |

### **3. Kode Dokumen (Dari Screenshot)**
| DocumentTypeId | Tipe Dokumen | TypeCode (Nomor) | Kegunaan |
|------|--------------|----------|----------|
| **1** | **Purchase** | **100** | **RESTOCK / PEMBELIAN** |
| 2 | Sales | 200 | Penjualan |
| 3 | Inventory Count | 300 | Stock Opname |
| 4 | Refund | 220 | Pengembalian dana |
| 5 | Stock Return | 120 | Retur ke supplier |
| 6 | Loss | 400 | Barang rusak/hilang |

### **4. Format Nomor Dokumen**
- Format: `YY-TYPECODE-SEQUENCE`
- Contoh: `26-100-000001`
- `26`: Tahun 2026
- `100`: TypeCode untuk Purchase
- `000001`: Urutan ke-1
- **Nomor Terakhir di DB**: `26-100-000001` (ID: 74)
- **Nomor Berikutnya**: `26-100-000002`

---

## 🔐 RULE KEAMANAN (WAJIB)

### ✅ DO (Lakukan):
1. **Selalu buat Document + DocumentItem** untuk restock
2. **Gunakan Transaction** (BEGIN/COMMIT) untuk atomicity
3. **Validasi** produk, quantity, dan harga sebelum insert
4. **Konfirmasi user** dengan Inline Keyboard [YA] / [BATAL]
5. **Log semua transaksi** restock ke logging service
6. **Hanya Owner** yang bisa akses fitur restock
7. **Update tabel Stock** setelah dokumen dibuat

### ❌ DON'T (Jangan):
1. **JANGAN UPDATE Product.Stock langsung** (melanggar integritas Aronium)
2. **JANGAN skip konfirmasi** (risiko salah input)
3. **JANGAN izinkan quantity negatif** untuk restock
4. **JANGAN izinkan harga negatif**
5. **JANGAN gunakan DocumentTypeId salah** (1≠100)

---

##  COMMAND TELEGRAM

### **Format Command:**
```
/restock <nama_produk> <quantity> [harga_modal]
```

### **Contoh Penggunaan:**
```bash
# Restock dengan harga modal
/restock minyak goreng 50 14000

# Restock tanpa harga modal (akan pakai harga modal terakhir atau 0)
/restock gula pasir 25

# Restock dengan nama produk yang mengandung spasi
/restock kapal api mix 100 16000

# Bulk Restock
/restock kapal api mix 50 16000, minyak goreng 30 14000, gula 25 12000
```

### **Flow Command:**
1. User ketik: `/restock minyak 50 14000`
2. Bot parse: `{produk: "minyak", qty: 50, harga: 14000}`
3. Bot cari produk di database (fuzzy match)
4. Bot tampilkan konfirmasi dengan tombol **[✅ YA]** dan **[❌ BATAL]**
5. Jika YA → Buat Document (Type 1) + DocumentItem + Update Stock
6. Bot balas: "✅ RESTOCK BERHASIL - Dokumen: 26-100-000002"

---

## 🧪 VALIDASI INPUT

### **Validasi Produk:**
```csharp
// Fuzzy match untuk toleransi typo
var product = allProducts.FirstOrDefault(p => 
    p.Name.ToLower().Contains(keyword.ToLower()) ||
    p.Name.ToLower().Contains(keyword.ToLower().Replace(" ", "")));

if (product == null)
    return "❌ Produk tidak ditemukan. Cek ejaan atau gunakan /stok untuk cari.";
```

### **Validasi Quantity:**
```csharp
if (qty <= 0)
    return "❌ Quantity harus lebih dari 0.";

if (qty > 1000)
    return "❌ Quantity terlalu besar. Maksimal 1000 per transaksi.";
```

### **Validasi Harga:**
```csharp
if (harga < 0)
    return "❌ Harga modal tidak boleh negatif.";

if (harga == 0)
    // Gunakan harga modal terakhir dari riwayat atau 0
    harga = product.PurchasePrice ?? 0;
```

---

## 💬 SISTEM KONFIRMASI (INLINE KEYBOARD)

### **Template Konfirmasi:**
```
📦 **KONFIRMASI RESTOCK**

📋 Detail:
• Produk: Minyak Goreng 2L
• Quantity: 50 Pcs
• Harga Modal: Rp 14.000/pcs
• Total Modal: Rp 700.000

⚠️ Aksi ini akan membuat dokumen pembelian di sistem.

Lanjutkan?

[✅ YA] [❌ BATAL]
```

### **Implementasi Callback:**
- `restock_confirm_{productId}_{qty}_{price}` → Execute Restock
- `bulk_restock_confirm_{data}` → Execute Bulk Restock
- `restock_cancel` → Batalkan

---

## 🧾 IMPLEMENTASI CODE (C# + SQLite)

### **1. Method: CreatePurchaseDocumentAsync()**
```csharp
public async Task<RestockResult> CreatePurchaseDocumentAsync(
    int productId, 
    decimal quantity, 
    decimal price,
    int userId = 1)
{
    // 1. Generate Nomor Dokumen (Format: 26-100-NNNNNN)
    string docNumber = await GenerateNextDocumentNumberAsync(connection, transaction, 1);
    
    // 2. Insert Document (Header)
    // Type 1 = Purchase
    // PaidStatus = 0 (Tidak Dibayar)
    
    // 3. Insert DocumentItem (Detail)
    // Quantity positif
    
    // 4. Update Stock table
    // newQty = currentQty + quantity
    
    // 5. Commit Transaction
}
```

### **2. Method: GenerateNextDocumentNumberAsync()**
```csharp
private async Task<string> GenerateNextDocumentNumberAsync(...)
{
    // Format: YY-TYPECODE-NNNNNN
    // Cari nomor terakhir yang LIKE '26-100-%'
    // Increment sequence
    // Return: "26-100-000002"
}
```

---

## 🔁 CARA KERJA UPDATE STOK

### **Mekanisme Aronium:**
Aronium **TIDAK** mengupdate `Product.Stock` secara langsung saat transaksi. Stok dihitung secara **real-time** dari:
```
Stok = SUM(DocumentItem.Quantity) 
WHERE Document.Type = 'Purchase' (1)
  - SUM(DocumentItem.Quantity) 
WHERE Document.Type = 'Sales' (2)
```

### **Keuntungan:**
- ✅ **Audit Trail Lengkap**: Semua perubahan stok tercatat di dokumen
- ✅ **Bisa Dirollback**: Jika ada kesalahan, dokumen bisa dihapus
- ✅ **Laporan Akurat**: Laporan pembelian & penjualan tetap konsisten
- ✅ **Multi-User**: Tidak ada conflict saat banyak user akses

---

## ⚠️ HANDLING MASALAH

### **Problem 1: Aronium Sedang Dibuka**
**Solusi:**
- SQLite mendukung **multi-reader, single-writer**
- Gunakan `PRAGMA busy_timeout = 5000` (tunggu 5 detik jika database locked)
- Jalankan restock di jam sepi jika memungkinkan

### **Problem 2: Nama Produk Tidak Cocok**
**Solusi:**
- Gunakan **fuzzy matching** (Levenshtein distance)
- Tampilkan pilihan jika ada beberapa produk mirip

### **Problem 3: Salah Input**
**Solusi:**
- **Konfirmasi WAJIB** dengan tombol [YA] / [BATAL]
- Log semua transaksi untuk audit

---

## 🔒 KEAMANAN

### **1. Whitelist User Telegram**
- Hanya Owner (Chat ID di `OwnerChatIds`) yang bisa akses `/restock`

### **2. Audit Log**
Semua transaksi restock dicatat di logging service:
```
[2026-04-07 06:30:00] INFO: Restock executed by User 123456789
- Product: Minyak Goreng (ID: 45)
- Quantity: +50
- Price: Rp 14.000
- Document: 26-100-000002
```

---

## 📊 OUTPUT FINAL

### **Sukses:**
```
✅ RESTOCK BERHASIL

📦 Detail:
• Dokumen: 26-100-000002
• Produk: Minyak Goreng 2L
• Quantity: +50 Pcs
• Total Modal: Rp 700.000

Stok akan otomatis bertambah setelah dokumen diproses Aronium.
```

### **Gagal:**
```
❌ RESTOCK GAGAL

Alasan: Database sedang digunakan oleh Aronium.
Silakan coba lagi dalam beberapa menit.
```

---

##  ROADMAP IMPLEMENTASI

### **Phase 1: Core Engine (SELESAI)**
- [x] Parser command `/restock`
- [x] Validator input (produk, qty, harga)
- [x] Insert Document + DocumentItem (Type 1)
- [x] Konfirmasi user (Inline Keyboard)
- [x] Generate nomor dokumen otomatis (`26-100-...`)
- [x] Update Stock table

### **Phase 2: Enhancement (SELESAI)**
- [x] Bulk Restock
- [x] History Tracking (`/riwayat_restock`)
- [x] Auto-Recommendation (`/rekomendasi_restock`)

### **Phase 3: Advanced (SELESAI)**
- [x] Role-Based Access
- [x] Anti-Hallucination Prompt
- [x] Fix DocumentTypeId Mapping

---

## 📝 CATATAN PENTING

1. **JANGAN langsung UPDATE Product.Stock** - Ini akan merusak integritas data Aronium
2. **Selalu gunakan Transaction** - Agar data konsisten jika ada error di tengah proses
3. **Nomor dokumen harus unik** - Gunakan format `26-100-NNNNNN` seperti Aronium
4. **Log semua transaksi** - Untuk audit trail dan troubleshooting
5. **Test di database backup** - Sebelum deploy ke production, test di copy database
6. **DocumentTypeId = 1** untuk Purchase, BUKAN 100!

---

## 🎯 KESIMPULAN

Dengan **RESTOCK ENGINE** ini:
- ✅ Restock bisa dilakukan dari Telegram tanpa buka Aronium
- ✅ Data tetap valid dan tercatat di dokumen (Type 1 - Purchase)
- ✅ Nomor dokumen berurutan (`26-100-000002`, dst)
- ✅ Laporan pembelian & penjualan tetap akurat
- ✅ Aman untuk jangka panjang (audit trail lengkap)

**Status Implementasi:** ✅ SELESAI & SIAP DIGUNAKAN

---

*Dibuat: 07 April 2026*
*Versi: 2.3 (Verified with Aronium DB)*
*Author: Smart Sembako Assistant AI*