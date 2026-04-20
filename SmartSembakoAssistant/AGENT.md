# 🤖 AGENT.md - Smart Sembako Assistant

Panduan untuk AI agent (Qwen Code) yang bekerja pada proyek Smart Sembako Assistant.

---

## 📋 Project Overview

**Nama:** Smart Sembako Assistant (SSA)  
**Versi:** 2.3  
**Platform:** Windows Desktop (WPF .NET 8)  
**Bahasa:** C# 12  
**AI Utama:** Groq API (LLaMA 3.1 70B) dengan fallback Gemini  
**Integrasi:** Telegram Bot, Aronium pos.db (READ-ONLY untuk data, WRITE untuk dokumen transaksi)  

**Tujuan:** AI Assistant toko sembako yang natural, pintar, memiliki memory, mempertimbangkan profit, dan mudah digunakan via Telegram.

---

## 🏗️ Architecture

### Technology Stack
- **Framework:** WPF .NET 8 (Windows Desktop)
- **Telegram Bot:** Telegram.Bot library (long polling)
- **AI:** Groq API via HttpClient + Gemini fallback
- **OCR:** Tesseract (Phase 4)
- **Database:** Microsoft.Data.Sqlite (pos.db & memory.db)
- **Google Sheets:** Google.Apis.Sheets.v4 (Phase 4)
- **Config:** JSON dengan DPAPI encryption

### Project Structure
```
SmartSembakoAssistant/
├── Models/           # Data models (AppConfig, Product, Memory, History)
├── Services/         # Business logic (6 core services + Engines)
├── Views/            # WPF UserControls (Dashboard, Stock, Logs, Settings)
├── ViewModels/       # MVVM ViewModels (jika ada)
├── Utils/            # Helper utilities
└── data/             # Runtime data (memory.db, logs)
```

### Core Services
1. **ConfigService** - Configuration management dengan DPAPI encryption
2. **DatabaseService** - SQLite CRUD untuk memory & logging
3. **LoggingService** - Logging system dengan CSV export
4. **PosDbService** - Aronium integration + **Restock/Inventory Engines**
5. **GroqService** - AI integration dengan fallback
6. **TelegramBotService** - Telegram bot handler + Command Engines

### Engines
- **Restock Engine**: Membuat dokumen Purchase (Type 1) untuk restock
- **Inventory Engine**: Membuat dokumen Inventory Count (Type 3) untuk koreksi stok
- **Recommendation Engine**: Auto-recommend restock berdasarkan stok rendah
- **History Engine**: Tracking riwayat restock & inventory

---

## 🎯 Development Guidelines

### 1. Coding Standards
- **Async/Await:** Semua operasi I/O harus async
- **Error Handling:** Try-catch dengan logging user-friendly
- **Security:** API keys terenkripsi dengan DPAPI
- **Performance:** Non-blocking UI, connection pooling
- **Naming:** PascalCase untuk public members, camelCase untuk private

### 2. Database Guidelines
- **pos.db:** READ-ONLY untuk data, WRITE ONLY via Dokumen (Purchase/Inventory Count)
- **memory.db:** Local SQLite untuk conversation, logs, memory
- **Queries:** Parameterized untuk prevent SQL injection
- **Indexes:** Pastikan ada index pada kolom yang sering diquery
- **DocumentTypeId Mapping:**
  - `1`: Purchase (Restock)
  - `2`: Sales
  - `3`: Inventory Count
  - `4`: Refund
  - `5`: Stock Return
  - `6`: Loss

### 3. AI Integration Guidelines
- **Groq First:** Selalu coba Groq API dulu
- **Gemini Fallback:** Otomatis fallback jika Groq error/timeout
- **Timeout:** 30 detik max untuk AI request
- **Temperature:** 0.7 untuk conversation, 0.3 untuk parsing
- **Max Tokens:** 500 untuk conversation, 600 untuk restock recommendation
- **Prompt Strategy:** System prompt + conversation history + context + **ANTI-HALLUCINATION RULES**

