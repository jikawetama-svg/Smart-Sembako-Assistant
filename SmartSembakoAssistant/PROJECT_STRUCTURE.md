# 📁 Smart Sembako Assistant - Complete Project Structure v2.3

```
SmartSembakoAssistant/
│
├── 📄 App.xaml                           # Application entry point (WPF resources)
├── 📄 App.xaml.cs                        # Application startup logic + Scheduler
├──  AssemblyInfo.cs                    # Assembly metadata
├── 📄 MainWindow.xaml                    # Main window UI (sidebar + content)
├── 📄 MainWindow.xaml.cs                 # Main window logic & navigation
│
├── 📄 SmartSembakoAssistant.csproj       # Project file (.NET 8 WPF)
├──  SmartSembakoAssistant.sln          # Solution file
│
├── 📄 config.json                        # Active configuration (auto-generated)
├── 📄 config.template.json               # Configuration template
├── 📄 .gitignore                         # Git ignore rules
│
├── 📖 README.md                          # User guide & documentation
├── 📖 TECHNICAL_DOCS.md                  # Developer documentation
├── 📖 QUICK_START.md                     # Quick setup guide
├── 📖 PROJECT_STRUCTURE.md               # This file
├── 📖 AGENT.md                           # AI agent guidelines
├──  RESTOCK.md                         # Restock Engine documentation
├──  QUICK_INVENTORY.md                 # Quick Inventory Engine documentation
│
├── 📂 Models/                            # Data models
│   ├── 📄 AppConfig.cs                   # Configuration models
│   │   ├── AppConfig
│   │   ├── GroqSettings
│   │   ├── TelegramSettings
│   │   ├── PosDbSettings
│   │   ├── GoogleSheetsSettings
│   │   ├── MemorySettings
│   │   ├── NotificationSettings
│   │   ├── StockThreshold
│   │   └── ExpiryThreshold
│   │
│   ├── 📄 Product.cs                     # Product, Transaction, User, History models
│   │   ├── Product
│   │   ├── Transaction
│   │   ├── TransactionItem
│   │   ├── User
│   │   ├── StockMovement
│   │   ├── CustomerInfo
│   │   ├── RestockHistoryItem
│   │   ├── InventoryHistoryItem
│   │   ├── RestockRecommendation
│   │   └── RestockResult
│   │
│   └── 📄 Memory.cs                      # Memory & Log models
│       ├── Conversation
│       ├── LongTermMemory
│       ├── LogEntry
│       ├── OcrResult
│       ├── ParsedReceipt
│       └── ReceiptItem
│
├── 📂 Services/                          # Business logic services
│   ├── 📄 ConfigService.cs               # Configuration management
│   │   ├── LoadConfig()
│   │   ├── SaveConfig()
│   │   ├── GetEncryptedValue()           # DPAPI decryption
│   │   ├── SetEncryptedValue()           # DPAPI encryption
│   │   ├── UpdateGroqSettings()
│   │   ├── UpdateTelegramSettings()
│   │   ├── UpdatePosDbSettings()
│   │   └── IsConfigured()
│   │
│   ├── 📄 DatabaseService.cs             # SQLite operations
│   │   ├── InitializeDatabase()
│   │   ├── AddConversationAsync()
│   │   ├── GetRecentConversationsAsync()
│   │   ├── ClearOldConversationsAsync()
│   │   ├── AddLongTermMemoryAsync()
│   │   ├── GetLongTermMemoriesAsync()
│   │   ├── UpdateLongTermMemoryUsageAsync()
│   │   ├── AddLogAsync()
│   │   ├── GetLogsAsync()
│   │   ├── ClearOldLogsAsync()
│   │   ├── GetAppConfigValueAsync()
│   │   └── SetAppConfigValueAsync()
│   │
│   ├── 📄 LoggingService.cs              # Logging system
│   │   ├── LogInfoAsync()
│   │   ├── LogWarningAsync()
│   │   ├── LogErrorAsync()
│   │   ├── LogCriticalAsync()
│   │   ├── GetLogsAsync()
│   │   └── ExportLogsToCsvAsync()
│   │
│   ├── 📄 PosDbService.cs                # Aronium integration + Engines
│   │   ├── GetAllProductsAsync()
│   │   ├── GetProductByIdAsync()
│   │   ├── GetLowStockProductsAsync()
│   │   ├── GetExpiringProductsAsync()
│   │   ├── GetRecentTransactionsAsync()
│   │   ├── GetTodayRevenueAsync()
│   │   ├── GetTodayProfitAsync()
│   │   ├── GetAllUsersAsync()
│   │   ├── AutoDetectPosDbPath()
│   │   ├── IsValidPosDbPath()
│   │   ├── CreatePurchaseDocumentAsync() # Restock Engine
│   │   ├── CreateInventoryCountDocumentAsync() # Inventory Engine
│   │   ├── GetRestockHistoryAsync()
│   │   ├── GetInventoryHistoryAsync()
│   │   ├── GetAutoRestockRecommendationsAsync()
│   │   └── GetCriticalStockProductsAsync()
│   │
│   ├── 📄 GroqService.cs                 # AI service
│   │   ├── SendPromptAsync()
│   │   ├── SendGroqRequestAsync()
│   │   ├── SendGeminiRequestAsync()
│   │   ├── ParseReceiptAsync()
│   │   ├── GenerateRestockRecommendationAsync()
│   │   └── GenerateNaturalResponseAsync()
│   │
│   └──  TelegramBotService.cs          # Telegram bot
│       ├── StartAsync()
│       ├── StopAsync()
│       ├── SendMorningReportAsync()
│       ├── HandleUpdateAsync()
│       ├── HandleTextMessageAsync()
│       ├── HandleCommandAsync()
│       ├── HandlePhotoMessageAsync()
│       ├── HandleCallbackQueryAsync()
│       ├── SendHelpMessageAsync()
│       ├── HandleStockCommandAsync()
│       ├── HandleLaporanCommandAsync()
│       ├── HandleRestockCommandAsync()
│       ├── HandleBulkRestockCommandAsync()
│       ├── HandleInventoryCommandAsync()
│       ├── HandleAnalisaCommandAsync()
│       ├── HandleCekModalCommandAsync()
│       ├── HandleLaporanKasirCommandAsync()
│       ├── HandleDeadStockCommandAsync()
│       ├── HandleRestockHistoryCommandAsync()
│       ├── HandleInventoryHistoryCommandAsync()
│       ├── HandleAutoRestockRecommendationCommandAsync()
│       ├── HandleStockNotificationCommandAsync()
│       ├── IsChatAllowed()
│       ├── IsOwner()
│       ├── IsKasir()
│       ├── GetUserRole()
│       └── SendMessageAsync()
│
├── 📂 Views/                             # WPF UserControls
│   ├── 📄 DashboardView.xaml             # Dashboard UI
│   ├── 📄 DashboardView.xaml.cs          # Dashboard logic
│   │   ├── LoadDashboardData()
│   │   ├── BtnTestAI_Click()
│   │   ├── BtnSyncNow_Click()
│   │   └── BtnTestAll_Click()
│   │
│   ├── 📄 StockMonitoringView.xaml       # Stock monitoring UI
│   ├── 📄 StockMonitoringView.xaml.cs    # Stock monitoring logic
│   │   ├── LoadProducts()
│   │   ├── FilterProducts()
│   │   ├── TxtSearch_TextChanged()
│   │   ├── BtnAll_Click()
│   │   ├── BtnLowStock_Click()
│   │   ├── BtnExpiring_Click()
│   │   └── BtnRefresh_Click()
│   │
│   ├── 📄 LogsView.xaml                  # Log viewer UI
│   ├── 📄 LogsView.xaml.cs               # Log viewer logic
│   │   ├── LoadLogs()
│   │   ├── CmbLevel_SelectionChanged()
│   │   ├── CmbCategory_SelectionChanged()
│   │   └── BtnExport_Click()
│   │
│   ├── 📄 SettingsView.xaml              # Settings UI
│   └──  SettingsView.xaml.cs           # Settings logic
│       ├── LoadSettings()
│       ├── BtnBrowse_Click()
│       ├── ChkAutoDetect_Changed()
│       ├── BtnTestConnections_Click()
│       └── BtnSave_Click()
│
├── 📂 bin/                               # Build output (auto-generated)
│   └── Debug/
│       └── net8.0-windows/
│           ├── SmartSembakoAssistant.dll
│           ├── SmartSembakoAssistant.exe
│           └── [dependencies]
│
├── 📂 obj/                               # Intermediate files (auto-generated)
│   └── [build artifacts]
│
└── 📂 data/                              # Application data (created at runtime)
    ├── memory.db                         # SQLite database (auto-created)
    └── logs/                             # Log files
```

