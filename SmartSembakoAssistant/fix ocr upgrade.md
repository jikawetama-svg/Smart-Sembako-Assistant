# Audit & Fix Plan — OCR Pipeline Smart Sembako Assistant

## Ringkasan Masalah
Sistem OCR sudah jalan (pipeline Tesseract → Groq AI → FuzzyMatch → DB), tapi masih ada bug pada:
1. Baca nama produk (kadang benar, kadang salah)
2. Quantity / satuan / total per produk tidak akurat
3. Tanggal struk tidak ter-parse
4. Nama supplier / toko keliru
5. Format struk berbeda tiap vendor → parser tidak adaptif per layout

---

## 📸 Mapping Layout Per Struk (Receipt Structure Map)

### Struk A — Wings Food (Surat Jalan)
> Gambar: `56474950-a625-476a-b9af-7ee73ffbc262.jpg`

```
Header: WINGS / PT. SAYUP ANAS DERMA
Jenis: SURAT JALAN
No: JBM0000929
Tanggal: 30.03.2026 (pojok kanan atas)
Pembeli: TK ASIAH

Kolom tabel:
No | QTY KODE | NAMA BARANG     | SEQ | ISI | HARGA | JMLH.KOTOR | CUST.DISC | PROD.DISC | JMLH.BERSIH

Contoh baris:
12 | 5 BOX 20050 | Sedaap Mie Bag 69Gr Ayam Spesial | | 40 | 99.400 | 497.000 | 3.728 | 3.000 | ...
13 | 2 BOX 20092 | Sedaap Mie Bag 77Gr Baso Spesial | | 40 | 108.380 | 216.760 | ... | ... |

Ciri khas:
- QTY dan satuan (BOX/PCS) ada di kolom ke-2 bersama kode produk
- Nama produk di kolom tersendiri, isi seperti "Sedaap Mie Bag 69Gr Ayam Spesial"
- Harga = harga per isi (per pcs), bukan per box
- Ada SEQ ISI = jumlah pcs per box
- Total = JMLH.BERSIH (setelah diskon)
- NAMA SUPPLIER: Baris "PT. SAYUP ANAS DERMA" / "WINGS" (header)
- TANGGAL: Format DD.MM.YYYY di pojok kanan atas ("30.03.2026")
```

**Bug utama pada Struk A:**
- Supplier terbaca jadi nama customer (TK ASIAH) bukan WINGS
- Qty terbaca "5" (BOX) padahal harus dikalikan ISI (40 pcs) atau dibiarkan "5 BOX"
- Harga yang diambil kadang `99.400` (per pcs) bukan `497.000` (total per baris)
- Kolom SEQ ISI dianggap kolom Qty oleh AI → salah

---

### Struk B — Tani Makmur Putra (Struk Kasir POS)
> Gambar: `d1c431f7-8acc-444a-b154-f1b1824f81f0.jpg`

```
Header: TANI MAKMUR PUTRA
Alamat: Jln Pasar Palumbon
Tanggal: Wed,06-May-2026,17:27:17
Kasir: Acun | Faktur No: 45906
Nama Pelanggan: Teh Aisah

Kolom tabel:
Produk | Harga | Qty | Sat | Total

Contoh baris:
Kapal api mix 1Ds    197000   2  dus   394000
Gula pasir 1Bal               1  bal   875000

Footer:
SubTotal    1269000
Kasbon      1084500
Total       2353500

Ciri khas:
- Format: [Nama Produk] [Harga Satuan] [Qty] [Satuan] [Total]
- Ada variasi: kalau harga = 0, diisi "-" atau kosong (contoh Gula Pasir)
- NAMA SUPPLIER = "TANI MAKMUR PUTRA" (baris pertama)
- TANGGAL = baris ke-2 "Wed,06-May-2026,17:27:17" → format non-standar
- Kasbon = piutang/pinjaman, BUKAN produk → harus di-skip
```

