# 🎨 Rancangan Format Response Bot Smart Sembako

> **Status:** Rancangan saja — belum ada perubahan kode.
> Semua format di bawah ini adalah **usulan** untuk disetujui sebelum diimplementasikan.

---

## Prinsip Desain

| Prinsip | Penerapan |
|---|---|
| **Hierarki visual** | Judul → isi → catatan → aksi |
| **Emoji sebagai ikon** | Konsisten: 📦 pembelian, 🔄 inventory, 📊 laporan, dll |
| **Ringkas tapi lengkap** | Tidak ada teks redundan |
| **Aksi jelas** | Selalu akhiri dengan instruksi berikutnya |

---

## 1. `/start` & `/help`

### ❌ Sekarang
```
Bantuan Smart Sembako Assistant
/stok [nama] - cek stok
/laporan - laporan hari ini
...
Bulk: pisahkan beberapa item dengan koma pada /restock atau /inventory
Chat natural juga didukung. Contoh: "stok beras berapa?"
```

### ✅ Usulan
```
🏪 Smart Sembako Assistant

📦 STOK & LAPORAN
/stok [nama] — cek stok produk
/laporan — omzet & profit hari ini
/analisa — analisa bisnis mingguan
/notifikasi_stok — stok kritis

🛒 PEMBELIAN & KOREKSI
/restock <produk> <qty> [harga] — tambah stok masuk
/inventory <produk> <target> — set stok akhir (koreksi)
↳ Bulk: pisahkan item dengan koma

📋 DATA
/penjualan <produk> — ringkasan penjualan
/dokumen <nomor> — detail dokumen
/riwayat_restock <produk> — histori restock
/riwayat_inventory <produk> — histori koreksi
/rekomendasi_restock — saran restock otomatis
/dead_stock — barang tidak laku >14 hari
/cek_modal — produk tanpa harga modal

👥 MASTER DATA
/pelanggan [nama] — cari pelanggan
/supplier [nama] — cari supplier
/user [nama] — cari user kasir
/laporan_kasir — performa kasir

⚙️ AKSI
/confirm — konfirmasi aksi menunggu
/cancel — batalkan aksi menunggu

💬 Chat natural juga didukung. Contoh: "stok beras berapa?"
```

---

## 2. `/laporan`

### ❌ Sekarang
```
Laporan hari ini
- Omzet: Rp 0
- Profit: Rp 0
- Jumlah transaksi: 0

Transaksi terakhir:
- 23/04/2026 00:00 | 000075 | Rp 55,000
```

### ✅ Usulan
```
📊 LAPORAN HARI INI — 02/05/2026

💰 Omzet    : Rp 0
📈 Profit   : Rp 0
🧾 Transaksi: 0

📋 Transaksi Terakhir:
  23/04 | #000075 | Rp 55,000
  23/04 | #000074 | Rp 44,000
  10/04 | #000073 | Rp 11,000
```

---

## 3. `/restock` — Konfirmasi (single)

### ❌ Sekarang
```
Konfirmasi restock:
- Produk: Kapal Api mix
- Qty: 10
- Harga modal: Rp 16,066

Kirim /confirm untuk lanjut atau /cancel untuk batal.
```

### ✅ Usulan
```
📦 KONFIRMASI RESTOCK

  Produk : Kapal Api mix
  Qty    : 10 Rcg
  Modal  : Rp 16,066/pcs
  Total  : Rp 160,660

✅ /confirm  |  ❌ /cancel
```

---

## 4. `/restock` — Hasil (single)

### ❌ Sekarang
```
Restock berhasil.
Dokumen: 26-100-000089
Produk: Kapal Api mix
Qty: 10.0
Total: Rp 160,660
```

### ✅ Usulan
```
✅ RESTOCK BERHASIL

📄 Dokumen : 26-100-000089
📦 Produk  : Kapal Api mix
📊 Qty     : 10 Rcg
💰 Total   : Rp 160,660
```

---

## 5. `/restock` — Konfirmasi Bulk

### ❌ Sekarang
```
Konfirmasi bulk restock (1 dokumen):
- Kapal Api mix: 10 Rcg @ Rp 16,066
- 2B PENCIL: 10 Pcs @ Rp 0
...

⚠️ Item berikut tidak diproses:
⚠️ Produk "ABC susu" ambigu untuk restock.
...— dilewati.

Kirim /confirm untuk lanjut atau /cancel untuk batal.
```

