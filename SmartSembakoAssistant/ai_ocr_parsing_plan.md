# 🧠 AI-Powered OCR Parsing Plan

Dokumen ini merangkum analisis mengapa pembacaan struk Anda terus gagal meskipun Tesseract berhasil mengekstrak teks, dan bagaimana kita akan menggunakan **AI (Groq LLM)** untuk menggantikan logika C# yang kaku.

---

## 🔍 Analisis Kegagalan Saat Ini

Berdasarkan log yang Anda kirimkan, Tesseract **BERHASIL** membaca teks dengan sangat baik:
```text
Produk Harga Qty Sat Total
Lakban bening tebal  9500  6  rol  57000
Magnum hitam 1pk   1  pak  256000
Keresek 40 bintang   1  pak  25000
...
```

Namun sistem merespons:
> *"Struk terbaca, tetapi item belum bisa dipetakan."*

### Kenapa Ini Terjadi?
Masalahnya ada di fungsi `ParseReceiptAsync()` (Logika kode C#). Saat ini, sistem mencoba memecah baris menggunakan kode kaku (seperti Regex atau pemisahan Spasi dari kanan ke kiri). 
Faktur "TANI MAKMUR" menggunakan urutan: **[Nama] [Harga] [Qty] [Satuan] [Total]**.
Jika kode C# berharap urutannya **[Nama] [Qty] [Satuan] [Harga] [Total]**, maka fungsi parser C# akan langsung "menyerah" dan mengembalikan daftar produk KOSONG (0 item). Itulah sebabnya bot mengatakan item belum bisa dipetakan.

Logika *hardcoded* (Regex) sangat rapuh. Satu spasi berlebih atau struktur kolom yang berbeda dari vendor akan merusak seluruh sistem.

---

## 🚀 Solusi: AI Parsing (LLM Groq)

Daripada membuat puluhan parser C# untuk setiap vendor (`TaniParser`, `WingsParser`, dll), kita akan memanfaatkan `GroqApiClient` yang sudah ada di sistem bot Anda. 

Kita akan menggunakan AI untuk membaca teks mentah dan mengubahnya menjadi JSON yang rapi!

### Alur Baru (AI-OCR Pipeline)

1. **Tesseract OCR:** Mengubah gambar struk menjadi teks mentah (*Raw Text*).
2. **AI Extractor (Groq):** Mengirimkan teks mentah tersebut ke Groq dengan instruksi (*Prompt*) ketat.
3. **JSON Deserialization:** Bot membaca JSON dari Groq dan mengubahnya menjadi List C#.
4. **Fuzzy Matching:** Mencocokkan nama dari JSON dengan database Aronium (`pos.db`).

### Contoh Prompt AI yang Akan Ditanam di C#
```text
SISTEM: 
Kamu adalah mesin ekstraksi data struk pembelian. 
Baca teks struk berikut dan kembalikan HANYA format JSON Array yang valid, tanpa teks awalan/akhiran.
Abaikan baris total, kasbon, atau pajak. Hanya ekstrak baris produk.

[
  {
    "ParsedName": "Nama Produk",
    "Qty": 1,
    "Price": 10000,
    "Total": 10000
  }
]

TEKS STRUK:
{RawTextDariTesseract}
```

### Simulasi Reaksi AI pada Struk Anda
Ketika AI membaca struk "TANI MAKMUR", AI akan dengan cerdas membedakan mana harga dan mana kuantitas meskipun posisinya dibalik, lalu merespons:
```json
[
  { "ParsedName": "Lakban bening tebal", "Price": 9500, "Qty": 6, "Total": 57000 },
  { "ParsedName": "Magnum hitam 1pk", "Price": 256000, "Qty": 1, "Total": 256000 },
  { "ParsedName": "Garam KETJAK 1bal", "Price": 65000, "Qty": 2, "Total": 130000 }
]
```

---

## 🛠️ Rencana Eksekusi Kode (Tanpa Edit Sekarang)

Saat Anda menyetujui rencana ini, kita akan membongkar kode lama dan mengimplementasikan hal berikut:

1. **Hapus Sistem Parser Lama:** Kita tidak butuh lagi `IInvoiceParser`, `TaniParser`, atau `ParserFactory`.
2. **Ubah `OcrReceiptService.cs`:** 
   * Tambahkan pemanggilan ke `_groqApiClient`.
   * Buat fungsi `ExtractItemsViaAIAsync(string rawOcrText)` yang mengembalikan `List<OcrLineItem>`.
3. **Penanganan Error AI:** Jika AI gagal mematuhi format JSON (sangat jarang terjadi jika di-set ke mode JSON, Groq mendukung *JSON mode*), bot akan *retry* 1 kali atau memberitahu kasir.

### 💡 Keuntungan Tambahan
* **Anti-Typo:** AI sering kali otomatis memperbaiki typo hasil OCR. "L4kban b3ning" bisa otomatis diterjemahkan menjadi "Lakban bening".
* **Universal:** Vendor baru muncul besok? Bot akan langsung bisa membacanya tanpa perlu pembaruan kode!

Apakah Anda setuju dengan transisi dari **Regex Parser ke AI JSON Parser** ini? Jika ya, saya siap mulai mengedit kodenya!