**Bug utama pada Struk B:**
- "Tani Makmur Putra" kadang dibaca sebagai pelanggan, bukan supplier
- Kasbon (1.084.500) ikut terparsing sebagai produk
- Tanggal "Wed,06-May-2026" tidak ter-parse karena format tidak standar ISO
- Gula pasir: harga satuan kosong → perlu fallback `total / qty`
- Satuan "dus" dan "bal" kadang dianggap nama produk

---

### Struk C — PT. Fastrata Buana (Faktur Penjualan Tercetak)
> Gambar: `87b373be-c344-4670-9dab-15b00de65819.jpg`

```
Header: PT. FASTRATA BUANA
Jenis: FAKTUR PENJUALAN
Nomor Faktur: 460-0994023
Tanggal: 08 May 2026 (di tabel kanan atas)
Pelanggan: TOKO AISAH

Kolom tabel:
No | Nama Barang | Quantity | Harga Incl. PPN | Promo | Extra | Regular | Jumlah Setelah Potongan

Contoh baris:
1 | 00J.KPSPM.G0233101XX SP MIX (RTG,12X10X23 GR)  | 10Box  | 200,000 | 0 | 0 | 36,000 | 1,964,000.00
2 | 00J.KPGMO.G0203101V1 GD MOCACINNO (RTG,12X10X20 GR) NB | 5Box | 170,000 | 8,500 | 0 | 15,147 | 826,353.00
3 | 00J.KPASU.G0303101XX ABC SUSU (RTG,12X10X30 GR) | 2Box | 200,000 | 6,680 | 0 | 7,079 | 386,241.00
4 | 00J.KPKAO.G0233101XX KA ONE GL PISAH (RTG,12X10X23 GR) | 10Bks | 1,355 | 0 | 0 | 0 | 13,550.00

Total Bersih: 3,183,072.00

Ciri khas:
- Nama produk dimulai dengan kode SKU panjang (00J.KPSPM.G0233101XX)
- Nama asli ada di tengah, setelah kode: "SP MIX", "GD MOCACINNO", "ABC SUSU"
- Ada tanda RTG,12X10X23 GR = info kemasan (RTG=karton, 12=pcs per layer, dst)
- Qty satuan gabung: "10Box", "2Box", "10Bks"
- Harga sudah include PPN
- Ada kolom Potongan (Regular, Promo, Extra) → harga netto = Jumlah Setelah Potongan
- NAMA SUPPLIER = "PT. FASTRATA BUANA" (header)
- TANGGAL = "08 May 2026" format bahasa Inggris
```

**Bug utama pada Struk C:**
- Nama produk ikut kode SKU (00J.KPSPM.G0233101XX) → fuzzy match gagal
- AI perlu ekstrak nama saja: "SP MIX", "GD MOCACINNO", "ABC SUSU", "KA ONE GL PISAH"
- Qty "10Box" tidak diparsing karena tidak ada spasi → qty=0 atau error
- Harga yang diambil = Harga Incl. PPN (satuan), tapi harusnya Jumlah Setelah Potongan (total baris)
- Nama supplier = "PT. FASTRATA BUANA" tapi bisa terbaca "TOKO AISAH" (nama pelanggan)

---

### Struk D — TOKO IDAN (OCR Review Queue - dari screenshot UI)
> Gambar: `WhatsApp Image 2026-05-08 at 14.45.00.jpeg`

```
Data dari OCR Review Queue:
Tanggal: 28/04/2026
Supplier: TOKO IDAN

Item yang masuk queue:
1. "3101XX KA ONE GL PISAH (RTG,12X10X23GR) .. TUBks" | Qty: 1.0 | Harga: 0
2. "3101XX ABC SUSU (RTG,12X10X30 GR)"               | Qty: 1.0 | Harga: 0 | Kandidat: abc kopi susu btl (20); ABC SUSU 1 DUS (20)
3. "3101V1 GD MOCACINNO (RTG,12X10X20 GR) NB 1701000" | Qty: 1.0 | Harga: 200,000 | Kandidat: Merries NB-S (10)
```