### ✅ Usulan
```
📦 BULK RESTOCK — 1 DOKUMEN (8 item)

  Kapal Api mix      10 Rcg  @ Rp 16,066
  2B PENCIL          10 Pcs  @ Rp 0
  APETITO            20 Pak  @ Rp 0
  76 apel 1pk        20 Pcs  @ Rp 137,000
  ALE-ALE @Dus        2 Dus  @ Rp 18,966
  Kapal api mix@1dus 10 Pcs  @ Rp 192,800
  AHH NABATI        300 Pak  @ Rp 8,240
  ANTAKA.            80 Pcs  @ Rp 4,900

💰 Estimasi total: Rp 4,767,820

⚠️ Dilewati (perlu perjelas):
  • "ABC susu" — ambigu, ada 3 kandidat

✅ /confirm  |  ❌ /cancel
```

> **Catatan:** Estimasi total dihitung dari qty × harga modal semua item.

---

## 6. `/restock` — Hasil Bulk

### ❌ Sekarang
```
Bulk restock selesai: 8/8 produk berhasil.
Dokumen: 26-100-000088
- Kapal Api mix: 10 Rcg @ Rp 16,066
- 2B PENCIL: 10 Pcs @ Rp 0
...
```

### ✅ Usulan
```
✅ BULK RESTOCK SELESAI

📄 Dokumen : 26-100-000088
✔️ Berhasil: 8/8 produk

  Kapal Api mix      10 Rcg
  2B PENCIL          10 Pcs
  APETITO            20 Pak
  76 apel 1pk        20 Pcs
  ALE-ALE @Dus        2 Dus
  Kapal api mix@1dus 10 Pcs
  AHH NABATI        300 Pak
  ANTAKA.            80 Pcs
```

---

## 7. `/inventory` — Konfirmasi Bulk

### ❌ Sekarang
```
📦 KONFIRMASI INVENTORY BULK

📋 Detail:
Aksi ini akan membuat 1 dokumen inventory untuk semua item di bawah.

• Kapal Api mix: 30 -> 10 Rcg (-20)
• 2B PENCIL: 30 -> 10 Pcs (-20)
...

ℹ️ Dilewati (stok sudah sesuai):
↔️ 2B PENCIL: stok sudah 10 Pcs, tidak ada perubahan.
...

📝 Inventory akan SET stok akhir per produk, bukan menambah stok seperti restock.
⚠️ Peringatan: ada perubahan stok yang cukup besar. Pastikan target sudah benar.

Kirim /confirm untuk lanjut atau /cancel untuk batal.
```

### ✅ Usulan
```
🔄 BULK INVENTORY — 1 DOKUMEN (9 item)

Produk akan di-SET ke stok berikut:

  Kapal Api mix      30 → 10 Rcg   (-20)
  2B PENCIL          30 → 10 Pcs   (-20)
  APETITO            60 → 20 Pak   (-40)
  76 apel 1pk        60 → 20 Pcs   (-40)
  ALE-ALE @Dus        6 →  2 Dus    (-4)
  Kapal api mix@1dus 30 → 10 Pcs   (-20)
  AHH NABATI        900 → 300 Pak (-600)
  Kopi ABC Susu      20 → 10 Rcg   (-10)
  ANTAKA.           240 → 80 Pcs  (-160)

⚠️ Perubahan besar terdeteksi. Pastikan target benar!

✅ /confirm  |  ❌ /cancel
```

> Jika ada skip: tambahkan blok `ℹ️ Tidak berubah (target = stok saat ini): ...` di bawah tabel.

---

## 8. `/inventory` — Hasil Bulk

### ❌ Sekarang
```
Bulk inventory selesai: 9/9 produk berhasil.
Dokumen: 26-300-000093
- Kapal Api mix: 30 -> 10 Rcg (-20)
...
```

### ✅ Usulan
```
✅ BULK INVENTORY SELESAI

📄 Dokumen : 26-300-000093
✔️ Berhasil: 9/9 produk

  Kapal Api mix       → 10 Rcg
  2B PENCIL           → 10 Pcs
  APETITO             → 20 Pak
  76 apel 1pk         → 20 Pcs
  ALE-ALE @Dus        →  2 Dus
  Kapal api mix@1dus  → 10 Pcs
  AHH NABATI          → 300 Pak
  Kopi ABC Susu       → 10 Rcg
  ANTAKA.             → 80 Pcs
```

---

## 9. `/stok`

### ❌ Sekarang
```
Hasil pencarian stok untuk "kapal api mix":
LOW Kapal Api mix: 10 Rcg | Jual Rp 17,000
OK Kapal Api 60g @Pcs: 20  | Jual Rp 9,500
OUT KAPAL API SILVER 120G: 0 PCS | Jual Rp 14,000
```

### ✅ Usulan
```
🔍 Stok "kapal api mix":

🟡 Kapal Api mix       10 Rcg  Rp 17,000
🟡 Kapal api mix@1dus  10      Rp 195,000
🔴 Coffe Candy Kapal    0 Pcs  Rp 7,000
🟢 Kapal Api 60g @Pcs  20      Rp 9,500
🔴 KAPAL API SILVER   0 PCS   Rp 14,000
```

