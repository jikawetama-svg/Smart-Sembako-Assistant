kamu bisa baca ini ga? apa aja yang tertulis digambar

Ya, saya bisa bantu baca isi nota itu. Karena tulisan tangan dan foto agak gelap, ada beberapa bagian yang cukup jelas, dan ada beberapa yang masih ambigu.
Berikut hasil bacaan saya dari gambar:
🧾 Isi Nota
BanyaknyaNama BarangHargaJumlah3Bapia kecil60120.0002Bapia besar55110.0001Kemper2560.0001Serklet (kurang jelas)336.0001Puring Nasi240.0001Mitu Baby-30.000
Total:
Jumlah Rp. 396.000
⚠️ Bagian yang kurang jelas:
1. "Bapia kecil"
Kemungkinan:
Bapia kecil
atau Bakpia kecil
Tulisan terlihat seperti:
Bapia kecil
2. "Kemper"
Bisa jadi:
Kemper
Kamper
Tulisan terlihat seperti:
Kemper
3. "Serklet"
Ini kurang jelas, bisa jadi:
Serklet
Seket
Serbet
Tulisan terlihat seperti:
Serklet
4. Kolom harga
Angka di kolom harga kemungkinan adalah:
60
55
25
3
2
Tapi formatnya tidak konsisten, jadi mungkin itu:
harga satuan singkat
atau kode harga
🧠 Kesimpulan:
OCR/manual masih bisa baca struktur nota:
✅ jumlah barang✅ nama barang sebagian besar✅ total nota 396.000
Tapi untuk otomasi bot, nota seperti ini berisiko error karena:
tulisan tangan
pencahayaan kurang
nama barang tidak baku
💡 Saran untuk bot OCR kamu:
Untuk nota seperti ini, jangan langsung auto-save.
Flow yang aman:
OCR baca nota↓Bot kirim hasil bacaan↓User konfirmasi / edit↓Baru simpan
Contoh:
Terdeteksi:1. Bapia kecil - 1200002. Bapia besar - 1100003. Kemper - 60000Balas:1 = benar2 = edit
Karena nota tulisan tangan seperti ini tidak aman untuk auto parsing langsung.
Kalau kamu mau, langkah berikutnya saya bisa bantu analisis apakah OCR bot kamu sekarang akan mampu baca nota seperti ini, lalu saya kasih solusi teknis supaya nota tulisan tangan tetap bisa diproses, misalnya pakai:
preprocessing gambar
OCR hybrid
manual correction flow
Itu bakal sangat berguna untuk proyek bot struk kamu.

kalo ini bisa ga