---

## 📊 Statistics

### Files Count
- **Models**: 3 files (20+ classes)
- **Services**: 6 files (100+ methods)
- **Views**: 8 files (4 XAML + 4 code-behind)
- **Documentation**: 8 markdown files
- **Configuration**: 3 files
- **Total**: ~40+ files

### Lines of Code
- **Models**: ~500 lines
- **Services**: ~3,500 lines
- **Views (XAML)**: ~800 lines
- **Views (Code)**: ~1,200 lines
- **Documentation**: ~2,000 lines
- **Total**: ~8,000+ lines

### Dependencies (NuGet Packages)
- Telegram.Bot (v19.0.0)
- Microsoft.Data.Sqlite (v8.0.0)
- Newtonsoft.Json (v13.0.3)
- Google.Apis.Sheets.v4 (v1.67.0.3393)
- Tesseract (v5.2.0)
- System.Security.Cryptography.ProtectedData (v8.0.0)
- Microsoft.Extensions.Http (v8.0.0)
- Microsoft.Extensions.DependencyInjection (v8.0.0)
- Microsoft.Extensions.Logging (v8.0.0)

---

## 🎯 Key Features by File

### Core Functionality
| File | Feature | Status |
|------|---------|--------|
| TelegramBotService.cs | Bot polling & command handler | ✅ Complete |
| GroqService.cs | AI integration + fallback | ✅ Complete |
| PosDbService.cs | Aronium database reader + Engines | ✅ Complete |
| DatabaseService.cs | Memory & logging | ✅ Complete |
| ConfigService.cs | Configuration management | ✅ Complete |
| LoggingService.cs | Logging system | ✅ Complete |

