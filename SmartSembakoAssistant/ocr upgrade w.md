# Rencana Perbaikan OCR & Pembaruan Model AI (Updated)

Dokumen ini adalah rencana implementasi yang sudah disesuaikan dengan persetujuan Anda.

## 1. Analisis Masalah Spasi pada OCR (Gambar 1;"C:\Users\MyBook SAGA 12\OneDrive\Gambar\Screenshot\Screenshot 2026-05-08 043728.png" vs Gambar 2; "D:\HOME\tempt\26b1a075-4e9f-4bca-a83b-d65bb6a2c285.jpg")
Sesuai arahan di `remake.md`, saya telah menelusuri seluruh *codebase* C# (`AutomationEngine.cs`, `DatabaseService.cs`, `GroqService.cs`, dll) untuk mencari proses normalisasi yang terlalu agresif.

**Hasil Temuan:**
* Fungsi `NormalizeText` dan `NormalizeAliasKey` di *codebase* saat ini sudah benar dan **TIDAK menghapus spasi**. Mereka justru menggunakan mekanisme yang mempertahankan spasi.
* **Akar Masalah (Root Cause):** Penghapusan spasi ("Kapal api mix 1Ds" menjadi "Kapalapimix1Ds") terjadi di tahap **AI Parsing (Groq/Gemini)**. Prompt yang ada sebelumnya (`Perbaiki typo OCR yang jelas, misalnya spasi hilang...`) disalahartikan oleh model AI, sehingga model tersebut secara halusinatif menggabungkan kata-kata pada produk.

## 2. Pembaruan Fallback Model (riwatat.md & Persetujuan)
Kita akan menyertakan beberapa opsi model AI pada UI Settings dan konfigurasi:
- **Dipertahankan/Ditambahkan:** `gemini-3.1-flash-lite-preview`, `gemini-3.1-flash-lite`, `gemma-4-31b-it`, dan `gemma-4-27b-it`.
- **Dibuang:** `gemini-1.5-flash` (dihapus dari daftar).
- **Default Fallback:** `gemma-4-31b-it` (atau bisa dipilih sesuai keinginan).

## 3. Pencegahan Bug Config Bloat (percakapan claude.md)
Terdapat masalah potensial di mana `ExpiryThresholds` bisa terduplikasi. Walaupun di `config.json` saat ini sudah bersih, kita harus memastikan logika penyimpanannya di UI/Settings tidak menduplikasi *array* tersebut.

---

## Proposed Changes

### GroqService.cs
Perbaiki prompt AI agar AI tidak merusak teks hasil OCR yang sudah bagus.
#### [MODIFY] GroqService.cs
- Ubah `ParseReceiptAsync` dan perbarui `systemPrompt` dengan instruksi ketat:
  - **"DILARANG KERAS menghapus spasi dari nama produk. Biarkan spasi seperti aslinya (contoh: 'Kapal api mix 1Ds' JANGAN diubah menjadi 'Kapalapimix1Ds')."**

### config.json / AppConfig
#### [MODIFY] config.json
- Ubah `"FallbackModel": "gemini-1.5-flash"` menjadi `"FallbackModel": "gemma-4-31b-it"`.

### SettingsView.xaml & SettingsView.xaml.cs
#### [MODIFY] SettingsView.xaml (UI / ComboBox)
- Tambahkan opsi `gemini-3.1-flash-lite` dan `gemini-3.1-flash-lite-preview` ke dalam dropdown model Fallback.
- Tambahkan opsi `gemma-4-31b-it` dan `gemma-4-27b-it`.
- Hapus opsi `gemini-1.5-flash`.
#### [MODIFY] SettingsView.xaml.cs / ConfigService.cs (Pencegahan Bloat)
- Tambahkan logika untuk melakukan `.Clear()` pada `ExpiryThresholds` dan `StockThresholds` sebelum melakukan penambahan data baru saat proses simpan config di halaman Settings.

---

## User Review Required

> [!NOTE]
> Sesuai instruksi Anda, saya hanya memperbarui dokumen rencana ini dan **belum melakukan pengeditan kode apa pun**. Silakan periksa kembali jika ada tambahan, atau beri tahu saya bila Anda sudah siap untuk saya mengeksekusi kode ini!
