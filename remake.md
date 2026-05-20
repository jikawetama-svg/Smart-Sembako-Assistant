# Rencana Perbaikan OCR & Pemetaan Manual (Update Arsitektur Aman)

Saran Anda sangat tepat dan jauh lebih aman untuk jangka panjang. Kita tidak boleh menjadikan model *lite* sebagai pengambil keputusan mutlak untuk *source of truth* nama produk, melainkan membiarkan sistem pencocokan *fuzzy* dan pembelajaran alias lokal yang bekerja. 

Berikut adalah pembaruan *Implementation Plan* berdasarkan "Pipeline Ideal" yang Anda berikan:

## 1. Perubahan Paradigma AI OCR (Pipeline Aman)
**Arsitektur Ideal:** OCR -> AI (hanya ekstrak struktur, qty, harga) -> Pertahankan nama asli -> Fuzzy Match lokal -> Alias Learning.
AI tidak lagi disuruh memperbaiki spasi, memisahkan kata, atau memperbaiki *typo*. Tugas AI HANYA menyusun *raw string* dari Tesseract menjadi JSON terstruktur. 

## 2. Sinkronisasi OCR Review Queue dengan `ocr_mappings.json`
Ketika *fuzzy match* gagal (karena teks terlalu berantakan) dan masuk ke *OCR Review Queue*, lalu Anda memperbaikinya secara manual (Klik Resolve), hasil perbaikan tersebut akan:
1. Masuk ke *database alias* (agar mesin belajar).
2. **Ditambahkan ke `ocr_mappings.json`** agar bisa Anda pantau, edit ulang, dan hapus melalui menu *Settings > OCR Mappings*.

---

## Proposed Changes

### 1. GroqService.cs (Pembaruan Prompt)
Memperbarui prompt agar AI bertindak pasif terhadap nama produk dan hanya aktif mengekstrak angka/satuan.
#### [MODIFY] GroqService.cs
- Hapus aturan yang menyuruh memisahkan kata.
- Tambahkan kumpulan aturan ketat yang Anda sarankan:
  - `PERTAHANKAN teks nama produk semirip mungkin dengan OCR asli.`
  - `Jangan mengganti merek, ukuran, atau satuan jika tidak yakin.`
  - `Jangan menggabungkan kata yang sudah terpisah spasi.`
  - `Jika ragu, pertahankan teks asli OCR.`
  - `JANGAN mencoba "memperbaiki", "menebak", atau "merapikan" nama produk.`

### 2. ConfigService.cs (Fungsi Bantuan Sinkronisasi)
Menambahkan logika untuk menjembatani Database dan JSON.
#### [MODIFY] ConfigService.cs
- Tambahkan metode `AddOcrMapping(string invoiceName, string dbProductId, string dbProductName)` yang menyalin *mapping* baru dari Review Queue ke dalam konfigurasi memori, lalu otomatis menimpa `ocr_mappings.json`.

### 3. SettingsView.xaml.cs (Trigger Sinkronisasi UI)
Mengaitkan aksi klik "Resolve" dengan pembaruan JSON.
#### [MODIFY] SettingsView.xaml.cs
- Pada `BtnResolveOcrQueue_Click`, setelah kode menyimpan produk ke database, tambahkan pemanggilan `ConfigService.AddOcrMapping(...)`.
- *Refresh* `_ocrProductMappings` di antarmuka (UI) agar baris baru otomatis muncul di tabel OCR Mappings tanpa harus *restart* aplikasi.

---

## User Review Required

> [!TIP]
> **Arsitektur Lebih Aman**
> Pendekatan ini akan sangat mengurangi beban komputasi AI (karena tidak perlu berpikir linguistik) dan meminimalkan halusinasi (mengarang nama produk). Semua error *typo* OCR akan diatasi oleh metode *Levenshtein Distance* (fuzzy matching) bawaan C#.


# Rencana Perbaikan OCR & Pemetaan Manual (Arsitektur Aman)