### Engines
| File | Feature | Status |
|------|---------|--------|
| PosDbService.cs | Restock Engine (Purchase Document) | ✅ Complete |
| PosDbService.cs | Inventory Engine (Inventory Count Document) | ✅ Complete |
| PosDbService.cs | Recommendation Engine | ✅ Complete |
| PosDbService.cs | History Engine | ✅ Complete |
| TelegramBotService.cs | Bulk Restock Handler | ✅ Complete |

### UI Components
| File | Feature | Status |
|------|---------|--------|
| MainWindow.xaml | Main window & navigation | ✅ Complete |
| DashboardView.xaml | Dashboard UI | ✅ Complete |
| StockMonitoringView.xaml | Stock table | ✅ Complete |
| LogsView.xaml | Log viewer | ✅ Complete |
| SettingsView.xaml | Configuration UI | ✅ Complete |

---

## 🔄 Data Flow

### 1. Application Startup
```
App.xaml.cs
  → MainWindow.xaml
    → Load DashboardView
      → ConfigService.LoadConfig()
      → DatabaseService.Initialize()
      → Update UI with status
      → SetupScheduler()
```

### 2. Telegram Message Flow
```
User sends message
  → TelegramBotService.HandleUpdateAsync()
    → Check whitelist (IsChatAllowed)
    → Save to DatabaseService.AddConversationAsync()
    → If command: HandleCommandAsync()
    → If text: GroqService.GenerateNaturalResponseAsync()
    → Save AI response to DatabaseService
    → Send reply via Telegram
```

### 3. AI Request Flow
```
User query
  → Get conversation history (DatabaseService)
  → Build prompt with context
  → GroqService.SendPromptAsync()
    → Try Groq API
      → Success: Return response
      → Error: Fallback to Gemini
  → Return response to user
```

### 4. Stock Check Flow
```
User: /stok or "Stok beras?"
  → TelegramBotService.HandleStockCommandAsync()
    → PosDbService.GetAllProductsAsync()
    → Filter by search query
    → Format response
    → Send to Telegram
```

