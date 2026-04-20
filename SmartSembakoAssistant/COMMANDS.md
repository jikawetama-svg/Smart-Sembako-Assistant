# Qwen CLI Command Modes

Panduan penggunaan mode AI untuk project SmartSembakoAssistant.

Gunakan command berikut agar AI merespons sesuai konteks pekerjaan.

---

# 1. /plan

Digunakan untuk:
- merancang fitur
- membuat roadmap
- membuat arsitektur modul
- menyusun implementasi bertahap

### Template:

/plan [tujuan / fitur yang ingin dirancang]

### Contoh:

/plan buat roadmap fitur OCR struk untuk telegram bot

### Output:
- architecture breakdown
- mekanisme kerja langkah demi langkah
- checklist progres
- saran struktur file
- wireframe UI jika diperlukan

---

### Prompt Detail:

/plan Buat rancangan implementasi fitur [nama fitur].
Jelaskan:
1. arsitektur modul
2. mekanisme kerja step-by-step
3. file/module yang perlu dibuat
4. checklist progres implementasi
5. wireframe text UI jika diperlukan
6. roadmap bertahap yang maintainable

---

# 2. /code

Digunakan untuk:
- membuat kode lengkap
- implementasi service
- integrasi modul
- generate fitur modular

### Template:

/code [fitur / service yang ingin dibuat]

### Contoh:

/code buatkan SchedulerService untuk notifikasi stok otomatis

### Output:
- kode lengkap modular
- lokasi file
- dependency
- catatan implementasi

---

### Prompt Detail:

/code Buat implementasi production-ready untuk [nama fitur/service].
Harus mencakup:
1. struktur file
2. kode modular lengkap
3. dependency yang dibutuhkan
4. penempatan file
5. catatan implementasi singkat
Gunakan arsitektur yang konsisten dengan project yang ada.

---

# 3. /debug

Digunakan untuk:
- analisa error
- cari akar masalah
- memperbaiki bug
- refactor kode bermasalah

### Template:

/debug [masalah / error]

### Contoh:

/debug kenapa scheduler tidak mengirim notifikasi telegram

### Output:
- root cause
- langkah perbaikan
- snippet fix

---

### Prompt Detail:

/debug Analisa masalah berikut: [deskripsi error].
Berikan:
1. kemungkinan root cause
2. prioritas perbaikan
3. kode perbaikan yang diperlukan
4. penjelasan teknis singkat
Fokus pada solusi praktis dan aman untuk arsitektur project.

---

# 4. /fast

Digunakan untuk:
- ringkas file
- edit cepat
- penjelasan singkat

### Template:

/fast [permintaan cepat]

### Contoh:

/fast ringkas isi TECHNICAL_DOCS.md

### Output:
- jawaban singkat dan langsung

---

### Prompt Detail:

/fast Jawab secara singkat dan langsung untuk tugas berikut: [permintaan].
Berikan hasil ringkas, jelas, dan teknis bila diperlukan.

---

# WORKFLOW RECOMMENDED

Gunakan urutan berikut untuk pengembangan fitur:

1. Rancang fitur:

/plan buat roadmap fitur supplier database

2. Implementasi kode:

/code buatkan SupplierService modular lengkap

3. Jika error:

/debug kenapa SupplierService gagal insert data

4. Ringkas hasil:

/fast ringkas perubahan di dokumentasi

---

# BEST PRACTICE

Gunakan:
- /plan untuk desain
- /code untuk implementasi
- /debug untuk bug fixing
- /fast untuk tugas singkat

Agar AI menghasilkan output yang konsisten, modular, dan sesuai arsitektur project.