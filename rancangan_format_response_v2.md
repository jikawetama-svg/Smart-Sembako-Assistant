# 🎨 Rancangan Format Response Bot v2 — Perbaikan Lanjutan

> **Status:** Rancangan saja — belum ada perubahan kode.
> Fokus pada **yang masih kurang** dari percakapan 02/05/2026 06:17-06:25.

---

## ✅ Sudah Sempurna (tidak perlu diubah)

| Command | Status |
|---|---|
| `/start` | ✅ |
| `/stok [nama]` | ✅ |
| `/laporan` | ✅ |
| `/analisa` | ✅ |
| `/dead_stock` | ✅ |
| `/penjualan` | ✅ |
| `/restock` single (konfirmasi + hasil) | ✅ |
| `/restock` bulk (konfirmasi + hasil) | ✅ |
| `/inventory` single (konfirmasi + hasil + no-change) | ✅ |
| `/inventory` bulk (konfirmasi + hasil) | ✅ |
| `/dokumen` | ✅ |
| `/cek_modal` | ✅ |
| `/pelanggan` | ✅ |
| `/user` | ✅ |
| `/laporan_kasir` | ✅ |

---

## 🔧 Yang Masih Perlu Diperbaiki

---

### 1. `/stok` (tanpa argumen) — Minor improvement

#### 🔶 Sekarang
```
📦 STOK RENDAH

  🔴 Roti@2000              -6560 Pcs  Rp 1,750
  🔴 Sedap kuah All variant -2478.5 Pcs  Rp 3,000
  ...
  🔴 Sukro bledug           -1087 Pcs  Rp 1,850
```

#### ✅ Usulan
```
📦 STOK RENDAH (top 10)

  🔴 Roti@2000              -6,560  Pcs  Rp 1,750
  🔴 Sedap kuah All variant -2,478  Pcs  Rp 3,000
  🔴 RCNG500                -2,323  Rcg  Rp 4,500
  🔴 Sedap goreng all       -1,954  Pcs  Rp 3,200
  🔴 Nabati@2000            -1,842  Pcs  Rp 1,750
  🔴 Seduhan                -1,452  Rcg  Rp 3,300
  🔴 Tarigu curah           -1,290  Kg   Rp 8,000
  🔴 Sasa 1000              -1,108  Pcs  Rp 950
  🔴 RCNG@1000              -1,095  Pcs  Rp 9,000
  🔴 Sukro bledug           -1,087  Pcs  Rp 1,850

⚠️ Banyak stok minus besar — lakukan /inventory untuk koreksi.
Atau: /stok [nama] untuk cek produk spesifik.
```

**Perubahan:**
- Angka negatif besar → format ribuan dengan koma (lebih mudah dibaca)
- Tambah "top 10" di judul agar user tahu ada lebih banyak
- Tambah hint aksi di bawah (terutama stok minus besar = perlu koreksi)

---

### 2. `/laporan` — Saat omzet Rp 0

#### 🔶 Sekarang
```
📊 LAPORAN HARI INI - 02/05/2026

  💰 Omzet    : Rp 0
  📈 Profit   : Rp 0
  🧾 Transaksi: 0

📋 Transaksi Terakhir:
  23/04 | #000075 | Rp 55,000
```

#### ✅ Usulan
```
📊 LAPORAN HARI INI - 02/05/2026

  💰 Omzet    : Rp 0
  📈 Profit   : Rp 0
  🧾 Transaksi: 0 (belum ada penjualan hari ini)

📋 Transaksi Terakhir:
  23/04 | #000075 | Rp 55,000
  23/04 | #000074 | Rp 44,000
  10/04 | #000073 | Rp 11,000
```

**Perubahan:** Tambah `(belum ada penjualan hari ini)` agar lebih informatif, bukan cuma angka nol.

---

### 3. `/analisa` — Saat ada produk terlaris

#### 🔶 Sekarang (saat ada penjualan)
```
📊 ANALISA BISNIS

  🗓️ Hari ini  : Rp X omzet | Rp X profit
  🗓️ Kemarin   : Rp X omzet
  🗓️ 7 hari    : Rp X omzet | Rp X profit

⚠️ Stok Kritis (top 3):
  ...
```