**Bug yang terlihat dari UI:**
- Semua qty = 1.0 (qty tidak terbaca dengan benar dari "10Box", "5Box", "2Box")
- Harga = 0 untuk sebagian besar (harga tidak ter-parse dari kolom yang benar)
- Nama produk masih menyertakan kode SKU + format kemasan (RTG,12X10X23GR)
- Kandidat fuzzy match salah: "GD MOCACINNO" cocok ke "Merries NB-S" → score rendah
- "NB 1701000" dianggap bagian nama, padahal 1701000 adalah nomor batch
- Supplier tertulis "TOKO IDAN" padahal harusnya PT. FASTRATA BUANA

---

## ✅ Yang Sudah Bagus / Berfungsi

| Komponen | Status | Catatan |
|---|---|---|
| Pipeline dasar (Foto → Tesseract → Groq → DB) | ✅ Jalan | Sudah end-to-end |
| OCR Review Queue UI | ✅ Ada | Bisa lihat item yang gagal |
| Fuzzy matching engine | ✅ Ada | Kadang suggestnya relevan (ABC SUSU) |
| ProductAlias learning | ✅ Ada | Auto-simpan alias kalau match kuat |
| Fallback Groq → Gemini | ✅ Ada | Failover tersedia |
| Hybrid tolerance (valid → simpan, error → queue) | ✅ Ada | Logika sudah benar |
| Bulk Purchase Document | ✅ Ada | Satu dokumen per struk |
| Config mapping (InvoiceName → ProductId) | ✅ Ada | Bisa di-setup via Settings |
| Groq Prompt tidak mengubah nama produk | ✅ Sudah | Rule 2-6 di prompt |

---

## ❌ Bug yang Masih Ada

| # | Bug | Root Cause | Severity |
|---|---|---|---|
| B1 | **Nama supplier salah** — terbaca nama customer/pelanggan | `store_name` diambil dari baris pertama, tapi di Fastrata baris pertama adalah nama supplier yang valid namun di struk kasir bisa jadi nama toko yang merupakan pelanggan | 🔴 Tinggi |
| B2 | **Qty tidak terbaca / =1** — khususnya "10Box", "5Box" | Groq tidak bisa split "10Box" karena tidak ada spasi | 🔴 Tinggi |
| B3 | **Harga = 0** — terutama pada struk Fastrata | AI mengambil kolom harga satuan tapi kolom yang benar adalah "Jumlah Setelah Potongan" | 🔴 Tinggi |
| B4 | **Nama produk menyertakan kode SKU** | Tidak ada instruksi di prompt untuk strip kode SKU format `XXX.XXXX.XXXXX` | 🔴 Tinggi |
| B5 | **Tanggal tidak ter-parse** | Format "Wed,06-May-2026" dan "30.03.2026" tidak dikenali `DateTime.TryParse()` | 🟡 Sedang |
| B6 | **Kasbon ikut diparse sebagai produk** | Tidak ada daftar keyword footer yang harus di-skip | 🟡 Sedang |
| B7 | **Fuzzy match salah pada nama bermakna rendah** | Nama singkat seperti "NB" memicu false match | 🟡 Sedang |
| B8 | **Satuan gabung dengan angka** tidak terdeteksi | "2dus", "1bal" tanpa spasi tidak di-normalize | 🟡 Sedang |
| B9 | **No batch/lot dianggap bagian nama** | "NB 1701000" — angka besar setelah huruf tidak distrip | 🟢 Rendah |
| B10 | **Total struk tidak cocok** — total valid items ≠ total struk | Karena banyak item gagal di-parse | 🟢 Rendah |

---

## 🛠 Mekanisme Perbaikan Per Bug

### Fix B1 — Supplier Name Logic

