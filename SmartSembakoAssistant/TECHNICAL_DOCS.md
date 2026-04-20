# 📚 Smart Sembako Assistant - Technical Documentation v2.3

## Architecture Overview

### Technology Stack
- **Framework**: WPF (Windows Presentation Foundation) .NET 8
- **Language**: C# 12
- **Database**: SQLite (Microsoft.Data.Sqlite)
- **AI**: Groq API (LLaMA 3.1 70B) + Gemini Fallback
- **Bot**: Telegram.Bot library
- **OCR**: Tesseract (Phase 4)
- **Sheets**: Google.Apis.Sheets.v4 (Phase 4)

### Project Structure
```
SmartSembakoAssistant/
├── Models/                    # Data models
│   ├── AppConfig.cs          # Configuration models
│   ├── Product.cs            # Product, Transaction, User, History models
│   └── Memory.cs             # Memory & Log models
│
├── Services/                  # Business logic
│   ├── ConfigService.cs      # Configuration management with DPAPI encryption
│   ├── DatabaseService.cs    # SQLite CRUD operations
│   ├── LoggingService.cs     # Logging & CSV export
│   ├── PosDbService.cs       # Aronium integration + Restock/Inventory Engines
│   ├── GroqService.cs        # Groq AI + Gemini fallback
│   └── TelegramBotService.cs # Telegram bot handler
│
├── Views/                     # WPF UserControls
│   ├── DashboardView.xaml    # Main dashboard
│   ├── StockMonitoringView.xaml
│   ├── LogsView.xaml
│   └── SettingsView.xaml
│
├── MainWindow.xaml           # Main application window
└── App.xaml                  # Application entry point
```

## Database Schema

### memory.db (Local SQLite)

#### Table: conversations
Stores short-term conversation memory
```sql
CREATE TABLE conversations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    chat_id INTEGER NOT NULL,
    user_name TEXT,
    role TEXT NOT NULL,          -- 'user' or 'assistant'
    message TEXT NOT NULL,
    timestamp TEXT NOT NULL,
    message_type TEXT            -- 'text', 'command', 'photo', 'voice'
);

CREATE INDEX idx_conversations_chat_id ON conversations(chat_id);
CREATE INDEX idx_conversations_timestamp ON conversations(timestamp);
```

#### Table: long_term_memory
Stores learned patterns and habits
```sql
CREATE TABLE long_term_memory (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    category TEXT NOT NULL,      -- 'habits', 'preferences', 'patterns'
    summary TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT,
    usage_count INTEGER DEFAULT 1
);
```

#### Table: logs
Application logging
```sql
CREATE TABLE logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    level TEXT NOT NULL,         -- 'Info', 'Warning', 'Error', 'Critical'
    category TEXT NOT NULL,      -- 'Command', 'OCR', 'AI', 'Notification', etc.
    message TEXT NOT NULL,
    details TEXT,
    user_id TEXT
);

CREATE INDEX idx_logs_timestamp ON logs(timestamp);
CREATE INDEX idx_logs_category ON logs(category);
```

#### Table: app_config
Application configuration cache
```sql
CREATE TABLE app_config (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    key TEXT NOT NULL UNIQUE,
    value TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
```

## Services Architecture

### 1. ConfigService
**Purpose**: Manage application configuration with encryption

**Key Methods**:
- `LoadConfig()` - Load from JSON file
- `SaveConfig()` - Save to JSON file
- `GetEncryptedValue()` - DPAPI decryption
- `SetEncryptedValue()` - DPAPI encryption
- `UpdateGroqSettings()` - Update Groq config
- `UpdateTelegramSettings()` - Update Telegram config
- `IsConfigured()` - Check if all required keys exist

**Security**:
- Uses Windows DPAPI for encryption
- Fallback to plain text if DPAPI fails (development mode)
- API keys masked in logs

### 2. DatabaseService
**Purpose**: Local SQLite operations for memory & logging

