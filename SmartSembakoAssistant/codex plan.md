# Owner Data Commands + Role-Aware Store AI + Response Cleanup

## Summary
- Ubah bot dari “AI umum yang kadang nebak” menjadi **assistant operasional toko** yang mengutamakan data nyata dari `pos.db`, respons singkat, dan akses berbasis role.
- Tambahkan command owner-only untuk **pelanggan, supplier, user/kasir, penjualan produk, dan dokumen**.
- Perbaiki bug operasional yang terlihat di transcript:
  - `/laporan` menampilkan transaksi non-sales
  - `/analisa` menyebut “stok” untuk produk terlaris padahal itu angka terjual
  - `/rekomendasi_restock` default `50` untuk hampir semua item
  - `/notifikasi_stok` kadang menampilkan stok kosong
  - natural language untuk pelanggan/supplier/penjualan/dokumen masih terlalu bergantung pada AI dan sering salah konteks
- Standar gaya bot: **ringkas, profesional, emoji seperlunya**, tanpa basa-basi, tanpa echo prompt, tanpa meta-output.

## Key Changes
### 1. Command owner-only baru
- Tambahkan command berikut di `AutomationEngine` dan tampilkan di `/help` hanya untuk owner:
  - `/pelanggan`
    - tanpa argumen: tampilkan maksimal 10 pelanggan aktif dari tabel `Customer` dengan `IsCustomer=1`, urut nama
    - dengan argumen: cari nama pelanggan yang mengandung keyword, tampilkan nama, HP, email jika ada
  - `/supplier`
    - baca dari tabel `Customer` yang `IsSupplier=1`
    - format sama seperti `/pelanggan`
  - `/user`
    - tampilkan user dari tabel `User`, nama, username, role/access level, status aktif
    - optional argumen untuk filter nama/username
  - `/penjualan <produk>`
    - cari produk terbaik seperti command stok
    - tampilkan ringkasan penjualan produk: qty terjual, revenue, profit, dan tanggal penjualan terakhir
    - sertakan maksimal 5 baris transaksi terakhir yang melibatkan produk itu
  - `/dokumen <nomor>`
    - cari dokumen berdasarkan `Document.Number`
    - tampilkan tipe dokumen, tanggal, user/kasir, customer, total, dan item dokumen
- Semua command di atas **owner only**. Jika kasir mengakses, balas singkat: akses ditolak.

### 2. Layer data baru di `PosDbService`
- Tambahkan atau rapikan method berikut:
  - `GetCustomersAsync(string? query, int limit, bool onlyCustomers = true)`
  - `GetSuppliersAsync(string? query, int limit)` dengan sumber data `Customer.IsSupplier=1`
  - `GetUsersAsync(string? query, int limit)`
  - `GetProductSalesSummaryAsync(string productId)`:
    - sales-only (`DocumentTypeId = 2`)
    - total qty
    - total revenue
    - total profit
    - last sale date
  - `GetProductSalesTransactionsAsync(string productId, int limit)`
  - `GetDocumentByNumberAsync(string documentNumber)`
  - `GetDocumentItemsAsync(string documentId)`
- Rapikan query customer yang sudah ada:
  - `GetTopCustomersAsync` wajib filter `DocumentTypeId = 2`
  - `GetCustomerTransactionsAsync` wajib filter `DocumentTypeId = 2`
- Jangan buat entitas `Supplier` baru. Gunakan tabel `Customer` dengan flag `IsSupplier`.

### 3. Routing natural language yang tidak lagi “halu”
- Tambahkan intent routing deterministik sebelum fallback ke AI untuk pola pertanyaan owner:
  - “daftar pelanggan”, “pelanggan loyal”, “nama pelanggan X”, “supplier”, “kasir/user”, “data penjualan produk”, “cek dokumen”
- Untuk intent yang bisa dijawab SQL langsung:
  - jawab langsung dari service
  - AI hanya dipakai untuk merangkum jika perlu, bukan untuk menebak data
- Update `BuildRealStoreDataAsync`:
  - jangan selalu injek top-selling + low-stock untuk semua pertanyaan
  - kirim context yang relevan sesuai intent
  - untuk pertanyaan pelanggan/supplier/user, sertakan data faktual yang sesuai
