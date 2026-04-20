# RANCANGAN FITUR BOT TELEGRAM YANG BELUM ADA

Berdasarkan dokumentasi proyek Smart Sembako Assistant (file `.md` yang dianalisis), berikut adalah fitur-fitur untuk bot Telegram yang belum diimplementasikan atau masih dalam tahap pengembangan:

## 1. Integrasi OCR Struk

*   **Deskripsi**: Bot saat ini tidak memiliki kemampuan untuk memproses atau menganalisis foto struk fisik secara otomatis. Pengguna tidak dapat mengirim gambar struk untuk diekstrak informasinya (misalnya, daftar barang, harga, total). Fitur ini direncanakan untuk Fase 4.
*   **Implikasi**: Membutuhkan implementasi layanan OCR (kemungkinan menggunakan Tesseract) dan penanganan pesan foto di `TelegramBotService`.

## 2. Integrasi Google Sheets

*   **Deskripsi**: Sistem belum terintegrasi dengan Google Sheets untuk sinkronisasi data. Fitur ini akan memungkinkan data transaksi atau laporan untuk diekspor secara otomatis ke Google Sheets. Ini ditargetkan untuk Fase 4.
*   **Implikasi**: Membutuhkan setup proyek Google Cloud, implementasi `SheetsService`, dan logika untuk sinkronisasi otomatis.

## 3. Background Scheduler Lanjutan

*   **Deskripsi**: Meskipun ada laporan harian, scheduler yang lebih canggih untuk memantau stok, peringatan kedaluwarsa, dan membuat laporan harian secara otomatis di latar belakang belum sepenuhnya diimplementasikan. Ini merupakan bagian dari Fase 4.
*   **Implikasi**: Membutuhkan pengembangan `SchedulerService` untuk mengelola tugas-tugas terjadwal seperti pemeriksaan stok dan notifikasi otomatis.

## 4. Dukungan Voice Note (Pesan Suara)

*   **Deskripsi**: Bot saat ini hanya memproses pesan teks dan perintah. Pengguna tidak dapat berinteraksi dengan bot menggunakan pesan suara. Ini direncanakan untuk Fase 5.
*   **Implikasi**: Membutuhkan implementasi handler untuk pesan suara di `TelegramBotService` dan integrasi dengan layanan pengenalan suara jika diperlukan.

## Fitur Masa Depan (Fase 5)

Selain fitur-fitur di atas, dokumentasi juga menyebutkan beberapa rencana masa depan yang belum ada:

*   **Supplier Database**: Basis data pemasok untuk manajemen lebih baik.
*   **Multi-cabang Support**: Dukungan untuk mengelola beberapa cabang toko.
*   **Advanced Analytics & Charts**: Analitik dan visualisasi data yang lebih canggih.

---

**Catatan**: Daftar ini dibuat berdasarkan informasi yang tersedia di file dokumentasi proyek dan mungkin tidak mencakup semua kebutuhan yang mungkin muncul di masa depan. Untuk implementasi, disarankan untuk merujuk kembali ke dokumentasi proyek dan berdiskusi dengan tim pengembangan.