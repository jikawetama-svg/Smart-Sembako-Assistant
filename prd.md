**✅ PRD v2.1 – Smart Sembako Assistant (SSA)**  
**Versi Lengkap, Terinci & Final untuk Development**

**Nama Produk:** Smart Sembako Assistant (SSA)  
**Versi:** 2.1  
**Tanggal:** 04 April 2026  
**Status:** Production-Ready (Test Awal Telegram)  
**Platform:** Windows Desktop Application (C# .NET 8/9)  
**AI Utama:** Groq API (dengan fallback Gemini)  
**Antarmuka Utama:** Telegram Bot  
**Tujuan Utama:** Membuat AI Assistant toko sembako yang natural, pintar, memiliki memory, mempertimbangkan profit, dan mudah digunakan via Telegram.

---

### 🎯 1. TUJUAN PRODUK
**Goal Utama:**
- Membangun aplikasi desktop Windows satu paket yang berjalan di PC Aronium.
- Memberikan kontrol dan insight toko melalui Telegram Bot dengan pengalaman natural.
- Mengurangi input manual melalui OCR struk + AI parsing.
- Memberikan rekomendasi restock yang cerdas dengan mempertimbangkan stok, penjualan, expiry, dan profit/margin.
- Memastikan sistem ringan, stabil, portable, dan aman (tidak ada auto order supplier).

**Key Objectives:**
- Natural conversation (bukan hanya command kaku)
- Memiliki short-term & long-term memory
- Insight berbasis profit (bukan hanya volume)
- Interaksi aman dengan konfirmasi untuk rekomendasi penting
- Mudah dipindah antar PC

**KPI Sukses:**
- Respons Telegram < 4 detik (rata-rata)
- Akurasi parsing struk ≥ 92%
- Penggunaan natural language ≥ 60% dari total interaksi
- Error rate < 5%
- User satisfaction (Owner/Kasir) tinggi pada test awal

---

### 👤 2. USER PERSONA
| Persona | Kebutuhan Utama | Akses yang Diizinkan |
|---------|------------------|----------------------|
| **Owner** | Insight profit, rekomendasi restock, laporan natural, keputusan bisnis | Full access |
| **Kasir** | Kirim foto struk, cek stok cepat, input transaksi | Hanya struk & cek stok |
| **Admin** | Setting aplikasi, melihat log, monitoring | Full technical access |

---

### 🧩 3. FITUR LENGKAP (v2.1)

**A. Telegram Bot**
- Natural language understanding (bisa jawab pertanyaan bebas)
- Command cepat: `stok [nama]`, `laporan`, `analisa`, `restock`, `help`
- Interactive inline keyboard & buttons
- Short-term memory (8–10 percakapan terakhir per user)
- Long-term memory (ringkasan kebiasaan toko)
- Voice note support (opsional fase selanjutnya)

**B. Integrasi Aronium pos.db**
- Baca langsung dari path default: `C:\Users\[Username]\AppData\Local\Aronium\Data\pos.db`
- Data yang diambil: transaksi, stok, produk, users, expiry_date, batch, harga beli (jika tersedia)
- User & role validation

**C. Scanner Struk (OCR + AI)**
- Terima foto struk via Telegram
- Tesseract OCR → Groq parsing → preview hasil + tombol konfirmasi (Simpan / Edit / Batal)
- Simpan ke Google Sheets setelah dikonfirmasi

**D. AI Analisa (Groq API)**
- Rekomendasi restock dengan pertimbangan profit & margin
- Klasifikasi fast/normal/slow moving
- Anomaly detection (stok turun drastis, transaksi mencurigakan)
- Insight harian/mingguan (omzet, profit, top produk)

**E. Notifikasi Otomatis**
- Stok rendah (<20, <10, <5)
- Expiry warning (<30 hari, <7 hari = URGENT)
- Anomaly alert
- Daily summary (opsional)

**F. Google Sheets Integration**
- Sheet Transaksi (hasil OCR)
- Sheet Analitik & Log
- Sheet Memory Summary (long-term)

**G. Monitoring & Logging**
- Real-time log di aplikasi desktop
- Export log ke CSV
- Dashboard kesehatan sistem

---

### 🔄 4. MEKANISME & WORKFLOW

**Startup Flow:**
1. Jalankan SSA.exe
2. Load config.json + inisialisasi memory
3. Cek koneksi (pos.db, Groq, Telegram, Google Sheets)
4. Start Telegram Bot (long polling)
5. Start background scheduler (setiap 5 menit cek stok & expiry)

**Workflow Utama:**

**1. Natural Chat**
- User kirim pesan (teks/voice) → aplikasi ambil memory + data terkini
- Kirim ke Groq dengan prompt lengkap → jawaban natural
- Simpan percakapan ke short-term memory

**2. Proses Struk**
- Foto diterima → OCR → parsing Groq → tampilkan preview di Telegram dengan tombol konfirmasi
- Jika disetujui → simpan ke Google Sheets + update log

**3. Notifikasi Otomatis**
- Background timer baca pos.db
- Jika kondisi terpenuhi → kirim pesan + button jika perlu

**4. Anomaly Detection**
- Deteksi perubahan tidak normal → alert ke owner

**Safety Layer:**
- Rekomendasi besar (>50 pcs atau >Rp500.000) → minta konfirmasi owner

---

### 🎨 5. DESAIN UI/UX APLIKASI DESKTOP

**Tema:** Fluent Design Windows 11, aksen hijau toko, clean & profesional. Support Light/Dark mode.

**Navigasi:**
- Sidebar kiri: Dashboard | Chat Preview | Monitoring Stok | Log & Analitik | Settings

**Dashboard (Home):**
- Status cards: Bot, Groq API, pos.db, Memory Usage
- Quick Insights: Omzet hari ini, Estimasi Profit, Top Profit Products, Critical Stok, Expiry Warning
- Recent Conversations (natural chat terakhir)
- Tombol besar: Start/Stop Bot, Sync Now, Test AI

**Monitoring Stok:**
- Tabel sortable dengan kolom: Produk, Stok, Expiry, Margin %, Status
- Highlight warna (merah = urgent, orange = low)
- Filter & search

**Log Tab:**
- Tabel log lengkap + filter (Command, OCR, AI, Notifikasi, Anomaly)
- Export CSV

**Settings Tab:**
- API Settings (Groq Key, Gemini Fallback, Model)
- Path pos.db (auto-detect + manual browse)
- Telegram (Bot Token, Allowed Chat IDs, Rate Limit)
- Memory Settings (jumlah history yang disimpan)
- Threshold (Stok, Expiry, Anomaly)
- Profit Calculation Settings
- Offline Mode Behavior
- Tombol “Test All Connections”

**UX Principles:**
- Semua informasi penting terlihat di Dashboard
- Feedback instan (toast notification + loading indicator)
- Error message ramah dengan solusi
- Natural language suggestion di Dashboard

---

### 🧠 6. PROMPT GROQ v2.1 (Sudah Dioptimalkan)

**System Prompt Utama** → (lihat respons sebelumnya)

**User Prompt untuk Natural Conversation** → (lihat respons sebelumnya)

**Prompt Parsing Struk** → JSON output

**Prompt Analisa & Rekomendasi** → Format Markdown yang rapi

Semua prompt sudah dirancang untuk:
- Mempertimbangkan margin & profit
- Menggunakan memory konteks
- Memberikan jawaban actionable
- Meminta konfirmasi jika diperlukan

---

### 🔧 7. TEKNIS & IMPLEMENTASI

**Teknologi Stack:**
- Framework: .NET 8/9 Windows Desktop (WinUI 3 direkomendasikan)
- Telegram Bot: Telegram.Bot library
- AI: Groq API via HttpClient atau Groq .NET SDK
- OCR: Tesseract.NET (bahasa Indonesia)
- pos.db Access: Microsoft.Data.Sqlite
- Google Sheets: Google.Apis.Sheets.v4
- Local Storage: SQLite untuk memory & log
- Config: config.json (API keys terenkripsi dengan DPAPI atau AES)

**Portability:**
- Single-file executable atau folder distributable
- Mudah dipindah: copy folder → edit config.json (path pos.db & API keys)

**Security:**
- Enkripsi API keys
- Whitelist Chat ID
- Rate limiting
- User validation dari pos.db
- Log sensitif di-mask

**Offline Mode:**
- Saat internet putus: bot tetap jawab command sederhana dari cache pos.db
- Beri notif “AI sedang offline, hanya data lokal tersedia”

---

### ⚠️ 8. ERROR HANDLING & FALLBACK

- Groq error → otomatis fallback ke Gemini
- OCR gagal → minta foto ulang + opsi input manual
- pos.db tidak ditemukan → redirect ke Settings
- Internet putus → graceful degradation
- Anomaly terdeteksi → log + alert

---

### 📈 9. ROADMAP PENGEMBANGAN

**Phase 1 – Test Awal v2.1 (Sekarang)**
- Natural conversation + memory
- Profit awareness
- OCR dengan preview & konfirmasi
- Notifikasi + anomaly detection
- UI Dashboard lengkap

**Phase 2**
- Voice note support
- Supplier database sederhana
- Daily auto report
- Installer + auto-start Windows

**Phase 3**
- Multi-cabang support
- Grafik analitik lebih advanced
- WhatsApp migration (jika diperlukan)

---

### 🎯 10. ACCEPTANCE CRITERIA

- Bot dapat menjawab pertanyaan natural dengan akurat
- Parsing struk berhasil dengan preview & konfirmasi
- Memory konteks berfungsi (AI ingat percakapan sebelumnya)
- Profit & margin masuk dalam rekomendasi
- Notifikasi stok & expiry terkirim tepat waktu
- Aplikasi tetap stabil berjalan 24 jam
- Mudah dipindah ke PC lain dengan edit config saja

---

**PRD v2.1 ini sudah mencakup secara lengkap:**
- Semua mekanisme
- UI/UX detail
- Workflow
- Prompt Groq
- Memory system
- Profit awareness
- Anomaly detection
- Safety layer
- Teknis & portability
