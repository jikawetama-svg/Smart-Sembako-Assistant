# QUICK INVENTORY ENGINE

## Tujuan
Fitur `/inventory` digunakan untuk mengoreksi stok akhir produk agar sama dengan stok fisik terbaru di lapangan.

Rule utama:
- `Stock.Quantity` = sumber kebenaran stok saat ini
- `DocumentItem` = jejak dokumen inventory
- input `/inventory` = stok akhir yang diinginkan, bukan qty tambahan

## Mekanisme Final

### Alur bot
1. User kirim `/inventory <produk> <stok_target>`
2. Bot cari produk dari data produk aktif
3. Bot baca `currentStock` dari `Stock.Quantity`
4. Bot hitung `delta = targetStock - currentStock`
5. Bot tampilkan konfirmasi:
   - Dari: `currentStock`
   - Ke: `targetStock`
   - Selisih: `delta`
6. Setelah `/confirm`, sistem membuat dokumen Inventory Count dan update `Stock.Quantity`

### Sumber data stok
- Semua pembacaan stok operasional harus mengacu ke warehouse aktif yang sama
- Prioritas warehouse:
  - `Warehouse.Id = 1`
  - fallback ke warehouse pertama berdasarkan `ORDER BY Id LIMIT 1`
- Query produk/stok yang join ke tabel `Stock` harus memakai filter warehouse yang sama

## Format Data Inventory di Database

### Document
- `DocumentTypeId = 3`
- Nomor dokumen mengikuti format Aronium `YY-300-NNNNNN`
- `InternalNote` tetap `Quick inventory ...`

### DocumentItem
Untuk inventory final yang sudah cocok dengan UI Aronium:
- `Quantity = target stock`
- `ExpectedQuantity = stok sebelum inventory`
- `delta = Quantity - ExpectedQuantity`

Contoh:
- stok sebelum `105`
- target stock `107`
- yang disimpan:
  - `Quantity = 107`
  - `ExpectedQuantity = 105`
  - delta bot = `+2`

Contoh turun:
- stok sebelum `455`
- target stock `105`
- yang disimpan:
  - `Quantity = 105`
  - `ExpectedQuantity = 455`
  - delta bot = `-350`

### Stock
- Setelah insert dokumen, `Stock.Quantity` harus di-set langsung ke `targetStock`

## Kontrak Implementasi

### Read once, write once
Di dalam transaction inventory:
1. baca `Stock.Quantity`
2. hitung `targetStock` dan `delta`
3. insert `DocumentItem`
4. update `Stock.Quantity`
5. commit

Jangan:
- hitung stok sekarang dari `SUM(DocumentItem.Quantity)`
- baca ulang stok dari source lain untuk pesan sukses
- join `Stock` tanpa filter warehouse

### RestockResult
Hasil inventory harus membawa:
- `Success`
- `DocumentId`
- `DocumentNumber`
- `OldStock`
- `NewStock`
- `Total`
- `Error`

`OldStock` dipakai ulang oleh bot untuk:
- pesan sukses
- inventory log
- konsistensi antara angka konfirmasi dan angka dokumen

## Perilaku Bot

### Konfirmasi
Format pesan:

```text
KONFIRMASI INVENTORY
Produk : Sasa 1000
Dari   : 105 Pcs
Ke     : 107 Pcs
Selisih: +2 Pcs
```

### Sukses
Format pesan:

```text
INVENTORY SELESAI
Dokumen : 26-300-000098
Produk  : Sasa 1000
Stok    : 105 -> 107 Pcs
Selisih : +2 Pcs
```

### Riwayat inventory bot
Riwayat inventory yang ditampilkan bot harus membaca:
- `QuantityChange = DocumentItem.Quantity - DocumentItem.ExpectedQuantity`

Bot tidak boleh menampilkan kolom `Quantity` mentah sebagai selisih, karena untuk inventory final kolom itu menyimpan stok target.

## Logging Diagnostik
Sebelum insert `DocumentItem`, log inventory harus memuat:
- `ProductId`
- `WarehouseId`
- `StockQuantity`
- `Target`
- `StoredQuantity`
- `Delta`

Contoh:

```text
[Inventory] ProductId=738 WarehouseId=1 StockQuantity=105 Target=107 StoredQuantity=107 Delta=2
```

## Yang Tidak Boleh Diubah
- format nomor dokumen
- `InternalNote`
- sistem `/confirm` dan `/cancel`
- mekanisme `UpdateOrInsertStockAsync`
- histori lama yang sudah terlanjur salah, kecuali dibersihkan manual

## Checklist Verifikasi
- `/stok Sasa 1000` menampilkan stok dari `Stock.Quantity`
- `/inventory Sasa 1000 107` menampilkan `Dari 105 -> Ke 107`
- sesudah `/confirm`:
  - `Stock.Quantity = 107`
  - `DocumentItem.Quantity = 107`
  - `DocumentItem.ExpectedQuantity = 105`
  - delta bot = `+2`
- riwayat inventory bot menampilkan `+2`, bukan `107`
- semua query stok utama memakai warehouse aktif yang sama

## Catatan Penting
- `SUM(DocumentItem)` tidak dipakai lagi sebagai stok operasional
- UI Aronium bisa membingungkan, jadi dokumentasi ini menjadi sumber kebenaran implementasi inventory di SSA
- Jika histori lama sudah salah, perbaikannya dilakukan manual per dokumen, bukan dengan mengubah rule engine baru

---

Status: final, sesuai implementasi inventory terbaru.
