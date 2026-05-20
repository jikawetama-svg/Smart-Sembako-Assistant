# 🛡️ OCR Fallback & Error Handling Plan

Dokumen ini adalah rancangan mendalam untuk meminimalisir kegagalan pembacaan OCR (Tesseract) dan merancang mekanisme *Human-in-the-Loop*, di mana pengguna dapat memperbaiki data yang salah baca sebelum data tersebut masuk ke database Aronium.

---

## 1. Strategi Meminimalisir Kegagalan Pembacaan (Pre-Processing)

Tesseract OCR sangat sensitif terhadap kualitas gambar. Foto struk yang miring, gelap, atau buram akan menghasilkan teks rongsokan (gibberish).

### Rekomendasi Teknis:
*   **Image Pre-processing Pipeline:** Sebelum gambar masuk ke Tesseract, kita harus memprosesnya secara otomatis di background:
    1.  **Grayscale & Binarization:** Mengubah gambar menjadi hitam-putih pekat (menghilangkan bayangan/warna kertas).
    2.  **Deskewing:** Meluruskan gambar jika difoto miring.
    3.  **Resizing:** Memperbesar gambar (upscaling) sekitar 2x lipat sering kali membuat Tesseract membaca huruf kecil jauh lebih akurat.
*   **AI Text Correction (Opsional tapi Kuat):** Menggunakan model AI Groq yang sangat cepat (misal `llama-3.1-8b-instant`) HANYA untuk merapikan teks *mentah* dari OCR sebelum masuk ke `ParserFactory`. AI sangat pintar memperbaiki salah ketik khas OCR (contoh: `M1nyak 1 L!t3r` -> `Minyak 1 Liter`).

---

## 2. Mekanisme User Fix (Human-in-the-Loop)

Jika sistem tetap gagal mengenali produk atau salah membaca harga/kuantitas, sistem tidak boleh langsung membuang data tersebut atau memaksakan data yang salah. Harus ada *Fallback Mechanism*.

### Skenario 1: Koreksi via Telegram (Quick Fix)
Saat bot mengirimkan "Preview", tampilkan instruksi jelas untuk mengoreksi.

**Contoh Flow:**
```text
Bot:
🧾 PREVIEW STRUK OCR
✅ Minyak Bimoli 2L    2 pcs × 35.000 = 70.000
⚠️ "?n??k Sania"       3 pcs × 34.000 = 102.000 (Produk Tidak Dikenal)
⚠️ "Beras Makmur"      1 pcs × 0      = 0 (Harga Tidak Terbaca)

Lanjutkan?
[✅ LANJUTKAN (Abaikan ⚠️)]
[❌ BATAL]
[✏️ EDIT ITEM (Balas pesan ini)]
```
*Jika user memilih Edit:* User membalas dengan format: `Edit 2 Minyak Sania 2L` atau `Edit 3 65000`. Sistem mengupdate memori *pending transaction* dan mengirim ulang Preview.

### Skenario 2: OCR Review Queue di Desktop UI (Advanced Fix)
Jika faktur sangat panjang dan rumit untuk diedit via Telegram, sediakan antrean dokumen (Review Queue) di WPF Dashboard.

1.  Bot mengirim pesan: `Struk terlalu rumit (5 error). Data telah disimpan ke "Review Queue" di Aplikasi Desktop. Silakan periksa di sana.`
2.  Di layar aplikasi (PC), ada halaman **[ OCR Review ]** yang menampilkan gambar struk asli di sebelah kiri, dan tabel (DataGrid) hasil parsing di sebelah kanan. Pengguna bisa mengetik ulang nama/harga yang salah sambil melihat gambar aslinya.

---

## 3. Penanganan Masalah Format Vendor Baru

Bagaimana jika sistem menerima struk dari vendor yang belum pernah ada parser-nya, dan `GenericParser` gagal menebak polanya?

### Rekomendasi:
Sediakan fitur **"Learning Mode" (Alias & Template Mapping)**.
Jika user sering mengoreksi nama dari `"M1nyak S4nia"` menjadi `"Minyak Sania 2L"`, sistem secara otomatis menyimpannya ke tabel `ProductMappings` di pengaturan. Seiring waktu, AI tidak akan pernah gagal lagi untuk kata tersebut.

---

## ❓ Pertanyaan untuk Anda (Mohon Dijawab)

Agar fitur ini tidak menjadi *over-engineered* (terlalu rumit dari yang dibutuhkan), mohon pandangan Anda:

1.  **Prioritas Koreksi:** Lebih baik user bisa mengoreksi kegagalan OCR langsung dari ketikan *chat* Telegram (Skenario 1), atau Anda lebih nyaman jika struk yang gagal diarahkan untuk diperbaiki di layar Laptop/Desktop (Skenario 2)?
2.  **Pemrosesan Gambar:** Apakah Anda ingin kita memasukkan library seperti `OpenCvSharp` atau `ImageMagick` ke dalam project ini untuk melakukan *Pre-processing* gambar (deskew, binarize)? Ini sangat meningkatkan akurasi, tapi akan membuat ukuran aplikasi sedikit lebih besar.
3.  **Tingkat Toleransi:** Jika dari 10 item di faktur ada 1 item yang tidak dikenali (⚠️), apakah Anda biasanya lebih suka bot *skip* item itu dan lanjut menyimpan 9 item yang valid, atau Anda ingin proses ditahan total sampai Anda memperbaikinya?