> Legend: 🟢 OK | 🟡 LOW | 🔴 OUT/MINUS

---

## 10. `/analisa`

### ❌ Sekarang
```
Analisa bisnis
Hari ini: omzet Rp 0 | profit Rp 0
Kemarin: omzet Rp 0
7 hari terakhir: omzet Rp 0 | profit Rp 0

Stok rendah:
- Roti@2000: -6560 Pcs

Dead stock: 20 produk
```

### ✅ Usulan
```
📊 ANALISA BISNIS

📅 Hari ini  : Rp 0 omzet | Rp 0 profit
📅 Kemarin   : Rp 0 omzet
📅 7 hari    : Rp 0 omzet | Rp 0 profit

⚠️ Stok Kritis (top 3):
  🔴 Roti@2000          -6,560 Pcs
  🔴 Sedap kuah All   -2,478.5 Pcs
  🔴 RCNG500           -2,323 Rcg

🗂️ Dead stock: 20 produk
   Ketik /dead_stock untuk detail
```

---

## 11. `/penjualan`

### ❌ Sekarang
```
Penjualan Kapal Api mix
- Qty terjual: 1 Rcg
- Revenue: Rp 17,000
- Profit: Rp 934
- Penjualan terakhir: 06/04/2026

Transaksi terakhir:
- 06/04/2026 | 000068 | Qty 1 | Rp 17,000 | Walk-in customer
```

### ✅ Usulan
```
📈 PENJUALAN — Kapal Api mix

  📦 Qty terjual : 1 Rcg
  💰 Revenue     : Rp 17,000
  📊 Profit      : Rp 934
  🗓️ Terakhir    : 06/04/2026

Transaksi terakhir:
  06/04 | #000068 | 1 Rcg | Rp 17,000 | Walk-in
```

---

## 12. `/riwayat_restock`

### ❌ Sekarang
```
Riwayat restock Kapal Api mix:
- 02/05/26 | 10 | Rp 16,066 | Rp 160,660
- 30/04/26 | 10 | Rp 16,066 | Rp 160,660
```

### ✅ Usulan
```
📦 RIWAYAT RESTOCK — Kapal Api mix

  02/05  10 Rcg  @ Rp 16,066  = Rp 160,660
  02/05  10 Rcg  @ Rp 16,066  = Rp 160,660
  30/04  10 Rcg  @ Rp 16,066  = Rp 160,660
  30/04  10 Rcg  @ Rp 16,066  = Rp 160,660
  11/04  10 Rcg  @ Rp 16,066  = Rp 160,660
  10/04  50 Rcg  @ Rp 0       = Rp 0
  09/04  50 Rcg  @ Rp 16,066  = Rp 803,300
  ...
```

---

## 13. `/riwayat_inventory`

### ❌ Sekarang
```
Riwayat inventory Kapal Api mix:
- 02/05/26 | +8
- 02/05/26 | 0
- 02/05/26 | +239
```

### ✅ Usulan
```
🔄 RIWAYAT INVENTORY — Kapal Api mix

  02/05  ⬆️ +239   (koreksi naik)
  02/05  ⬇️  -20   (koreksi turun)
  02/05  ⬆️   +8   (koreksi naik)
  02/05  ➡️    0   (tidak berubah)
  01/05  ⬇️   -6
  30/04  ⬇️  -59
  30/04  ⬇️  -15
  30/04  ⬆️  +10
  30/04  ⬇️   -5
  30/04  ⬇️  -22
```

---

## 14. `/rekomendasi_restock`

### ❌ Sekarang
```
Rekomendasi restock
- Tidak ada item dengan histori penjualan yang perlu restock sekarang.

Perlu review manual:
- Ager jaring: stok 0 Pcs | belum ada histori penjualan 30 hari
```

### ✅ Usulan
```
🤖 REKOMENDASI RESTOCK

✅ Tidak ada item urgent berdasarkan penjualan.

👀 Perlu Review Manual (stok 0, belum ada penjualan):
  • Ager jaring         0 Pcs
  • Akusuka Micado      0 lmbr
  • ASAM JAWA.          0 Dus
  • Astaga Super Stick  0 Dus
  • Astor Singles       0 Pak
  • Baby happy@XL       0 Rcg
  • Blaster Choco Mint  0 Pak
  • Cemilan@2000        0 Rcg
  • Cemilan1000@10      0 Rcg
  • Cemilan500@10       0 Rcg
```

---

## 15. `/notifikasi_stok`

### ❌ Sekarang
```
Notifikasi stok kritis:
- ASAM JAWA.: Habis 0 Dus
- Ager jaring: Habis 0 Pcs
```

