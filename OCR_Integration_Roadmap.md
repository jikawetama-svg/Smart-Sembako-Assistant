# Roadmap Integrasi OCR ke Smart Sembako Assistant

**Tanggal:** April 20, 2026  
**Status:** Planning & Design Phase  
**Tujuan:** Mengintegrasikan OCR untuk pemrosesan struk WhatsApp/Telegram, dengan role-based access, UI/UX lengkap, dan mekanisme teknis yang solid.

**Kondisi Aplikasi Saat Ini (v4.0.0):**
- ✅ Telegram Bot lengkap dengan command, AI, memory, restock/inventory.
- ✅ Dashboard WPF modern dengan UI overhaul (Fluent Design).
- ✅ Database integration (Aronium pos.db), logging, settings.
- ✅ BotController untuk lifecycle management.
- ✅ WhatsApp Handler dengan kemampuan sama seperti Telegram (command, chat natural, OCR placeholder).
- ❌ OCR belum diimplementasikan (coming soon di README).
- ❌ Role-based access belum ada (hanya basic).
- ❌ Product matching dan alias belum ada.

Roadmap disesuaikan agar realistis dengan kondisi sekarang.

---

## 🗺️ Roadmap Utama

### Phase 1: Integrasi WhatsApp Bridge
**Tujuan:** Tambahkan dukungan WhatsApp sebagai channel input, mirip Telegram.  
**Durasi:** 1 minggu  
**Progres Checklist:**
- [x] Desain WhatsAppHandler untuk menerima pesan dari Aroanium WhatsApp Bridge (via HTTP endpoint atau queue).
- [x] Implementasi WhatsAppHandler class di Services.
- [x] Update MessageRouter untuk route pesan dari Telegram/WhatsApp ke handler yang sama.
- [x] Buat CommandHandler untuk kemampuan sama seperti Telegram (/stok, /laporan, chat natural, dll.).
- [x] API mekanisme untuk send message back ke WhatsApp.
- [x] Testing koneksi WhatsApp Bridge (asumsi bridge sudah ada).

### Phase 2: Role-Based Access Control (RBAC)
**Tujuan:** Sistem akses berdasarkan role untuk Telegram/WhatsApp.  
**Durasi:** 1 minggu  
**Progres Checklist:**
- [x] Desain tabel Users, Roles, Permissions (SQLite).
- [x] Tambah kolom TelegramId, WhatsappNumber di Users.
- [x] Implementasi authentication di TelegramBotService dan WhatsAppHandler.
- [x] Middleware permission check per command.
- [x] Testing role Owner (full), Kasir (OCR + Purchase), Staff (OCR).

### Phase 3: Merge OCR ke Smart Sembako Assistant
**Tujuan:** Implementasi OCR sebagai modul internal.  
**Durasi:** 2 minggu  
**Progres Checklist:**
- [x] Desain OcrService untuk download gambar dan OCR text (Tesseract.NET).
- [x] Desain ProductMatcher dengan lapisan Exact/Alias/Fuzzy.
- [ ] Implementasi OcrService dan integrasi dengan Telegram/WhatsApp.
- [ ] Tabel ProductAliases untuk learning.
- [ ] Testing OCR parsing dan error handling.

### Phase 4: Chat Bot Flow untuk OCR Resolution
**Tujuan:** Resolution produk unknown via chat WhatsApp/Telegram.  
**Durasi:** 1 minggu  
**Progres Checklist:**
- [x] State machine diagram untuk conversation flow.
- [x] Tabel ConversationSessions untuk simpan state.
- [ ] Implementasi state handler di bot services.
- [ ] Flow: Struk → OCR → Unknown → Pilih → Konfirmasi.
- [ ] Testing full flow via chat.

### Phase 5: UI/UX Dashboard untuk User Management & OCR
**Tujuan:** Dashboard admin untuk kelola user, role, dan monitoring OCR.  
**Durasi:** 2 minggu  
**Progres Checklist:**
- [x] Wireframe User Access Management, Roles & Permissions, Access Logs.
- [x] Wireframe Resolve Unknown Products, OCR Queue.
- [ ] Tambah Views di WPF (UserManagementView, OcrReviewView).
- [ ] Integrasi dengan database untuk CRUD user/role.
- [ ] Mekanisme auto-save alias dari dashboard.