- Update prompt `GroqService`:
  - tone ringkas-profesional
  - maksimal 2-4 kalimat untuk pertanyaan sederhana
  - jangan ulangi pertanyaan user
  - jangan output meta seperti `✅ User:` / `✅ AI:`
  - jika data tidak ada, jawab satu kalimat faktual
  - jika user kasir meminta data owner-only, jawab pendek bahwa data dibatasi untuk owner

### 4. Perbaikan minor operasional dari transcript
- `/laporan`
  - gunakan **recent sales only**, bukan semua dokumen
  - jumlah transaksi = count transaksi sales pada hari itu, bukan jumlah list recent docs
  - transaksi terakhir = ambil dari query sales-only
- `/analisa`
  - untuk produk terlaris, tampilkan `terjual X`, bukan `stok X`
  - low stock dan dead stock tetap terpisah
- `/rekomendasi_restock`
  - hapus rumus default `Math.Max(50 - stok, 10)` sebagai baseline tunggal
  - ganti dengan:
    - jika ada histori penjualan: pakai rata-rata penjualan 30 hari / 7 hari dan rekomendasi realistis
    - jika tidak ada histori penjualan: masukkan ke section `Perlu Review Manual`, bukan langsung sarankan `50`
- `/notifikasi_stok`
  - normalisasi tampilan stock null menjadi `0`
  - normalisasi unit kosong menjadi `Pcs`
- Semua respons command owner dirapikan ke format konsisten:
  - judul singkat
  - bullet flat
  - emoji seperlunya
  - tidak bertele-tele

## Interface / Command Changes
- Command owner baru:
  - `/pelanggan [nama]`
  - `/supplier [nama]`
  - `/user [nama]`
  - `/penjualan <produk>`
  - `/dokumen <nomor>`
- Natural language yang harus dipetakan ke jalur deterministik:
  - `siapa pelanggan terloyal`
  - `daftar pelanggan`
  - `ada pelanggan namanya owi`
  - `daftar supplier`
  - `data penjualan kapal api mix`
  - `cek dokumen 26-100-000066`
  - `dokumen pembelian atau penjualan`
- Tidak ada perubahan hak akses:
  - owner: full
  - kasir: operasional saja

## Test Plan
- Role/access:
  - owner bisa pakai `/pelanggan`, `/supplier`, `/user`, `/penjualan`, `/dokumen`
  - kasir ditolak untuk semua command di atas
- Pelanggan/supplier:
  - `/pelanggan` menampilkan customer dari tabel `Customer` dengan `IsCustomer=1`
  - `/supplier` menampilkan entri `Customer` dengan `IsSupplier=1`
  - `/pelanggan owi` menemukan customer `owi`
  - natural language “ada ga daftar pelanggan namanya owi” mengembalikan hasil faktual, bukan bilang data tidak ada
- Penjualan/dokumen:
  - `/penjualan kapal api mix` menampilkan data penjualan nyata, bukan hanya modal/jual
  - `/dokumen <nomor>` membedakan pembelian/inventory/penjualan dengan benar
- Laporan/analisa:
  - `/laporan` hanya menghitung dan menampilkan transaksi sales-only
  - `/analisa` menampilkan produk terlaris sebagai qty terjual, bukan stok
- Restock recommendation:
  - produk tanpa histori penjualan tidak lagi otomatis direkomendasikan `50`
  - produk dengan histori penjualan menghasilkan rekomendasi berbasis histori
- Response style:
  - tidak ada output meta `✅ User:` / `✅ AI:`
  - jawaban pendek, rapi, dan sesuai role
- Data formatting:
  - stok nol tampil sebagai `0`, bukan kosong
  - unit kosong fallback ke `Pcs`

## Assumptions
- Sumber supplier adalah tabel `Customer`, bukan tabel `Supplier`, karena schema DB nyata tidak memiliki tabel supplier terpisah.
- Semua fitur data pelanggan/supplier/user/penjualan detail tetap **owner only**.
- Paket command yang diimplementasikan adalah paket inti:
  - `/pelanggan`
  - `/supplier`
  - `/user`
  - `/penjualan <produk>`
  - `/dokumen <nomor>`
- Gaya respons default adalah ringkas-profesional dengan emoji seperlunya.
- Fokus implementasi adalah command owner, routing natural language terarah, prompt tightening, dan bug operasional yang terlihat di transcript; tidak mencakup Google Sheets, OCR, atau dashboard baru pada iterasi ini.