**Root cause:** Di `ParseReceiptDocument()` dan Groq prompt, `store_name` dipetakan ke `receipt.StoreName`.
Groq tidak tahu perbedaan "nama toko penjual" vs "nama pembeli/pelanggan".

**Fix:**
- Tambahkan field baru di JSON schema: `"supplier_name"` (siapa yang jual) dan `"buyer_name"` (siapa yang beli)
- Update prompt: *"supplier_name adalah nama perusahaan/toko yang menerbitkan faktur ini (baris header atas). buyer_name adalah nama pelanggan/pembeli."*
- Update `ParsedReceipt` model: tambah property `SupplierName` dan `BuyerName`
- Di preview & queue, gunakan `SupplierName` bukan `StoreName`

**File terdampak:** `GroqService.cs` (prompt), `AutomationEngine.cs` (model + parsing), `DatabaseService.cs` (OcrReviewQueueItem)

---

### Fix B2 — Qty Parsing "10Box", "5Box", "2Box"

**Root cause:** Groq mengirim `"quantity": "10Box"` atau `"quantity": 1` karena tidak bisa split.

**Fix (dua lapis):**
1. **Prompt enhancement:** Tambah aturan: *"Jika quantity ditulis bersambung dengan satuan tanpa spasi (contoh: '10Box', '5PCS', '2Bks'), pisahkan angka dari satuannya: quantity=10, unit='Box'."*
2. **Post-parse normalization** di C# — fungsi `NormalizeQtyUnit(string raw)`:
   ```csharp
   var match = Regex.Match(raw, @"^(\d+(?:[.,]\d+)?)\s*([A-Za-z]+)$");
   if (match.Success) {
       qty = ParseLooseDecimal(match.Groups[1].Value);
       unit = match.Groups[2].Value;
   }
   ```
   Dipanggil setelah JSON di-parse, sebelum mapping.

**File terdampak:** `GroqService.cs` (prompt), `AutomationEngine.cs` (post-parse step)

---

### Fix B3 — Harga = 0 pada Struk Multi-Kolom Diskon

**Root cause:** Groq mengambil "Harga Incl. PPN" (harga satuan) sebagai `unit_price`, tapi tidak menghitung diskon. Untuk struk Fastrata ada kolom "Jumlah Setelah Potongan" = total baris yang benar.

**Fix:**
1. **Prompt update:** Tambah aturan: *"Jika ada kolom 'Jumlah Setelah Potongan', 'Jmlh Bersih', atau 'Total Bersih' per baris, gunakan itu sebagai `total`. Jika ada kolom 'Harga' sebagai harga satuan, gunakan sebagai `unit_price`."*
2. **Fallback di C#:** Jika `unit_price=0` tapi `total > 0` dan `qty > 0`, hitung `unit_price = total / qty`
   (ini sudah ada tapi pastikan diprioritaskan dengan benar)

**File terdampak:** `GroqService.cs` (prompt), `AutomationEngine.cs` (MapReceiptItemsToBulkPendingItemsAsync sudah ada fallback, tapi perlu dipastikan qty benar dulu)

---

### Fix B4 — Strip Kode SKU dari Nama Produk (Fastrata)

**Root cause:** Nama produk di Fastrata = `"00J.KPSPM.G0233101XX SP MIX (RTG,12X10X23 GR)"`. 
Groq tidak strip kode, jadi fuzzy match gagal.

**Fix:**
1. **Prompt khusus Fastrata-style:** Tambah aturan: *"Jika nama barang diawali kode format seperti '00J.XXXX.XXXXXX' atau kode alphanumerik panjang, hapus kode tersebut dan ekstrak nama produk saja. Juga hapus bagian dalam tanda kurung seperti '(RTG,12X10X23 GR)' yang merupakan info kemasan."*
2. **Post-parse cleanup di C#:**
   ```csharp
   private static string CleanProductName(string raw) {
       // Strip leading SKU codes: 00J.KPSPM.G0233101XX
       raw = Regex.Replace(raw, @"^[\w\.]+[A-Z]{2,}\s+", "").Trim();
       // Strip packaging info in parens: (RTG,12X10X23 GR)
       raw = Regex.Replace(raw, @"\(RTG,[^\)]+\)", "").Trim();
       // Strip trailing batch numbers: NB 1701000
       raw = Regex.Replace(raw, @"\bNB\s+\d{5,}\b", "").Trim();
       return raw;
   }
   ```
   Dipanggil saat menerima item dari Groq sebelum fuzzy matching.