### ✅ Usulan
```
🚨 STOK KRITIS

  🔴 ASAM JAWA.            0 Dus
  🔴 Ager jaring           0 Pcs
  🔴 Akusuka Micado        0 lmbr
  🔴 Astaga Super Stick    0 Dus
  🔴 Astor Singles 250g    0 Pak
  🔴 Baby happy@XL         0 Rcg
  🔴 Blaster Choco Mint    0 Pak
  🔴 CLEAR                 0 Rcg
  🔴 Cemilan1000@10        0 Rcg
  🔴 Cemilan500@10         0 Rcg

Gunakan /restock atau /inventory untuk memperbarui stok.
```

---

## 16. `/cek_modal`

### ❌ Sekarang
```
Produk tanpa harga modal:
- 2B PENCIL: jual Rp 12,000
- ABC KLEPON: jual Rp 17,500
```

### ✅ Usulan
```
⚠️ PRODUK TANPA HARGA MODAL

  2B PENCIL                jual Rp 12,000
  ABC KLEPON               jual Rp 17,500
  ABC SUSU 1 DUS           jual Rp 195,000
  ABC Sambal Asli @10S     jual Rp 6,000
  AIDA BUBUK               jual Rp 23,000
  APETITO                  jual Rp 17,000
  ...

Profit tidak bisa dihitung untuk produk di atas.
Update harga modal di aplikasi Aronium.
```

---

## 17. `/laporan_kasir`

### ❌ Sekarang
```
Laporan penjualan per kasir:
- Saeful Arifin: 43 transaksi | Rp -25,210,252
```

### ✅ Usulan
```
👤 PERFORMA KASIR

  Saeful Arifin   43 trx   Rp -25,210,252 ⚠️

⚠️ Nilai negatif = ada retur/void. Cek dokumen di Aronium.
```

---

## 18. `/dead_stock`

### ❌ Sekarang
```
Dead stock (>14 hari tidak laku):
- ABC SUSU 1 DUS: -1 DUS
- ABC Sambal Asli 15g @10S: 66 Pak
```

### ✅ Usulan
```
🗂️ DEAD STOCK (>14 hari tidak terjual)

  ABC SUSU 1 DUS           -1 DUS  ⚠️
  ABC Sambal Asli @10S     66 Pak
  ABC Sambal Extra Pedas   26 Pak
  ABC Sambal Terasi        50 Pak
  AIDA BUBUK               10 Rcg
  ANTANGIN CAIR@1PK        14 Pak
  AQUA@600ML @Dus          81 Dus
  ...

Total: 20 produk | Pertimbangkan promosi atau retur ke supplier.
```

---

## 19. `/dokumen`

### ❌ Sekarang
```
Dokumen 26-100-000082
- Tipe: Pembelian
- Tanggal: 02/05/2026
- Kasir: Saeful Arifin
- Customer: Walk-in customer
- Total: Rp 2,472,000

Item:
- AHH NABATI | Qty 300 | Harga Rp 8,240 | Total Rp 2,472,000
```

### ✅ Usulan
```
📄 DOKUMEN 26-100-000082

  🏷️ Tipe     : Pembelian
  📅 Tanggal  : 02/05/2026
  👤 Kasir    : Saeful Arifin
  👥 Customer : Walk-in customer
  💰 Total    : Rp 2,472,000

Item:
  AHH NABATI   300 Pak  @ Rp 8,240  = Rp 2,472,000
```

---

## 20. `/pelanggan`, `/supplier`, `/user`

### ❌ Sekarang
```
Hasil pelanggan: owi
- owi | HP: - | Email: -

Hasil user: uum
- uum | - | Admin / level 8 | Aktif
```

### ✅ Usulan — Pelanggan
```
👥 PELANGGAN — "owi"

  owi
  📱 HP    : -
  📧 Email : -
```

### ✅ Usulan — User
```
👤 USER — "uum"

  uum (Admin Lv.8) ✅ Aktif
  Username: -
```

---

## Ringkasan Perubahan

| Area | Perubahan |
|---|---|
| Header setiap response | Emoji + judul kapital |
| Tabel item | Rata kiri dengan spasi, lebih mudah dibaca |
| Status stok | 🟢🟡🔴 konsisten di semua tempat |
| Riwayat inventory | ⬆️⬇️➡️ untuk arah perubahan |
| Konfirmasi | Format `✅ /confirm \| ❌ /cancel` |
| Estimasi total | Ditambahkan di konfirmasi bulk restock |
| Pesan error/skip | Lebih human-friendly, tidak terlalu teknis |
| Dead stock | Tambah saran aksi di bawah |
| Laporan kasir | Flag ⚠️ jika nilai negatif |

---

> **Langkah selanjutnya:** Setujui format mana yang ingin diimplementasikan, lalu saya akan edit kodenya.