Ya, yang ini jauh lebih bisa dibaca, bahkan jauh lebih cocok untuk sistem OCR bot kamu dibanding nota tulisan tangan tadi.
Karena struk ini punya:
✅ teks cetak✅ tabel rapi✅ format konsisten✅ angka jelas
Ini tipe struk yang ideal untuk OCR otomatis.
🧾 Dari gambar, saya bisa baca sebagian besar:
Dokumen ini adalah Surat Jalan.
Terlihat data:
Tanggal: 30.03.2026
Kirim ke: TK AISAH
Vendor: AROV
Sopir: RUSKI MUL
No. PO: 85404041
Total: 3.061.854
📦 Barang yang terbaca:
Berikut beberapa item yang terlihat:
QtyNama BarangHargaTotal5Sedap Mie Bag 690gr Ayam Special99.400497.0002Sedap Mie Bag 770gr Soto Special108.300216.76015Krisbee 7 Bag 656gr5.75086.25020Potabee Reg Bag 156gr BBQ Beef4.35687.11310Potabee Reg Bag 656gr Chili Seaweed7.85278.5173Potabee Reg Bag 156gr Grill Seaweed98.386295.158
Dan total bawah:
TOTAL: 3.061.854
🧠 Kesimpulan OCR:
Untuk OCR:
Nota tulisan tangan sebelumnya:
❌ sulit untuk auto OCR
Surat jalan ini:
✅ sangat layak untuk auto OCR
🔥 Ini artinya untuk proyek kamu:
Kalau supplier mengirim invoice seperti ini, sistem kamu bisa otomatis:
foto surat jalan↓OCR baca tabel↓parse qty + produk + harga↓simpan ke sheet
Dan akurasinya bisa lumayan tinggi.
⚠️ Tapi ada 3 tantangan teknis:
1. Tabel tidak otomatis terbaca rapi
OCR hanya baca teks mentah.
Contoh hasil OCR bisa jadi:
5 Sedap Mie Bag 690gr Ayam Special 99.400 497.000
Bukan langsung berbentuk kolom.
Jadi kamu harus buat parser:
angka pertama = qty
angka terakhir = subtotal
angka sebelum subtotal = harga
2. Nama produk panjang
Contoh:
Sedap Mie Bag 690gr Ayam Special
Ini bisa terpotong.
Maka perlu:
product alias matching
3. Format supplier bisa beda-beda
Supplier A formatnya beda dengan supplier B.
Maka parser harus:
deteksi supplier↓pakai parser template supplier
Ini sangat penting.
🚀 Saran untuk sistem kamu sekarang
Karena struk seperti ini sudah layak OCR, maka upgrade terbaik:
1. Tambah OCR preprocessing
Sebelum OCR:
grayscale
sharpen
threshold
Tujuannya memperjelas teks.
Flow:
Image↓Preprocess↓OCR
Ini bisa naikkan akurasi drastis.
2. Gunakan parser berbasis baris
Setelah OCR:
Pisah per line, misalnya:
5 Sedap Mie Bag...2 Sedap Mie Bag...
Lalu regex:
qty + nama + harga + subtotal
3. Template parser per supplier
Contoh:
Supplier = Wings→ gunakan WingsParser
Karena layout invoice supplier biasanya konsisten.
Ini meningkatkan akurasi besar.
4. Tambahkan review hasil OCR
Bot reply:
Terdeteksi 6 produk.Simpan?1. Ya2. Edit
Ini penting untuk validasi.
🎯 Penilaian kelayakan untuk OCR:
Nota tulisan tangan:
3/10
Surat jalan seperti ini:
8.5/10
Kalau ada preprocessing + parser supplier:
9/10
Artinya:
surat jalan seperti ini sangat feasible untuk otomatisasi
Kalau kamu mau, langkah selanjutnya saya bisa bantu buat:
desain parser OCR khusus invoice supplier seperti gambar ini
Misalnya:
regex parser baris produk
cara deteksi qty / harga / subtotal
format JSON hasil parsing
Supaya langsung bisa kamu implementasikan di C# bot kamu.

ketiganyaregex parser baris produk cara deteksi qty / harga / subtotal format JSON hasil parsing