**Key Methods**:
- `InitializeDatabase()` - Create tables & indexes
- `AddConversationAsync()` - Save conversation
- `GetRecentConversationsAsync()` - Get chat history
- `AddLongTermMemoryAsync()` - Save learned patterns
- `GetLongTermMemoriesAsync()` - Retrieve memories
- `AddLogAsync()` - Add log entry
- `GetLogsAsync()` - Query logs with filters
- `ClearOldConversationsAsync()` - Cleanup old data
- `ClearOldLogsAsync()` - Cleanup old logs

### 3. LoggingService
**Purpose**: Application-wide logging with CSV export

**Key Methods**:
- `LogInfoAsync()` - Information log
- `LogWarningAsync()` - Warning log
- `LogErrorAsync()` - Error log
- `LogCriticalAsync()` - Critical error log
- `GetLogsAsync()` - Query logs
- `ExportLogsToCsvAsync()` - Export to CSV file

**Log Categories**:
- `Command` - Telegram command processing
- `OCR` - OCR processing
- `AI` - AI request/response
- `Notification` - Automatic notifications
- `Anomaly` - Anomaly detection
- `System` - System events
- `Telegram` - Telegram bot events

### 4. PosDbService
**Purpose**: Read data from Aronium pos.db & Execute Restock/Inventory Engines

**Key Methods**:
- `GetAllProductsAsync()` - Get all products
- `GetProductByIdAsync()` - Get single product
- `GetLowStockProductsAsync()` - Products with low stock
- `GetExpiringProductsAsync()` - Products near expiry
- `GetRecentTransactionsAsync()` - Recent transactions
- `GetTodayRevenueAsync()` - Today's revenue
- `GetTodayProfitAsync()` - Today's profit (margin-based)
- `GetAllUsersAsync()` - Users from pos.db
- `AutoDetectPosDbPath()` - Auto-find pos.db location
- `IsValidPosDbPath()` - Validate path
- `CreatePurchaseDocumentAsync()` - **Restock Engine**: Creates Purchase document (Type 1)
- `CreateInventoryCountDocumentAsync()` - **Inventory Engine**: Creates Inventory Count document (Type 3)
- `GetRestockHistoryAsync()` - Get restock history for product
- `GetInventoryHistoryAsync()` - Get inventory history for product
- `GetAutoRestockRecommendationsAsync()` - Auto-recommend restock based on low stock
- `GetCriticalStockProductsAsync()` - Get products with zero/negative stock

**DocumentTypeId Mapping**:
- `1`: Purchase (Restock) - TypeCode 100
- `2`: Sales - TypeCode 200
- `3`: Inventory Count - TypeCode 300
- `4`: Refund - TypeCode 220
- `5`: Stock Return - TypeCode 120
- `6`: Loss - TypeCode 400

**Note**: Struktur tabel Aronium mungkin berbeda antar versi. Service ini menggunakan asumsi schema umum. Jika error, sesuaikan query dengan schema actual.

### 5. GroqService
**Purpose**: AI integration dengan Groq + Gemini fallback

**Key Methods**:
- `SendPromptAsync()` - Generic AI prompt
- `SendGroqRequestAsync()` - Direct Groq API call
- `SendGeminiRequestAsync()` - Direct Gemini API call
- `ParseReceiptAsync()` - OCR receipt parsing
- `GenerateRestockRecommendationAsync()` - Smart restock suggestions
- `GenerateNaturalResponseAsync()` - Natural conversation

**AI Prompt Strategy**:

1. **Natural Conversation**:
   - System prompt defines persona & capabilities
   - Includes conversation history (8 messages)
   - Temperature: 0.7 (creative but focused)
   - Max tokens: 500

2. **Restock Recommendation**:
   - Includes low stock products
   - Includes expiring products
   - Includes today's revenue & profit
   - Considers margin per product
   - Temperature: 0.7
   - Max tokens: 600