## Analisis Masalah
1. **Kenapa teks masih digabung?** Teks yang digabung berasal dari pembacaan awal Tesseract OCR (karena jarak teks di gambar struk fisik terlalu rapat). 
2. **Kenapa AI tidak otomatis dipaksa memisahkan teks?** Karena AI versi *lite* memiliki tendensi "menebak" atau "mengarang" yang berbahaya untuk integritas data inventaris. Seperti saran Anda, AI tidak boleh menjadi *source of truth*. AI cukup bertugas mem-*parsing* struktur JSON (kuantitas, harga, dsb.), dan membiarkan *fuzzy matcher* lokal yang mencocokkan namanya.
3. **Kenapa hasil OCR Review Queue tidak masuk ke JSON?** Karena saat ini hasilnya hanya disimpan di *database* SQLite (`product_aliases`) agar bot bisa belajar diam-diam, tanpa memberikan antarmuka visual (UI) bagi Anda untuk mengeditnya secara manual.

---

## Rencana Perubahan (Proposed Changes)

### 1. Perubahan Prompt AI (Arsitektur Pasif & Aman)
**File Target:** `GroqService.cs`
Kita akan mengubah prompt AI untuk berhenti menyuruh AI memperbaiki teks. 
Tugas AI akan diubah menjadi:
- `PERTAHANKAN teks nama produk semirip mungkin dengan OCR asli.`
- `Jangan mengganti merek, ukuran, atau satuan jika tidak yakin.`
- `Jangan menggabungkan kata yang sudah terpisah spasi.`
- `Jika ragu, pertahankan teks asli OCR.`
- `JANGAN mencoba "memperbaiki", "menebak", atau "merapikan" nama produk.`

### 2. Sinkronisasi Review Queue ke `ocr_mappings.json`
**File Target:** `ConfigService.cs`
- Membuat fungsi baru `AddOcrMapping(string invoiceName, string dbProductId, string dbProductName)` untuk menulis dan menyimpan pemetaan baru ke dalam `ocr_mappings.json`.

### 3. Pembaruan UI Settings
**File Target:** `SettingsView.xaml.cs`
- Pada fungsi tombol `BtnResolveOcrQueue_Click`, setelah sistem menyimpan pembelajaran ke *database*, sistem juga akan memanggil `ConfigService.AddOcrMapping()`.
- Ini memastikan bahwa setiap kali Anda mencocokkan produk di *Review Queue*, pemetaan tersebut **langsung tertulis ke JSON dan muncul di tabel OCR Mappings** di menu Settings, sehingga Anda dapat dengan mudah mengedit atau menghapusnya kapan saja tanpa perlu me-*restart* aplikasi.

### Wireframe UI Update (Bagian OCR Pemetaan Nama)

[ TextBox: Nama Faktur ] [ TextBox: Nama di Database ] [ Cari DB ] [ + Tambah / Update ]
-----------------------------------------------------------------------------------------
| Nama di Faktur        | Produk di Database     | ID      | Aksi                       |
-----------------------------------------------------------------------------------------
| Kapalapimix1Ds        | Kapal Api Mix 1 Dus    | 142     | [ Edit ] [ Hapus ]         |
| GulapasirBal          | Gula Pasir 1 Bal       | 99      | [ Edit ] [ Hapus ]         |
-----------------------------------------------------------------------------------------

Alur Kerja Fitur Edit:
Ketika tombol [ Edit ] diklik pada sebuah baris, Nama di Faktur dan Nama di Database akan otomatis naik (terisi) ke TextBox input di bagian atas tabel.
Tombol + Tambah akan mendeteksi apakah nama faktur tersebut sudah ada. Jika ada, tombol tersebut secara logika akan berfungsi sebagai Update (menimpa mapping lama) alih-alih membuat duplikat baru.
Anda tinggal menekan tombol [ Cari ] ulang jika ingin mengganti tujuan produk database-nya, lalu klik [ + Tambah ] (yang menyimpan perubahannya).