### Phase 6: Integration & Testing End-to-End
**Tujuan:** Gabungkan semua dan test full flow.  
**Durasi:** 1 minggu  
**Progres Checklist:**
- [ ] End-to-end: WhatsApp struk → OCR → Resolution → Purchase → Google Sheets.
- [ ] Performance: Respons <4 detik, accuracy >90%.
- [ ] Error handling: User tidak terdaftar, OCR gagal.
- [ ] Dokumentasi dan release.

---

## 🎨 Wireframe UI

### 1. Halaman User Access Management
```
┌─────────────────────────────────────────────────────────────────────┐
│ SMART SEMBAKO ASSISTANT                             Owner: ARIFIN   │
├─────────────────────────────────────────────────────────────────────┤
│ Sidebar             │ User Access Management                        │
│                     │                                               │
│ > Dashboard         │ [ + Add User ]   [ Search User... ]           │
│ > Inventory         │                                               │
│ > Purchase          │ ┌───────────────────────────────────────────┐ │
│ > Reports           │ │ Nama      Role      Telegram   WA   Stat │ │
│ > Settings          │ ├───────────────────────────────────────────┤ │
│    - Users          │ │ Arifin    Owner     Linked    Linked  🟢 │ │
│    - Roles          │ │ Kasir A   Cashier   Linked    Linked  🟢 │ │
│    - Logs           │ │ Staff B   Staff     -         Linked  🟡 │ │
│                     │ └───────────────────────────────────────────┘ │
│                     │                                               │
│                     │ [ Edit ] [ Disable ] [ Reset Access ]         │
└─────────────────────────────────────────────────────────────────────┘
```

### 2. Modal Add User
```
┌──────────────────────────────────────────────┐
│ Add New User                                 │
├──────────────────────────────────────────────┤
│ Nama             : [_____________________]   │
│ Role             : [ Cashier ▼ ]             │
│ Telegram ID      : [_____________________]   │
│ WhatsApp Number  : [_____________________]   │
│ Status           : [ Active ▼ ]              │
│                                              │
│             [ Cancel ]   [ Save User ]       │
└──────────────────────────────────────────────┘
```

### 3. Halaman Roles & Permissions
```
┌─────────────────────────────────────────────────────────────────────┐
│ Roles & Permissions                                                 │
├─────────────────────────────────────────────────────────────────────┤
│ Role List           │ Permissions                                   │
│                     │                                               │
│ > Owner             │ Role: Cashier                                │
│ > Cashier           │                                               │
│ > Staff             │ [✓] OCR Receipt                              │
│                     │ [✓] Purchase Entry                           │
│                     │ [ ] Edit Stock                               │
│                     │ [ ] View Reports                             │
│                     │ [ ] Manage Users                             │
│                     │                                               │
│                     │                     [ Save Permissions ]      │
└─────────────────────────────────────────────────────────────────────┘
```

### 4. Halaman Resolve Unknown Products
```
┌──────────────────────────────────────────────────────────────────────────┐
│ Resolve Unknown Products                                                 │
├──────────────────────────────────────────────────────────────────────────┤
│ Receipt: Sumber Makmur - 20 Apr 2026                                     │
│ 3 products need review before purchase can continue                      │
│                                                                          │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ OCR Product: MIE GRG SP                                              │ │
│ │ Qty: 2         Price: 5000                                           │ │
│ │ Status: 🔴 Unknown Product                                           │ │
│ │                                                                      │ │
│ │ Match to Existing Product:                                           │ │
│ │ [ Search product name................................. ] [Search]    │ │
│ │                                                                      │ │
│ │ Suggested Matches:                                                   │ │
│ │ ( ) Mie Goreng Spesial                                               │ │
│ │ ( ) Mie Goreng Original                                              │ │
│ │ ( ) Mie Instan Goreng                                                │ │
│ │                                                                      │ │
│ │ [ Confirm Match ]      [ + Create New Product ]                      │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
│                                                                          │
│                          [ Save & Continue ]                             │
└──────────────────────────────────────────────────────────────────────────┘
```