3. **Receipt Parsing**:
   - Strict JSON output format
   - Temperature: 0.3 (very focused)
   - Max tokens: 500

**Error Handling**:
- Automatic fallback to Gemini if Groq fails
- User-friendly error messages
- Detailed logging
- Specific handling for 401 (Unauthorized) and 404 (Not Found)

### 6. TelegramBotService
**Purpose**: Telegram bot handler dengan command & natural language

**Key Methods**:
- `StartAsync()` - Start bot dengan polling
- `StopAsync()` - Stop bot gracefully
- `HandleUpdateAsync()` - Process incoming updates
- `HandleTextMessageAsync()` - Process text messages
- `HandleCommandAsync()` - Process slash commands
- `HandlePhotoMessageAsync()` - Process photos (OCR)
- `SendHelpMessageAsync()` - Send help text
- `HandleStockCommandAsync()` - /stok command
- `HandleLaporanCommandAsync()` - /laporan command
- `HandleRestockCommandAsync()` - /restock command
- `HandleInventoryCommandAsync()` - /inventory command
- `HandleAnalisaCommandAsync()` - /analisa command
- `HandleCekModalCommandAsync()` - /cek_modal command
- `HandleLaporanKasirCommandAsync()` - /laporan_kasir command
- `HandleDeadStockCommandAsync()` - /dead_stock command
- `HandleRestockHistoryCommandAsync()` - /riwayat_restock command
- `HandleInventoryHistoryCommandAsync()` - /riwayat_inventory command
- `HandleAutoRestockRecommendationCommandAsync()` - /rekomendasi_restock command
- `HandleStockNotificationCommandAsync()` - /notifikasi_stok command
- `HandleBulkRestockCommandAsync()` - Bulk restock handler
- `IsChatAllowed()` - Check whitelist
- `SendMessageAsync()` - Send message to chat

**Command Handler**:
```
/start, /help     → Help message
/stok [query]     → Stock check (with search)
/laporan          → Daily report
/restock [p] [q] [h] → Restock product
/inventory [p] [q] → Quick inventory
/riwayat_restock [p] → Restock history
/riwayat_inventory [p] → Inventory history
/rekomendasi_restock → Auto restock recommendations
/notifikasi_stok → Critical stock check
/analisa          → Business analysis
/cek_modal        → Check zero-cost products
/laporan_kasir    → Cashier performance
/dead_stock       → Dead stock check
```

**Natural Language Flow**:
1. User sends message
2. Save to conversation history
3. Get recent history (8 messages)
4. Send to Groq with context
5. Save AI response to history
6. Send response to user

## WPF UI Architecture

### MainWindow
Main window dengan sidebar navigation
- **Left Sidebar**: Navigation + Bot control
- **Right Content**: Dynamic content area

**Navigation**:
- Dashboard
- Monitoring Stok
- Log & Analitik
- Settings

### DashboardView
Main dashboard dengan status cards & quick insights
- Status: Bot, Groq, Database, Memory
- Quick Insights: Revenue, Profit, Critical Stock
- Quick Actions: Test AI, Sync, Test All
- Recent Conversations

### StockMonitoringView
Product monitoring dengan DataGrid
- Search functionality
- Filter: All, Low Stock, Expiring
- Sortable columns
- Refresh button

### LogsView
Log viewer dengan filters
- Filter by level & category
- Export to CSV
- Statistics display

### SettingsView
Configuration UI
- Groq AI settings
- Telegram Bot settings
- Database settings
- Notification settings
- Test connections button
- Save settings

## Security Considerations

### API Key Encryption
```csharp
// DPAPI encryption
byte[] encrypted = ProtectedData.Protect(
    Encoding.UTF8.GetBytes(plainText),
    null,
    DataProtectionScope.CurrentUser);

// DPAPI decryption
byte[] decrypted = ProtectedData.Unprotect(
    Convert.FromBase64String(encryptedText),
    null,
    DataProtectionScope.CurrentUser);
```