**File terdampak:** `GroqService.cs` (prompt), `AutomationEngine.cs` (tambah `CleanProductName()`)

---

### Fix B5 — Tanggal Non-Standar

**Root cause:** `DateTime.TryParse("Wed,06-May-2026")` dan `DateTime.TryParse("30.03.2026")` gagal karena format tidak standar.

**Fix — Fungsi `ParseFlexibleDate()` di AutomationEngine.cs:**
```csharp
private static DateTime? ParseFlexibleDate(string? raw) {
    if (string.IsNullOrWhiteSpace(raw)) return null;

    // Remove day-of-week prefix: "Wed,06-May-2026,17:27:17" → "06-May-2026"
    raw = Regex.Replace(raw, @"^[A-Za-z]+,\s*", "").Split(',')[0].Trim();

    // Try standard parse first
    if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;

    // Try DD.MM.YYYY
    if (DateTime.TryParseExact(raw, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;

    // Try DD-MMM-YYYY (06-May-2026)
    if (DateTime.TryParseExact(raw, "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d3)) return d3;

    // Try "08 May 2026"
    if (DateTime.TryParseExact(raw, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d4)) return d4;

    return null;
}
```
Ganti semua `DateTime.TryParse(dateProp.GetString(), ...)` dengan `ParseFlexibleDate()`.

**File terdampak:** `AutomationEngine.cs` (`ParseReceiptDocument()`, ~baris 3840)

---

### Fix B6 — Skip Footer Keywords (Kasbon, SubTotal, dll)

**Root cause:** Baris "Kasbon 1084500" ikut terparsing.

**Fix:**
- **Prompt:** Aturan sudah ada (aturan 1: "abaikan subtotal, total, kasbon, diskon, pajak, header, footer")
- **Tambah di C# post-parse filter:**
  ```csharp
  private static readonly HashSet<string> FooterKeywords = new(StringComparer.OrdinalIgnoreCase)
  {
      "kasbon", "subtotal", "sub total", "total", "bayar", "kembalian",
      "diskon", "ppn", "dpp", "terbilang", "keterangan", "catatan"
  };

  private static bool IsFooterItem(string name) =>
      FooterKeywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase));
  ```
  Filter di `MapReceiptItemsToBulkPendingItemsAsync()` sebelum proses mapping.

**File terdampak:** `AutomationEngine.cs`

---

### Fix B7 & B9 — Fuzzy Match Quality & Batch Number Cleanup

**Fix B7:** Tingkatkan threshold minimum fuzzy score dari 35 → 45 untuk auto-accept. Ini mencegah false match "GD MOCACINNO" → "Merries NB-S".

**Fix B9:** Sudah tercakup di `CleanProductName()` (strip `NB \d{5,}`).

**File terdampak:** `AutomationEngine.cs` (threshold di `MapReceiptItemsToBulkPendingItemsAsync`, baris ~3975)

---

### Fix B8 — Satuan Gabung Tanpa Spasi

Tercakup di Fix B2 dengan fungsi `NormalizeQtyUnit()`.

---

## 🗺 Vendor Detection & Parser Routing

Tambahkan deteksi vendor otomatis di `ParseReceiptAsync()` sebelum kirim ke Groq,
untuk menambahkan context yang tepat ke prompt:

```csharp
private static string DetectReceiptVendor(string ocrText) {
    if (ocrText.Contains("WINGS", StringComparison.OrdinalIgnoreCase) ||
        ocrText.Contains("SURAT JALAN", StringComparison.OrdinalIgnoreCase))
        return "WINGS_SURAT_JALAN";

    if (ocrText.Contains("FASTRATA", StringComparison.OrdinalIgnoreCase) ||
        ocrText.Contains("FAKTUR PENJUALAN", StringComparison.OrdinalIgnoreCase) ||
        ocrText.Contains("RTG,", StringComparison.OrdinalIgnoreCase))
        return "FASTRATA_FAKTUR";

    if (ocrText.Contains("TANI MAKMUR", StringComparison.OrdinalIgnoreCase) ||
        ocrText.Contains("Kasbon", StringComparison.OrdinalIgnoreCase) ||
        ocrText.Contains("Faktur No:", StringComparison.OrdinalIgnoreCase))
        return "TANI_MAKMUR_POS";

    return "GENERIC";
}
```

Gunakan vendor type untuk menambah hint di prompt Groq:
- `WINGS_SURAT_JALAN`: *"Kolom JMLH.BERSIH adalah total per baris. QTY ada di kolom 2 bersama kode produk (contoh: '5 BOX 20050')."*
- `FASTRATA_FAKTUR`: *"Nama produk diawali kode SKU panjang, strip kode tersebut. Qty ditulis bersambung dengan satuan (contoh: '10Box'). Kolom 'Jumlah Setelah Potongan' = total baris."*
- `TANI_MAKMUR_POS`: *"Baris 'Kasbon' bukan produk, skip. Tanggal di baris kedua format 'Wed,DD-Mon-YYYY'. Nama toko di baris pertama = supplier."*

---

## 📋 Rencana Implementasi (Prioritas)

| Prioritas | Bug | File | Estimasi |
|---|---|---|---|
| P1 🔴 | B4 — CleanProductName() | `AutomationEngine.cs` | 30 mnt |
| P1 🔴 | B2 — NormalizeQtyUnit() | `AutomationEngine.cs` + prompt | 30 mnt |
| P1 🔴 | B1 — Supplier vs Buyer | `GroqService.cs` + `AutomationEngine.cs` | 45 mnt |
| P2 🟡 | B5 — ParseFlexibleDate() | `AutomationEngine.cs` | 20 mnt |
| P2 🟡 | B6 — FooterKeywords filter | `AutomationEngine.cs` | 15 mnt |
| P2 🟡 | Vendor Detection + Hint Prompt | `AutomationEngine.cs` + `GroqService.cs` | 45 mnt |
| P3 🟢 | B7 — Naikkan fuzzy threshold 35→45 | `AutomationEngine.cs` | 5 mnt |
| P3 🟢 | B3 — Prompt harga netto | `GroqService.cs` | 15 mnt |

**Total estimasi: ~3.5 jam implementasi + testing**

---

## Open Questions

> [!IMPORTANT]
> **Q1 — Konversi Qty Wings:** Di Struk Wings, `5 BOX` x `40 pcs per box` = `200 pcs`. 
> Haruskah sistem menyimpan qty dalam satuan BOX (5) atau satuan dalam box (200)?
> Jawaban mempengaruhi unit conversion logic yang sudah ada.

> [!IMPORTANT]
> **Q2 — Harga di Struk Wings:** Harga yang diinput ke Purchase Document = harga per box (497.000) atau per pcs (99.400)?
> Kalau per pcs, maka harga beli di DB beda dengan harga yang ada di faktur.

> [!NOTE]
> **Q3 — Fastrata Harga Netto vs Gross:** 
> Harga Incl. PPN = 200.000/box. Setelah diskon Regular = 36.000, maka harga efektif = 164.000/box.
> Haruskah harga yang disimpan adalah harga netto setelah diskon?

> [!NOTE]
> **Q4 — Vendor baru:** Apakah ada supplier lain selain Wings, Fastrata, dan Tani Makmur?
> Ini menentukan apakah perlu Generic parser yang lebih robust.