Mantap. Ini bagian inti teknis OCR invoice parser, dan kalau ini rapi, bot kamu akan jauh lebih stabil.
Saya bantu bikin 3 hal:
Regex parser baris produk
Cara deteksi qty / harga / subtotal
Format JSON hasil parsing
Saya buat dengan asumsi hasil OCR seperti invoice supplier tadi:
5 Sedap Mie Bag 690gr Ayam Special 99.400 497.0002 Sedap Mie Bag 770gr Soto Special 108.380 216.76015 Krisbee 7 Bag 656gr 5.750 86.250
🔥 1. REGEX PARSER BARIS PRODUK
Tujuan:Pisahkan satu baris menjadi:
qty
nama produk
harga
subtotal
Struktur umum baris:
[qty] [nama produk] [harga] [subtotal]
Regex dasar:
^(\d+)\s+(.+?)\s+([\d\.,]+)\s+([\d\.,]+)$
Penjelasan:
(\d+)
Ambil qty:
5
(.+?)
Ambil nama produk:
Sedap Mie Bag 690gr Ayam Special
([\d\.,]+)
Ambil harga:
99.400
([\d\.,]+)
Ambil subtotal:
497.000
Contoh hasil parsing:
Baris:
5 Sedap Mie Bag 690gr Ayam Special 99.400 497.000
Hasil:
qty = 5name = Sedap Mie Bag 690gr Ayam Specialprice = 99.400subtotal = 497.000
💻 Implementasi C#
Contoh:
var regex = new Regex(@"^(\d+)\s+(.+?)\s+([\d\.,]+)\s+([\d\.,]+)$");var match = regex.Match(line);if (match.Success){ var qty = int.Parse(match.Groups[1].Value); var name = match.Groups[2].Value; var price = ParseNumber(match.Groups[3].Value); var subtotal = ParseNumber(match.Groups[4].Value);}
🔥 2. DETEKSI QTY / HARGA / SUBTOTAL
Ini penting karena OCR kadang kacau.
Cara deteksi:
QTY = angka pertama
Contoh:
5 Sedap Mie Bag ...
Ambil angka pertama.
Subtotal = angka terakhir
Contoh:
... 99.400 497.000
Ambil angka terakhir = subtotal
Harga = angka sebelum subtotal
Ambil angka kedua terakhir.
Jadi algoritma:
split by spaceqty = first numbersubtotal = last numberprice = second last numbername = middle text
Contoh:
5 Sedap Mie Bag 690gr Ayam Special 99.400 497.000
Token:
[5][Sedap][Mie][Bag][690gr][Ayam][Special][99.400][497.000]
Hasil:
qty = 5price = 99.400subtotal = 497.000name = Sedap Mie Bag 690gr Ayam Special
💻 C# Version
var parts = line.Split(' ');qty = int.Parse(parts[0]);subtotal = ParseNumber(parts[^1]);price = ParseNumber(parts[^2]);name = string.Join(" ", parts.Skip(1).Take(parts.Length - 3));
🔥 Parse Number Indonesia
Karena angka:
99.400
harus jadi:
99400
Function:
decimal ParseNumber(string text){ return decimal.Parse(text.Replace(".", "").Replace(",", ""));}
🔥 3. FORMAT JSON HASIL PARSING
Setelah parsing, simpan dalam JSON agar mudah:
dikirim ke Google Sheets
disimpan ke DB
dikirim ke bot
Format:
{ "supplier": "WINGS", "invoice_date": "2026-03-30", "items": [ { "qty": 5, "name": "Sedap Mie Bag 690gr Ayam Special", "price": 99400, "subtotal": 497000 }, { "qty": 2, "name": "Sedap Mie Bag 770gr Soto Special", "price": 108380, "subtotal": 216760 } ], "total": 3061854}
🧠 Kenapa JSON penting?
Karena nanti pipeline kamu:
OCR↓Parse↓JSON↓Google Sheets / DB
JSON jadi format standar
🔥 Saran struktur C# model
public class ParsedInvoice{ public string Supplier { get; set; } public DateTime InvoiceDate { get; set; } public List<ParsedItem> Items { get; set; } public decimal Total { get; set; }}public class ParsedItem{ public int Qty { get; set; } public string Name { get; set; } public decimal Price { get; set; } public decimal Subtotal { get; set; }}
🚨 Masalah yang pasti muncul
Regex ini bagus untuk:
invoice dengan format stabil
Tapi akan gagal kalau OCR hasilnya:
5 Sedap Mie Bag 690gr 99.400497.000
karena subtotal pindah baris.
Maka perlu Line Merge Preprocessing
Gabungkan baris yang terpotong:
if line tidak match regexgabung dengan line berikutnya
Alur lengkap parser:
OCR text↓Split lines↓Merge broken lines↓Regex parse↓JSON result↓Review user↓Save
🎯 Saran implementasi tercepat untuk project kamu:
Kerjakan urut:
1.
Buat parser token-based:
qty = firstprice = second lastsubtotal = last
Ini paling tahan OCR.
2.
Output ke JSON
3.
Bot kirim preview:
{ "items": 6, "total": 3061854}
4.
User confirm → save
Kalau kamu mau, langkah berikutnya saya bisa bantu bikin:
fungsi C# lengkap untuk parse OCR text invoice jadi JSON otomatis
langsung siap tempel ke project kamu.

kalo gini kebaca ga? TANI MAKMUR PUTRA Jln Pasar Palumbon Mon,20-April-2026,8:16:28 ========================= Kasir : Acun Faktur No: 43339 Nama Pelanggan:Teh Aisah ========================= Produk Harga Qty Sat Total Pop Ice 10800 10 rcng 108000 Finto/top capucino 1pk 38500 2 pak 77000 Hit mat/mc1000 1pak 1 pak 112500 Teh cap poci 1pk@5 18000 2 pak 36000 Plastik 1/2 H 3000 50 ikt 150000 Plastik 1/4 H 2250 25 ikt 56250 PE 1kg 52000 3 kg 156000 Samsu12 1pk 1 pak 194500 Dj coklat kretek 1pk 163500 10 pak 1635000 Aci soba AS/HST 1Bal 210000 2 bal 420000 Palmia 200gr 1Dus 1 dus 367000 Minyak 20kg 423000 2 kmpn 846000 Keresek 40 bintang 1 pak 26500 ------------------------------------------- SubTotal 4184750 Kasbon 1138600 Total 5323350 Bayar 0 KasbonBaru -5323350