### 5. Flow Percakapan Bot
- **Step 1:** User kirim struk → Bot: "📄 Struk diterima. Sedang membaca..."
- **Step 2:** OCR selesai → Bot: "Produk berikut belum dikenali: 'TG PYNG'. Pilih: 1. Terigu Payung 1Kg 2. Terigu Payung 500gr 3. Produk baru"
- **Step 3:** User balas 1 → Bot: "✅ Dipetakan. Ringkasan: Total 178000. 1. Konfirmasi 2. Batal"
- **Step 4:** User balas 1 → Bot: "✅ Pembelian berhasil diproses."

---

## 🔄 Mekanisme UI dan UX

### Mekanisme Akses Role-Based
- **Authentication:** Cek Telegram ID / WhatsApp Number di tabel Users.
- **Permission Check:** Setiap command cek role (Owner: all, Kasir: OCR + Purchase, Staff: OCR).
- **UX:** User tidak terdaftar → "Nomor belum terdaftar". Role tidak punya akses → "Akses ditolak".

### Mekanisme OCR Processing
- **Matching:** Exact → Alias → Fuzzy → Unknown.
- **Resolution:** Via chat (balas angka) atau dashboard (card-based edit).
- **UX:** Fokus exception (hanya unknown ditampilkan), confidence warning, auto-save alias.

### Mekanisme Chat Bot
- **Flow:** Struk → OCR → Unknown → Pilih → Konfirmasi → Purchase.
- **UX:** Minim ketik (balas angka), feedback real-time, fallback ke dashboard jika kompleks.

### Mekanisme Dashboard
- **User Management:** Add/Edit user dengan role, status channel (🟢 Linked).
- **Monitoring:** Logs akses, status koneksi, progress resolve.
- **UX:** Simple, visual badges, one-click actions.

---

## ⚙️ Hal Teknis

### Arsitektur Backend
- **Modul Utama:** OcrService, ProductMatcher, PurchaseService, TelegramBotService, WhatsAppHandler.
- **Database:** SQLite dengan tabel Users, Roles, Permissions, ProductAliases, ConversationSessions, ReceiptResolutionQueue.
- **State Machine:** Switch state (IDLE, PROCESSING_RECEIPT, RESOLVING_PRODUCTS, WAITING_CONFIRMATION).
- **Teknologi:** C# .NET 8, Telegram.Bot, WhatsApp Bridge, Tesseract OCR, Google Sheets API.

### Mekanisme Teknis OCR
- **Input:** Gambar struk dari WhatsApp/Telegram.
- **Proses:** Download → OCR text → Parse item → Match produk → Resolve unknown → Purchase.
- **Error Handling:** Confidence <80% → Warning; Gagal total → Input manual.

### Mekanisme Teknis Chat Flow
- **Session Management:** Simpan state per user di ConversationSessions.
- **Handler:** Switch berdasarkan state (e.g., WAITING_PRODUCT_SELECTION → Process selection).
- **Persistence:** Database, bukan memory, untuk restart safety.

### Mekanisme Teknis UI
- **WPF Framework:** MainWindow dengan sidebar, Views untuk User Management, OCR Review.
- **Data Binding:** MVVM pattern, auto-refresh untuk status.
- **Security:** Enkripsi API keys, whitelist user.

### Dependencies
- Telegram.Bot
- Microsoft.Data.Sqlite
- Tesseract.NET
- Google.Apis.Sheets.v4
- WhatsApp Bridge (eksternal)

---

## 📊 Progres Keseluruhan
- **Completed:** 40% (Design & Planning)
- **In Progress:** 0% (Implementation belum mulai)
- **Remaining:** 60% (Implementation & Testing)

**Next Steps:**
1. Mulai implementasi Phase 1 (Merge OCR).
2. Build UI dashboard untuk user management.
3. Test OCR flow via chat.

---

**Catatan:** Roadmap ini berdasarkan percakapan dan dapat disesuaikan. Fokus pada UX praktis untuk kasir/owner, dengan teknis yang scalable.</content>
<parameter name="filePath">/workspaces/Smart-Sembako-Assistant/OCR_Integration_Roadmap.md