### Chat Whitelist
- Allowed Chat IDs configurable
- If empty, all chats allowed (development mode)
- Recommended: Set specific Chat IDs

### Role-Based Access
- **Owner**: Full access (restock, inventory, history, recommendations)
- **Kasir**: View only (stock, reports)
- Configurable via `OwnerChatIds` and `KasirChatIds` in config.json

### Rate Limiting
- Configurable rate limit (default: 5 seconds)
- Prevents spam & abuse
- Future: Implement actual rate limiter

## Performance Optimization

### Database
- Indexes on frequently queried columns
- Async/await for all DB operations
- Connection pooling (automatic by SQLite)
- Cleanup old data periodically
- Transaction usage for Restock/Inventory engines

### AI Requests
- Timeout: 30 seconds
- Fallback to Gemini on error
- Conversation history limited to 8 messages
- Max tokens controlled per use case (500-600)

### UI
- Async data loading
- DataGrid virtualization
- ScrollViewer for large lists
- Loading indicators

## Error Handling Strategy

### Service Level
- Try-catch dengan detailed logging
- User-friendly error messages
- Fallback mechanisms (Groq → Gemini)
- Specific handling for API errors (401, 404, 429)

### UI Level
- Disable buttons during processing
- Loading states
- Error dialogs dengan actionable messages
- Graceful degradation

## Testing Strategy

### Manual Testing
1. Test all Telegram commands
2. Test natural language conversation
3. Test database connection
4. Test AI response
5. Test settings save & reload
6. Test log export
7. Test restock/inventory engines
8. Test bulk operations

### Integration Testing
- Test pos.db read operations
- Test Groq API calls
- Test Telegram bot polling
- Test database CRUD
- Test document creation (Purchase/Inventory Count)

## Deployment

### Publish Command
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Output
- Single executable file
- All dependencies included
- No external installation required
- Portable (copy folder to another PC)

### First Run
1. Copy folder ke PC tujuan
2. Edit config.json atau gunakan Settings UI
3. Jalankan SmartSembakoAssistant.exe
4. Test all connections
5. Start bot

## Future Enhancements (Phase 4-5)

### Phase 4
- OCR dengan Tesseract
- Google Sheets integration
- Background scheduler
- Automatic notifications
- Daily auto-report
- Installer + auto-start Windows

### Phase 5
- Voice note support
- Supplier database
- Multi-cabang support
- Advanced charts & analytics
- WhatsApp integration (optional)

## Troubleshooting Guide

### Common Issues

**Issue**: Bot tidak start
- **Cause**: Invalid token atau network issue
- **Solution**: Cek token, test koneksi internet

**Issue**: pos.db not found
- **Cause**: Aronium tidak terinstall atau path salah
- **Solution**: Auto-detect atau browse manual

**Issue**: AI timeout
- **Cause**: Groq quota habis atau network issue
- **Solution**: Cek quota Groq, enable Gemini fallback

**Issue**: Database locked
- **Cause**: Multiple connections
- **Solution**: Restart aplikasi atau tutup Aronium sementara

**Issue**: DocumentTypeId error
- **Cause**: Wrong type code used in queries
- **Solution**: Use Id (1, 2, 3) not TypeCode (100, 200, 300)

## API Reference

### Groq API
- **Endpoint**: `https://api.groq.com/openai/v1/chat/completions`
- **Auth**: Bearer token
- **Model**: `llama-3.1-70b-versatile`

### Gemini API
- **Endpoint**: `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
- **Auth**: API key in URL
- **Model**: `gemini-1.5-flash`

### Telegram Bot API
- **Library**: Telegram.Bot (C#)
- **Method**: Long polling
- **Updates**: Message, Photo, Voice (future)

---

**Last Updated**: April 2026
**Version**: 2.3