Ya — format seperti ini jauh lebih mudah dibaca parser, bahkan ini sudah cukup bagus untuk diproses otomatis.
Karena struk ini punya pola yang konsisten:
[Nama Produk] [Harga] [Qty] [Satuan] [Total]
Contoh:
Pop Ice 10800 10 rcng 108000
Itu sangat cocok untuk parser.
✅ Yang bisa dibaca otomatis:
Parser bisa ambil:
Nama Produk
Harga
Qty
Satuan
Total
Contoh hasil parse:
Input:
Pop Ice 10800 10 rcng 108000
Output:
{ "name": "Pop Ice", "price": 10800, "qty": 10, "unit": "rcng", "total": 108000}
🔥 Tetapi ada masalah besar di data ini:
Beberapa baris harga kosong / OCR hilang, misalnya:
Hit mat/mc1000 1pak 1 pak 112500
Di sini parser membaca:
nama: Hit mat/mc1000 1pak
qty: 1
unit: pak
total: 112500
❗ Harga tidak ada
Padahal harusnya:
Harga = 112500Qty = 1Total = 112500
Jadi parser harus bisa handle 2 pola:
POLA A — Lengkap
Ada harga dan qty:
Pop Ice 10800 10 rcng 108000
Struktur:
[Nama] [Harga] [Qty] [Sat] [Total]
POLA B — Harga kosong
Kalau qty = 1, sering harga tidak ditulis:
Hit mat/mc1000 1pak 1 pak 112500
Struktur:
[Nama] [Qty] [Sat] [Total]
Harga = total
🧠 Ini artinya parser kamu harus punya dua regex
REGEX 1 — lengkap
Untuk baris seperti:
Pop Ice 10800 10 rcng 108000
Regex:
^(.+?)\s+(\d+)\s+(\d+)\s+(\w+)\s+(\d+)$
Hasil:
Nama
Harga
Qty
Satuan
Total
REGEX 2 — harga kosong
Untuk baris:
Hit mat/mc1000 1pak 1 pak 112500
Regex:
^(.+?)\s+(\d+)\s+(\w+)\s+(\d+)$
Hasil:
Nama
Qty
Satuan
Total
Lalu:
Harga = Total / Qty
💻 Contoh logika C#
if (regexFull.Match(line).Success){ // parse lengkap}else if (regexNoPrice.Match(line).Success){ // price = total / qty}
🔥 Contoh parsing dari data kamu
1. Pop Ice
Pop Ice 10800 10 rcng 108000
hasil:
{ "name":"Pop Ice", "price":10800, "qty":10, "unit":"rcng", "total":108000}
2. Hit Mat
Hit mat/mc1000 1 pak 112500
hasil:
{ "name":"Hit mat/mc1000", "price":112500, "qty":1, "unit":"pak", "total":112500}
🔥 Format JSON final:
{ "invoice_no": "43339", "customer": "Teh Aisah", "items": [ { "name": "Pop Ice", "price": 10800, "qty": 10, "unit": "rcng", "total": 108000 }, { "name": "Finto/top capucino 1pk", "price": 38500, "qty": 2, "unit": "pak", "total": 77000 } ], "subtotal": 4184750, "total": 5323350}
🚨 TAPI ADA HAL YANG HARUS DIWASPADAI
Beberapa baris seperti ini:
Dj coklat kretek 1pk 163500 10 pak 1635000
Bisa bermasalah kalau nama produk ada angka.
Karena parser bisa salah baca:
1pk dianggap qty
padahal bagian nama
SOLUSI:
Parser harus baca dari belakang:
Ambil dari kanan:
[total] = angka terakhir[unit] = kata sebelum total[qty] = angka sebelum unit[harga] = angka sebelum qty[sisa kiri] = nama produk
Ini jauh lebih aman
Contoh:
Dj coklat kretek 1pk 163500 10 pak 1635000
Dari kanan:
1635000 = totalpak = unit10 = qty163500 = harga
Sisa:
Dj coklat kretek 1pk
= nama produk
🔥 Ini metode terbaik untuk kasus kamu
Bukan regex full line.
Tapi:
token parsing dari kanan ke kiri
Karena nama produk bisa berisi angka.
🎯 Kesimpulan:
Ya, format ini:
sangat bisa diparse otomatis
dan jauh lebih aman dibanding OCR invoice gambar.
Rekomendasi terbaik:
Gunakan parser:
ambil token dari kanan
Urutan:
total → unit → qty → harga → nama
Ini paling cocok untuk struk kamu.