### 5. Restock Flow
```
User: /restock minyak 50 14000
  → TelegramBotService.HandleRestockCommandAsync()
    → PosDbService.GetAllProductsAsync() (find product)
    → Show confirmation with inline keyboard
    → User clicks [YA]
    → TelegramBotService.HandleCallbackQueryAsync()
    → PosDbService.CreatePurchaseDocumentAsync()
      → Generate document number (26-100-NNNNNN)
      → Insert Document (Type 1)
      → Insert DocumentItem
      → Update Stock table
      → Commit transaction
    → Send success message
```

### 6. Inventory Flow
```
User: /inventory minyak -10
  → TelegramBotService.HandleInventoryCommandAsync()
    → PosDbService.GetAllProductsAsync() (find product)
    → Show confirmation with inline keyboard
    → User clicks [YA]
    → TelegramBotService.HandleCallbackQueryAsync()
    → PosDbService.CreateInventoryCountDocumentAsync()
      → Generate document number (26-300-NNNNNN)
      → Insert Document (Type 3)
      → Insert DocumentItem
      → Update Stock table
      → Commit transaction
    → Send success message
```

---

## 🔐 Security Points

| Location | Security Measure |
|----------|------------------|
| ConfigService.cs | DPAPI encryption for API keys |
| TelegramBotService.cs | Chat ID whitelist |
| TelegramBotService.cs | Role-Based Access (Owner/Kasir) |
| All Services | Async/await (non-blocking) |
| DatabaseService.cs | Parameterized queries (SQL injection prevention) |
| LoggingService.cs | Sensitive data masking |
| PosDbService.cs | Transaction usage for data integrity |

---

## 🚀 Build & Deploy

### Development
```bash
dotnet run
```

### Build
```bash
dotnet build --configuration Release
```

### Publish (Single File)
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Output Location
```
bin/Release/net8.0-windows/win-x64/publish/SmartSembakoAssistant.exe
```

---

## 📝 Configuration Files

### config.json (Active)
- User-specific configuration
- Auto-created from template
- Encrypted API keys (optional)

### config.template.json
- Template with placeholders
- Version controlled
- Safe to commit to git

### .gitignore
- Excludes config.json
- Excludes bin/obj folders
- Excludes database files
- Excludes logs

---

## 🎨 UI Theme

### Colors
- Primary: `#2E7D32` (Green)
- Primary Light: `#4CAF50`
- Accent: `#FF9800` (Orange)
- Background: `#F5F5F5`
- Card: `#FFFFFF`

### Fonts
- Default: System font (Segoe UI)
- Sizes: 9px - 24px

### Components
- Cards with shadow
- Sidebar navigation
- DataGrid with alternating rows
- Buttons with hover effects

---

## 🧪 Testing Checklist

### Manual Testing
- [ ] Application starts without error
- [ ] Dashboard loads with correct data
- [ ] Settings can be saved
- [ ] Test All Connections works
- [ ] Telegram bot starts
- [ ] Commands work (/start, /stok, /laporan, etc.)
- [ ] Natural language conversation works
- [ ] Stock monitoring shows data
- [ ] Logs can be exported to CSV
- [ ] pos.db connection works (if Aronium installed)
- [ ] Restock Engine creates Purchase documents correctly
- [ ] Inventory Engine creates Inventory Count documents correctly
- [ ] History tracking works
- [ ] Auto-recommendations work
- [ ] Bulk operations work
- [ ] Role-Based Access works

### Integration Testing
- [ ] Groq API responds
- [ ] Gemini fallback works
- [ ] Database CRUD operations work
- [ ] Memory system stores conversations
- [ ] Logs are written correctly
- [ ] DocumentTypeId mapping is correct

---

## 📚 Next Steps (Phase 4)

### Files to Add
```
Services/
  ├── OcrService.cs              # Tesseract integration
  ├── GoogleSheetsService.cs     # Sheets integration
  └── SchedulerService.cs        # Background tasks

Views/
  └── ReceiptPreviewView.xaml    # OCR preview UI
  └── ReceiptPreviewView.xaml.cs
```

### Features to Implement
- OCR photo processing
- Google Sheets sync
- Automatic notifications
- Daily summary scheduler
- Voice note handler
- Installer + auto-start Windows

---

**Last Updated**: April 2026
**Version**: 2.3
**Status**: Phase 3 Complete ✅
