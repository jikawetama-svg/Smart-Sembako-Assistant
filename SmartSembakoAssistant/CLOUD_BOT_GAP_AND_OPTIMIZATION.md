# Audit & Optimasi Cloud Bot — Toko Sembako Teh Asiah

Tanggal audit: 28 Juli 2026

## Ringkasan

Cloud Bot sudah berhasil menerima webhook Telegram dan membaca snapshot POS dari Supabase. Namun, ia belum setara dengan bot lokal karena cloud hanya boleh membaca data tersinkronisasi; aksi yang mengubah stok, pembelian, dan dokumen tetap harus dilakukan Desktop POS.

Arsitektur yang dipertahankan:

```text
Desktop POS (sumber data & aksi tulis) -> Supabase (snapshot cloud) -> Cloud Bot (baca & jawab)
```

Pola ini mencegah konflik stok. Desktop perlu dibuka untuk sinkronisasi awal dan untuk pembaruan berkala, tetapi Cloud Bot tetap dapat menjawab dari snapshot terakhir saat Desktop ditutup.

## Temuan dari percakapan Telegram dan perbaikan

| Gap yang ditemukan | Dampak | Perbaikan yang diterapkan |
|---|---|---|
| `stok kritis` diklasifikasikan sebagai pencarian stok biasa | Bot mencari produk bernama “kritis” lalu menyatakan stok aman | Intent prioritas `restock_rekomendasi`; meminta daftar produk stok rendah |
| `restock kapal api mix` dianggap riwayat restock | Jawaban tidak relevan dan berisiko memberi kesan bot dapat menulis data | Dipisahkan menjadi `desktop_input_restock` dan diarahkan ke Desktop POS |
| “Gimana caranya meningkatkan penjualan” dibaca sebagai laporan | Bot membalas angka omset, bukan saran | Intent `strategi_penjualan` khusus untuk respons konsultatif |
| “Nama toko ku apa?” mengubah nama toko menjadi kalimat pertanyaan | Store Brain rusak | Pengubahan nama kini wajib eksplisit: `set nama toko: ...`; pertanyaan nama toko dibaca aman |
| Bot menyangkal ingatan chat | Kepercayaan pengguna menurun | Pertanyaan “barusan saya minta apa” dijawab dari riwayat pesan yang tersimpan |
| Jumlah transaksi cloud selalu `0` | Laporan penjualan tidak akurat | Desktop Sync sekarang mengambil `GetSalesTransactionCountAsync()` sebelum mengirim ringkasan harian |

## Batasan saat ini dan tindakan yang masih diperlukan

1. Jalankan `data/supabase_schema.sql` di Supabase bila belum pernah dijalankan. Tabel penting: `products_sync`, `transactions_summary`, `conversations_memory`, dan `store_brain`.
2. Pastikan konfigurasi Desktop mengaktifkan Supabase, lalu jalankan **Sync Delta Cloud**. Cek `last_delta_sync` pada `sync_metadata` atau status sync di Desktop.
3. Isi `OWNER_TELEGRAM_IDS` di Render. Tanpa itu mode awal memberi akses owner ke semua pengguna—cocok hanya saat setup, bukan produksi.
4. Jangan memakai `anon key` untuk bot yang menyimpan memory. Gunakan key dengan kebijakan RLS yang tepat; idealnya pisahkan key Desktop (tulis) dan Cloud Bot (baca + memory terbatas).
5. Data restock, koreksi inventory, dan `top_products_json` belum terlihat disinkronkan oleh `SyncService`. Fitur Cloud terkait akan kosong sampai ekspor tabel tersebut ditambahkan ke Desktop.

## Prioritas pengembangan berikutnya

### P0 — Akurasi dan operasional

- Sinkronkan summary transaksi walaupun tidak ada perubahan produk; implementasi saat ini hanya mengirim summary di cabang saat ada produk aktif.
- Tambahkan field `last_sync_at` dan `source_status` pada jawaban Cloud agar user tahu data masih baru atau sudah kedaluwarsa.
- Bedakan “tidak ada data” dari “query ke Supabase gagal”; jangan pernah menyimpulkan stok aman saat koneksi gagal.
- Tambahkan uji integrasi untuk `stok kritis`, nama toko, memory, laporan hari/bulan, dan RBAC sebelum deploy.

### P1 — Kesetaraan fitur lokal

- Sinkronkan riwayat restock dan koreksi inventori dari Desktop ke `restock_sync` dan `inventory_sync`.
- Kirim jumlah transaksi nyata dan `top_products_json` per hari.
- Tambahkan rekomendasi restock terukur: rata-rata penjualan, stok saat ini, lead time supplier, minimum stock, dan jumlah pesan.
- Tambahkan command eksplisit: `/statussync`, `/namatoko`, `/ingat`, dan `/help` dengan contoh yang sesuai cloud.

### P2 — Agent yang aman dan bernilai bisnis

- Buat tool `sync_health` dan `restock_forecast` yang benar-benar terdaftar; planner saat ini meminta prediksi restock tetapi tool prediksinya belum tersedia.
- Terapkan approval workflow: Cloud boleh membuat *draft* restock, Desktop/owner wajib menyetujui sebelum ada perubahan data.
- Tambahkan scheduler alert stok rendah, stok minus, dan laporan tutup hari dengan zona waktu `Asia/Jakarta`.
- Simpan preferensi toko secara eksplisit, misalnya `set nama toko: Toko Sembako Teh Asiah`, bukan melalui tebakan model.

## Konfigurasi Render yang direkomendasikan

```env
ENVIRONMENT=production
SUPABASE_URL=https://<project>.supabase.co
SUPABASE_KEY=<key-terbatas-untuk-cloud-bot>
TELEGRAM_BOT_TOKEN=<token-bot>
TELEGRAM_SECRET_TOKEN=<secret-panjang-acak>
OWNER_TELEGRAM_IDS=<telegram-user-id-owner>
SCHEDULER_ENABLED=true
SCHEDULER_MORNING_HR=7
SCHEDULER_EVENING_HR=20
GROQ_API_KEY=<opsional>
GEMINI_API_KEY=<opsional>
```

Jangan set `PORT` secara manual di Render; platform menyediakan nilainya dan Dockerfile sudah membacanya secara dinamis.

## Cara menggunakan setelah deploy

- Set nama toko: `set nama toko: Toko Sembako Teh Asiah`
- Cek nama toko: `nama toko ku apa?`
- Cek stok: `cek stok kapal api mix`
- Daftar prioritas: `stok kritis` atau `rekomendasi restock`
- Riwayat restock: `riwayat restock kapal api mix`
- Laporan: `laporan hari ini`, `laporan minggu ini`, `laporan bulan ini`
- Ingat konteks: `barusan saya minta apa?`

## Verifikasi rilis ini

- `python -m py_compile` berhasil untuk modul runtime utama.
- Routing intent diuji untuk tujuh kasus percakapan yang sebelumnya salah klasifikasi.
- `pytest` belum tersedia pada Python lokal ini, sehingga suite pytest tidak dijalankan. File tes yang ketinggalan versi endpoint telah diselaraskan untuk `/` dan `/bot-health`.