#### ✅ Usulan (tambahkan section produk terlaris jika ada data)
```
📊 ANALISA BISNIS

  🗓️ Hari ini  : Rp X omzet | Rp X profit
  🗓️ Kemarin   : Rp X omzet
  🗓️ 7 hari    : Rp X omzet | Rp X profit

🏆 Produk Terlaris (7 hari):
  1. Kapal Api mix     — 12 Rcg  Rp 204,000
  2. Aqua @600ml       —  8 Pcs  Rp 36,000
  3. Indomie goreng    —  6 Bks  Rp 18,000

⚠️ Stok Kritis (top 3):
  🔴 Roti@2000         -6,560 Pcs
  🔴 Sedap kuah All    -2,478 Pcs
  🔴 RCNG500           -2,323 Rcg

🗃️ Dead stock: 20 produk
   Ketik /dead_stock untuk detail
```

**Perubahan:** Tambahkan section "Produk Terlaris" sebelum stok kritis (sudah ada datanya di `GetTopSellingProductsAsync` — tinggal tampilkan). Jika tidak ada penjualan, section ini disembunyikan.

---

### 4. `/riwayat_restock` — Belum diupdate

#### 🔶 Sekarang (masih format lama)
```
Riwayat restock Kapal Api mix:
- 02/05/26 | 10 | Rp 16,066 | Rp 160,660
- 02/05/26 | 10 | Rp 16,066 | Rp 160,660
- 30/04/26 | 10 | Rp 16,066 | Rp 160,660
```

#### ✅ Usulan
```
📦 RIWAYAT RESTOCK — Kapal Api mix

  Tgl      Qty   Modal/unit    Total
  02/05    10 Rcg @ Rp 16,066  = Rp 160,660
  02/05    10 Rcg @ Rp 16,066  = Rp 160,660
  30/04    10 Rcg @ Rp 16,066  = Rp 160,660
  30/04    10 Rcg @ Rp 16,066  = Rp 160,660
  11/04    10 Rcg @ Rp 16,066  = Rp 160,660
  10/04    50 Rcg @ Rp 0       = Rp 0  ⚠️
  09/04    50 Rcg @ Rp 16,066  = Rp 803,300
  09/04    50 Rcg @ Rp 16,066  = Rp 803,300
  ...

Total restock ditampilkan: 10 entri
```

**Perubahan:**
- Emoji header
- Format angka lebih rapi
- ⚠️ flag untuk restock dengan harga Rp 0 (kemungkinan data tidak lengkap)

---

### 5. `/riwayat_inventory` — Belum diupdate

#### 🔶 Sekarang (masih format lama)
```
Riwayat inventory Kapal Api mix:
- 02/05/26 | +8
- 02/05/26 | 0
- 02/05/26 | +239
- 02/05/26 | -20
```

#### ✅ Usulan
```
🔄 RIWAYAT INVENTORY — Kapal Api mix

  02/05  ⬆️  +239 Rcg
  02/05  ⬇️   -20 Rcg
  02/05  ⬆️    +8 Rcg
  02/05  ➡️     0      (tidak berubah)
  01/05  ⬇️    -6 Rcg
  30/04  ⬇️   -59 Rcg
  30/04  ⬇️   -15 Rcg
  30/04  ⬆️   +10 Rcg
  30/04  ⬇️    -5 Rcg
  30/04  ⬇️   -22 Rcg

Total ditampilkan: 10 entri
```

**Perubahan:**
- ⬆️⬇️➡️ arrow untuk arah perubahan
- Tampilkan satuan (Rcg/Pcs)
- "0" ditampilkan sebagai "tidak berubah" agar lebih jelas

---

### 6. `/notifikasi_stok` — Belum diupdate

#### 🔶 Sekarang (masih format lama)
```
Notifikasi stok kritis:
- ASAM JAWA.: Habis 0 Dus
- Ager jaring: Habis 0 Pcs
- Akusuka Micado: Habis 0 lmbr
```