### 4. Telegram Bot Guidelines
- **Long Polling:** Gunakan polling, bukan webhook
- **Rate Limiting:** Default 5 detik antar pesan
- **Chat Whitelist:** Validate Chat ID jika dikonfigurasi
- **Natural Language:** Support bahasa Indonesia natural
- **Role-Based Access:** Owner vs Kasir
- **Commands:** `/start`, `/help`, `/stok`, `/laporan`, `/restock`, `/inventory`, `/analisa`, dll

### 5. UI/UX Guidelines
- **Theme:** Fluent Design Windows 11
- **Colors:** Aksen hijau toko (#2E7D32, #4CAF50)
- **Layout:** Sidebar navigation + content area
- **Feedback:** Loading indicators, toast notifications
- **Error Messages:** User-friendly dengan solusi
- **Responsive:** Support berbagai ukuran window

---

## 🔄 Workflow

### Feature Development
1. Baca PRD (`prd.md`) untuk requirement
2. Check existing code structure
3. Buat branch/changes dengan nama fitur jelas
4. Implement dengan遵循 coding standards
5. Test manual (jika applicable)
6. Update changelog.json
7. Dokumentasikan di TECHNICAL_DOCS.md

### Bug Fixes
1. Reproduksi issue
2. Identifikasi root cause
3. Fix dengan minimal changes
4. Test fix tidak break fitur lain
5. Update changelog.json

### Release Process
1. Update version di AssemblyInfo.cs
2. Update changelog.json dengan status "implemented"
3. Build Release: `dotnet build --configuration Release`
4. Test: `dotnet run`
5. Publish: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
6. Update README.md jika ada perubahan signifikan

---

## 📝 Changelog Format

Setiap perubahan harus dicatat di `changelog.json` dengan format:

```json
{
    "project": "Smart Sembako Assistant",
    "timezone": "UTC+07:00",
    "entries": [
        {
            "date": "YYYY-MM-DD",
            "version": "unreleased | X.Y.Z",
            "title": "Judul Perubahan",
            "summary": [
                "Ringkasan perubahan dalam 1-2 kalimat"
            ],
            "details": [
                "Detail teknis perubahan",
                "File impact",
                "Notes (compatibility, UX, dll)"
            ],
            "status": "draft | implemented | released"
        }
    ]
}
```

### Status Definitions
- **draft:** Perubahan sedang dikerjakan, belum selesai
- **implemented:** Selesai, belum dirilis sebagai versi resmi
- **released:** Sudah jadi rilis resmi (version = X.Y.Z)

### Version Numbering
- **unreleased:** Changes yang belum dirilis
- **X.Y.Z:** Versi resmi (Major.Minor.Patch)
- Major: Breaking changes
- Minor: Fitur baru (backward compatible)
- Patch: Bug fixes

---

## 🚨 Important Rules

### DO ✅
- Async/await untuk semua operasi I/O
- Logging untuk semua error & warning
- User-friendly error messages
- Fallback mechanisms (Groq → Gemini)
- Parameterized SQL queries
- DPAPI encryption untuk API keys
- READ-ONLY access ke pos.db untuk data
- WRITE access ke pos.db HANYA via Dokumen (Purchase/Inventory Count)
- Update changelog.json setiap perubahan
- Test sebelum commit
- Gunakan Transaction untuk operasi database kritis

### DON'T ❌
- Jangan write ke pos.db secara langsung (kecuali via Dokumen)
- Jangan hardcode API keys
- Jangan blocking UI thread
- Jangan skip error handling
- Jangan expose sensitive data di logs
- Jangan auto-order ke supplier (safety layer)
- Jangan bump version tanpa update changelog
- Jangan commit tanpa test
- Jangan gunakan DocumentTypeId salah (1≠100, 3≠300)

---

## 🧪 Testing Checklist

### Manual Testing (Setiap Perubahan)
- [ ] Application starts without error
- [ ] Affected feature works as expected
- [ ] No regression di fitur lain
- [ ] Error handling works (test dengan invalid input)
- [ ] Logging mencatat events dengan benar
- [ ] UI responsive (no freezing)
- [ ] Database transactions work correctly
- [ ] DocumentTypeId mapping benar

### Integration Testing (Phase 2+)
- [ ] Groq API responds
- [ ] Gemini fallback works
- [ ] Telegram bot polling works
- [ ] Database CRUD operations work
- [ ] Restock Engine creates Purchase documents correctly
- [ ] Inventory Engine creates Inventory Count documents correctly
- [ ] History tracking works
- [ ] Auto-recommendations work
- [ ] Bulk operations work

---

## 📚 Documentation

### Files to Update
- **README.md:** User guide (update jika ada perubahan signifikan)
- **TECHNICAL_DOCS.md:** Developer documentation (update setiap perubahan)
- **QUICK_START.md:** Setup guide (update jika setup berubah)
- **PHASE1_SUMMARY.md:** Phase summary (update sesuai progress)
- **PROJECT_STRUCTURE.md:** Structure overview (update jika ada file baru)
- **changelog.json:** Change log (WAJIB update setiap perubahan)
- **AGENT.md:** Agent guidelines (update jika ada perubahan arsitektur)
- **RESTOCK.md:** Restock Engine documentation
- **QUICK_INVENTORY.md:** Quick Inventory Engine documentation

### Documentation Standards
- Bahasa Indonesia untuk user-facing docs
- Bahasa Inggris untuk technical docs (opsional)
- Include code examples jika perlu
- Screenshot untuk UI changes (opsional)
- Clear & concise, tidak bertele-tele
- Update DocumentTypeId mapping dengan benar

---

## 🗺️ Roadmap Reference

### Phase 1 (✅ Complete)
- Natural conversation + memory
- Profit awareness
- Dashboard UI
- Settings UI
- Logging & CSV export
- pos.db integration

### Phase 2 (✅ Complete)
- Restock Engine (Purchase Document)
- Quick Inventory Engine (Inventory Count Document)
- History Tracking (Restock & Inventory)
- Auto-Recommendation Restock
- Critical Stock Notification
- Bulk Restock Support

### Phase 3 (✅ Complete)
- Role-Based Access (Owner/Kasir)
- Anti-Hallucination Prompt Engineering
- Fix DocumentTypeId Mapping
- Enhanced Error Handling

### Phase 4 (Coming Soon)
- OCR dengan Tesseract
- Google Sheets integration
- Background scheduler
- Automatic notifications
- Daily auto-report
- Installer + auto-start Windows

### Phase 5 (Future)
- Voice note support
- Supplier database
- Multi-cabang support
- Advanced analytics & charts
- WhatsApp integration (optional)

---

## 🛠️ Useful Commands

### Build & Run
```bash
# Development
dotnet run

# Build
dotnet build --configuration Release

# Publish (Single File)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Clean
dotnet clean

# Restore
dotnet restore
```

### File Locations
- **Source:** `D:\HOME\n8n Ai AGent\SmartSembakoAssistant\`
- **Config:** `config.json` (auto-generated)
- **Data:** `data\memory.db` (auto-created)
- **Logs:** `data\logs\` (auto-created)
- **Build Output:** `bin\Release\net8.0-windows\`
- **Publish Output:** `bin\Release\net8.0-windows\win-x64\publish\`

---

## 📞 Support & Resources

### Documentation
- PRD: `prd.md`
- User Guide: `README.md`
- Technical Docs: `TECHNICAL_DOCS.md`
- Quick Start: `QUICK_START.md`
- Phase Summary: `PHASE1_SUMMARY.md`
- Project Structure: `PROJECT_STRUCTURE.md`
- Changelog: `changelog.json`
- Restock Engine: `RESTOCK.md`
- Quick Inventory: `QUICK_INVENTORY.md`

### External Resources
- [WPF Documentation](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
- [.NET 8 Documentation](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [Groq API Docs](https://console.groq.com/docs)
- [Telegram Bot API](https://core.telegram.org/bots/api)
- [SQLite Documentation](https://www.sqlite.org/docs.html)

---

## 🎯 Acceptance Criteria

Setiap perubahan harus memenuhi:
- [ ] Fungsionalitas sesuai requirement
- [ ] Tidak ada regression di fitur lain
- [ ] Error handling lengkap
- [ ] Logging adequate
- [ ] Documentation updated
- [ ] Changelog updated
- [ ] Build successful (no errors)
- [ ] Manual testing passed
- [ ] DocumentTypeId mapping benar

---

**Last Updated:** April 2026  
**Version:** 2.3  
**Status:** Phase 3 Complete ✅

---

**Happy Coding! 🚀**