#### ✅ Usulan
```
🚨 STOK KRITIS

  🔴 ASAM JAWA.              0 Dus
  🔴 Ager jaring             0 Pcs
  🔴 Akusuka Micado          0 lmbr
  🔴 Astaga Super Stick      0 Dus
  🔴 Astor Singles 250g      0 Pak
  🔴 Baby happy@XL           0 Rcg
  🔴 Blaster Choco Mint      0 Pak
  🔴 CLEAR                   0 Rcg
  🔴 Cemilan1000@10          0 Rcg
  🔴 Cemilan500@10           0 Rcg

Gunakan /restock atau /inventory untuk memperbarui.
```

---

### 7. `/rekomendasi_restock` — Belum diupdate

#### 🔶 Sekarang (masih format lama)
```
Rekomendasi restock
- Tidak ada item dengan histori penjualan yang perlu restock sekarang.

Perlu review manual:
- Ager jaring: stok 0 Pcs | belum ada histori penjualan 30 hari
- Akusuka Micado: stok 0 lmbr | belum ada histori penjualan 30 hari
```

#### ✅ Usulan
```
🤖 REKOMENDASI RESTOCK

✅ Tidak ada item urgent berdasarkan histori penjualan.

👀 Perlu Review Manual (stok 0, belum ada penjualan 30 hari):
  • Ager jaring              0 Pcs
  • Akusuka Micado           0 lmbr
  • ASAM JAWA.               0 Dus
  • Astaga Super Stick       0 Dus
  • Astor Singles 250g       0 Pak
  • Baby happy@XL            0 Rcg
  • Blaster Choco Mint       0 Pak
  • Cemilan@2000             0 Rcg
  • Cemilan1000@10           0 Rcg
  • Cemilan500@10            0 Rcg

Gunakan /restock [nama] [qty] untuk restock item di atas.
```

---

### 8. `/supplier` — Belum terlihat, kemungkinan masih format lama

#### 🔶 Sekarang (perkiraan)
```
Hasil supplier: owi
- owi | HP: - | Email: -
```

#### ✅ Usulan (sama dengan /pelanggan)
```
🏭 SUPPLIER - "owi"

  owi
  📱 HP    : -
  📧 Email : -
```

---

### 9. `/cek_modal` — Minor improvement

#### 🔶 Sekarang
```
⚠️ PRODUK TANPA HARGA MODAL

  2B PENCIL                  jual Rp 12,000
  ...
  AQUVIVA@250ML              jual Rp 800

Profit tidak bisa dihitung untuk produk di atas.
Update harga modal di aplikasi Aronium.
```

#### ✅ Usulan
```
⚠️ PRODUK TANPA HARGA MODAL (15 dari X total)

  2B PENCIL                  jual Rp 12,000
  ...
  AQUVIVA@250ML              jual Rp 800

⚠️ Profit tidak bisa dihitung untuk produk di atas.
Update harga modal di aplikasi Aronium.
```

**Perubahan:** Tambahkan total count `(15 dari X total)` sehingga user tahu ada berapa banyak produk tanpa modal.

---

## 📌 Ringkasan Prioritas

| # | Command | Status | Prioritas |
|---|---|---|---|
| 1 | `/riwayat_restock` | Belum update | 🔴 Tinggi |
| 2 | `/riwayat_inventory` | Belum update | 🔴 Tinggi |
| 3 | `/notifikasi_stok` | Belum update | 🔴 Tinggi |
| 4 | `/rekomendasi_restock` | Belum update | 🔴 Tinggi |
| 5 | `/supplier` | Belum terverifikasi | 🟡 Sedang |
| 6 | `/analisa` produk terlaris | Improvement | 🟡 Sedang |
| 7 | `/stok` format angka + hint | Minor | 🟢 Rendah |
| 8 | `/laporan` saat omzet 0 | Minor | 🟢 Rendah |
| 9 | `/cek_modal` total count | Minor | 🟢 Rendah |

---

> **Langkah selanjutnya:** Setujui mana yang ingin diimplementasikan, lalu saya edit kodenya.
