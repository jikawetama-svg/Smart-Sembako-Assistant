# 🚀 UPDATE PLAN - Smart Sembako Assistant v3.0

**Tanggal:** 10 April 2026
**Status:** Planning Phase
**Target:** Production Ready System

---

## 📋 RINGKASAN EKSEKUTIF

Upgrade dari "AI Assistant yang jalan" menjadi **Sistem Manajemen Toko Siap Produksi** dengan:
- ✅ Bot cepat & stabil (tanpa dependensi AI)
- ✅ AI cerdas sebagai analis (bukan eksekutor)
- ✅ **Dashboard Admin GUI yang sudah ada** (dipoles, bukan buat dari nol)
- ✅ **AI Fallback custom** (Groq primary → Gemini fallback → Rule-based)
- ✅ **Custom API Key** (bisa edit/ganti dari Dashboard)
- ✅ Validation Engine (anti data kacau)
- ✅ Auto-switch AI (fallback otomatis saat error)
- ✅ Logging & Audit Trail lengkap
- ✅ Scheduler & Auto Report

---

## 🏗️ 1. ARSITEKTUR FINAL

### Prinsip Utama
```
USER (Telegram)
   ↓
COMMAND ROUTER (Bot)
   ↓
VALIDATION ENGINE ⚠️
   ↓
CONFIRMATION LAYER ✅
   ↓
EXECUTION ENGINE (DB Aronium)
   ↓
EVENT BUS
   ↓
AI + ANALYTICS 🧠
   ↓
LOGGING + REPORT
```

### Pembagian Peran
| Komponen | Tugas | Teknologi |
|----------|-------|-----------|
| **Bot** | Interface user, parse command | Telegram.Bot |
| **Core Engine** | Validasi, eksekusi DB | C# Services |
| **AI Layer** | Analisa, insight, rekomendasi | Groq/Gemini |
| **Scheduler** | Auto report, monitoring | C# Timer/Task |
| **Dashboard** | Monitoring, settings | WPF/Web |

### Aturan Emas
1. ❌ **AI TIDAK BOLEH** langsung insert DB
2. ✅ **Semua transaksi** via Core Engine
3. ✅ **AI hanya** analisa & rekomendasi
4. ✅ **Sistem jalan** walau AI mati

---

## 🎯 2. FITUR YANG SUDAH SELESAI (v2.x)

### ✅ Phase 1 - Core AI Assistant
- [x] Natural conversation dengan memory
- [x] Groq API + Gemini fallback (basic)
- [x] **Dashboard Views** (DashboardView, StockMonitoringView, SettingsView, LogsView)
- [x] Logging system
- [x] pos.db integration (READ-ONLY)

### ✅ Phase 2 - Restock & Inventory
- [x] Restock Engine (Document Type 100)
- [x] Quick Inventory (Document Type 300) - **SET mode**
- [x] History tracking
- [x] Bulk operations
- [x] Aronium compatibility (InternalNote, Price, dll)

### ✅ Phase 3 - Role-Based & Anti-Hallucination
- [x] Role-Based Access (Owner/Kasir)
- [x] Anti-Hallucination Prompt
- [x] DocumentTypeId mapping fix
- [x] Auto-recommendation restock
- [x] Critical stock notification

### ✅ Phase 3.1 - Inventory Logic Fix (CRITICAL)
- [x] Ubah concept dari ADD ke SET
- [x] selisih = target - currentStock
- [x] Message consistency (qty=0 → "RESET STOK")

### 🔄 Yang Perlu Dipoles (Bukan Buat Baru)
- [ ] DashboardView: Tambah widget status & quick stats
- [ ] SettingsView: Tambah section AI Fallback & custom API key
- [ ] StockMonitoringView: Tambah filter & quick stats
- [ ] LogsView: Tambah filter, export, & cleanup

---

## 🔥 3. FITUR BARU (v3.0 - Planned)

### 3.1 VALIDATION ENGINE (Anti Data Kacau)

#### Rule Validasi
```csharp
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string Level { get; set; } // "NORMAL", "WARNING", "DANGER"
    public string Message { get; set; }
}

public ValidationResult ValidateInventory(int productId, int targetStock)
{
    var current = GetCurrentStock(productId);
    var selisih = targetStock - current;
    
    // Rule 1: Tidak boleh negatif besar
    if (targetStock < -10)
        return new ValidationResult { 
            Level = "DANGER", 
            Message = "Stok target terlalu kecil, akan jadi minus besar" 
        };
    
    // Rule 2: Lonjakan tidak wajar (> 3x stok normal)
    if (Math.Abs(selisih) > Math.Abs(current) * 3)
        return new ValidationResult { 
            Level = "WARNING", 
            Message = "Perubahan besar terdeteksi, kemungkinan salah input" 
        };
    
    // Rule 3: Selisih terlalu besar (> 100)
    if (Math.Abs(selisih) > 100)
        return new ValidationResult { 
            Level = "WARNING", 
            Message = "Selisih perubahan sangat besar" 
        };
    
    return new ValidationResult { Level = "NORMAL", IsValid = true };
}
```

#### Output ke User
```
⚠️ PERINGATAN - PERUBAHAN BESAR

Stok sekarang: 20
Target: 500
Selisih: +480

Ini tidak normal untuk produk ini.

Kemungkinan:
• Salah input (typo)
• Maksudnya /restock bukan /inventory

[LANJUTKAN] [BATAL]
```

#### Anti Double Input
```csharp
private Dictionary<long, DateTime> _lastCommandTime = new();

public bool IsDuplicateCommand(long chatId, string command, TimeSpan window)
{
    if (_lastCommandTime.ContainsKey(chatId))
    {
        var lastTime = _lastCommandTime[chatId];
        if (DateTime.Now - lastTime < window)
            return true; // Duplicate!
    }
    
    _lastCommandTime[chatId] = DateTime.Now;
    return false;
}
```

---

### 3.2 AI AUTO-SWITCH ENGINE (DENGAN CUSTOM API KEY)

#### Konsep Fallback
```
User Request
   ↓
Try AI Primary (Groq)
   ↓ [Error/Timeout]
Retry 2x
   ↓ [Still Error]
Try AI Fallback (Gemini)
   ↓ [Error/Timeout]
Use Rule-Based Fallback Engine
   ↓
Return Response
```

#### AI Manager Implementation
```csharp
public class AIManager
{
    private AIStatus _status = AIStatus.ACTIVE;
    private DateTime _lastFail = DateTime.MinValue;
    private int _retryCount = 0;
    private string _activeProvider = "Groq"; // "Groq" or "Gemini"
    
    public AIStatus Status => _status;
    public string ActiveProvider => _activeProvider;
    
    public async Task<string> AskAI(string prompt)
    {
        // Cek status dulu
        if (_status == AIStatus.FALLBACK)
        {
            // Auto recovery setelah 5 menit
            if (DateTime.Now - _lastFail > TimeSpan.FromMinutes(5))
            {
                _status = AIStatus.RETRYING;
            }
            else
            {
                return FallbackResponse(prompt);
            }
        }
        
        // Try Primary AI (Groq)
        for (int i = 0; i < 2; i++)
        {
            try
            {
                var result = await CallGroqAPI(prompt);
                _status = AIStatus.ACTIVE;
                _activeProvider = "Groq";
                _retryCount = 0;
                return result;
            }
            catch (Exception ex)
            {
                _lastFail = DateTime.Now;
                _retryCount++;
                _loggingService.LogWarning($"Groq attempt {i+1} failed: {ex.Message}", "AI");
                await Task.Delay(500); // Wait before retry
            }
        }
        
        // Try Fallback AI (Gemini)
        _loggingService.LogInfo("Switching to Gemini fallback", "AI");
        for (int i = 0; i < 2; i++)
        {
            try
            {
                var result = await CallGeminiAPI(prompt);
                _status = AIStatus.ACTIVE;
                _activeProvider = "Gemini";
                _retryCount = 0;
                return result;
            }
            catch (Exception ex)
            {
                _lastFail = DateTime.Now;
                _retryCount++;
                _loggingService.LogWarning($"Gemini attempt {i+1} failed: {ex.Message}", "AI");
                await Task.Delay(500);
            }
        }
        
        // All AI failed - use rule-based fallback
        _status = AIStatus.FALLBACK;
        _activeProvider = "Rule-Based";
        _loggingService.LogWarning("All AI providers failed, using rule-based fallback", "AI");
        return FallbackResponse(prompt);
    }
}
```

#### Custom API Key Configuration
```csharp
public class AIConfig
{
    // Primary AI (Groq)
    public bool GroqEnabled { get; set; } = true;
    public string GroqApiKey { get; set; } = ""; // Encrypted
    public string GroqModel { get; set; } = "llama-3.1-70b-versatile";
    public double GroqTemperature { get; set; } = 0.7;
    public int GroqMaxTokens { get; set; } = 1000;
    public int GroqTimeoutMs { get; set; } = 30000;
    
    // Fallback AI (Gemini)
    public bool GeminiEnabled { get; set; } = true;
    public string GeminiApiKey { get; set; } = ""; // Encrypted
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
    public double GeminiTemperature { get; set; } = 0.7;
    public int GeminiMaxTokens { get; set; } = 1000;
    public int GeminiTimeoutMs { get; set; } = 30000;
    
    // Behavior
    public bool AutoSwitchToFallback { get; set; } = true;
    public int RetryCount { get; set; } = 2;
    public int AutoRecoveryMinutes { get; set; } = 5;
    public bool CacheEnabled { get; set; } = true;
    public int CacheTtlMinutes { get; set; } = 60;
}
```

#### Fallback Engine (Rule-Based)
```csharp
public class FallbackEngine
{
    private Dictionary<string, string> _cache = new();
    
    public string GenerateResponse(string userMessage, DataContext context)
    {
        // Smart cache
        string cacheKey = $"{userMessage}_{context.Hash}";
        if (_cache.ContainsKey(cacheKey))
            return _cache[cacheKey];
        
        string response;
        
        // Rule-based analysis
        if (userMessage.Contains("restock") || userMessage.Contains("rekomendasi"))
            response = GenerateRestockRecommendation(context);
        
        else if (userMessage.Contains("minus") || userMessage.Contains("habis"))
            response = GetNegativeStockReport(context);
        
        else if (userMessage.Contains("laporan") || userMessage.Contains("omzet"))
            response = GenerateDailyReport(context);
        
        else if (userMessage.Contains("top") || userMessage.Contains("terlaris"))
            response = GetTopSellingProducts(context);
        
        else
            response = "Mode standar aktif. Gunakan command: /stok, /laporan, /restock, /analisa";
        
        // Cache result
        if (_cache.Count < 1000) // Limit cache size
            _cache[cacheKey] = response;
        
        return response;
    }
}
```

#### UX Message
```
🟢 AI Aktif (Groq): "🧠 AI Insight aktif (Groq)"
🟡 AI Aktif (Gemini): "🧠 AI Insight aktif (Gemini)"
🟠 Fallback: "⚙️ Mode standar aktif"
🔴 Error: "❌ AI sedang gangguan, coba lagi nanti"
```

#### Dashboard - AI Status Indicator
```
┌─────────────────────────────────────┐
│  AI STATUS                          │
├─────────────────────────────────────┤
│  Provider: 🟢 Groq (Active)         │
│  Fallback: 🟢 Gemini (Ready)        │
│                                     │
│  Requests Today: 1,234              │
│  Success Rate: 98.5%                │
│  Avg Response Time: 2.3s            │
│  Last Error: None                   │
│                                     │
│  [Refresh] [Clear Cache]            │
└─────────────────────────────────────┘
```

---

### 3.3 EVENT BUS SYSTEM

#### Event Types
```csharp
public enum EventType
{
    STOCK_UPDATED,
    RESTOCK_ADDED,
    INVENTORY_ADJUSTED,
    SALE_COMPLETED,
    AI_FALLBACK,
    ERROR_OCCURRED
}

public class StockEvent
{
    public EventType Type { get; set; }
    public string ProductName { get; set; }
    public decimal Before { get; set; }
    public decimal After { get; set; }
    public string User { get; set; }
    public DateTime Timestamp { get; set; }
}
```

#### Event Publisher
```csharp
public class EventBus
{
    private List<Action<StockEvent>> _subscribers = new();
    
    public void Subscribe(Action<StockEvent> handler)
    {
        _subscribers.Add(handler);
    }
    
    public async Task Publish(StockEvent evt)
    {
        foreach (var handler in _subscribers)
        {
            await Task.Run(() => handler(evt));
        }
    }
}

// Usage
eventBus.Subscribe(async (evt) => {
    await loggingService.LogEvent(evt);
    await googleSheetsService.UpdateStockLog(evt);
    if (evt.After < 0) await telegramBot.SendAlert(evt);
});
```

---

### 3.4 SMART RECOMMENDATION ENGINE

#### Restock Recommendation
```csharp
public class RestockRecommendation
{
    public string ProductName { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal AvgDailySales { get; set; }
    public decimal RecommendedQty { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Priority { get; set; } // "HIGH", "MEDIUM", "LOW"
}

public async Task<List<RestockRecommendation>> GetRecommendations()
{
    var products = await GetAllProductsAsync();
    var recommendations = new List<RestockRecommendation>();
    
    foreach (var product in products)
    {
        // Hitung rata-rata penjualan 7 hari terakhir
        var salesData = await GetSalesLast7Days(product.Id);
        var avgDaily = salesData.TotalQuantity / 7;
        
        // Safety stock = 3 hari
        var safetyStock = avgDaily * 3;
        
        // Lead time supplier = 2 hari
        var leadTimeDemand = avgDaily * 2;
        
        // Recommended = (safety stock + lead time demand) - current stock
        var recommended = (safetyStock + leadTimeDemand) - product.Stock;
        
        if (recommended > 0)
        {
            recommendations.Add(new RestockRecommendation
            {
                ProductName = product.Name,
                CurrentStock = product.Stock,
                AvgDailySales = avgDaily,
                RecommendedQty = Math.Ceiling(recommended),
                EstimatedCost = recommended * (product.PurchasePrice ?? 0),
                Priority = product.Stock <= safetyStock ? "HIGH" : "MEDIUM"
            });
        }
    }
    
    return recommendations.OrderByDescending(r => r.Priority).ToList();
}
```

#### Dead Stock Detection
```csharp
public async Task<List<Product>> GetDeadStockProducts()
{
    // Produk tidak laku > 14 hari
    var sql = @"
        SELECT p.Id, p.Name, p.Stock
        FROM Product p
        LEFT JOIN DocumentItem di ON di.ProductId = p.Id
        LEFT JOIN Document d ON d.Id = di.DocumentId
        WHERE d.DocumentTypeId = 200 -- Sales
        AND d.Date >= date('now', '-14 days')
        GROUP BY p.Id
        HAVING COUNT(di.Id) = 0
        AND p.Stock > 0
    ";
    
    return await QueryProductsAsync(sql);
}
```

---

### 3.5 SCHEDULER & AUTO REPORT

#### Daily Report (07:00)
```csharp
public class ReportScheduler
{
    private Timer _timer;
    
    public void Start()
    {
        // Cek setiap 1 menit
        _timer = new Timer(60000);
        _timer.Elapsed += async (sender, e) => await CheckAndRunReports();
        _timer.Start();
    }
    
    private async Task CheckAndRunReports()
    {
        var now = DateTime.Now;
        
        // Daily report at 07:00
        if (now.Hour == 7 && now.Minute == 0)
        {
            await SendDailyReport();
        }
        
        // Weekly report on Sunday at 20:00
        if (now.DayOfWeek == DayOfWeek.Sunday && now.Hour == 20 && now.Minute == 0)
        {
            await SendWeeklyReport();
        }
    }
    
    private async Task SendDailyReport()
    {
        var revenue = await PosDbService.GetYesterdayRevenueAsync();
        var profit = await PosDbService.GetYesterdayProfitAsync();
        var transactions = await PosDbService.GetYesterdayTransactionCountAsync();
        var negativeStock = await PosDbService.GetNegativeStockCountAsync();
        var lowStock = await PosDbService.GetLowStockCountAsync();
        
        var message = $@"📊 **LAPORAN HARIAN**

💰 Omzet Kemarin: Rp {revenue:N0}
📈 Profit: Rp {profit:N0}
🧾 Transaksi: {transactions} nota

🚨 Stok Minus: {negativeStock} produk
⚠️ Stok Rendah: {lowStock} produk

🔝 Top Produk:
{(await GetTopProductsAsync(3))}";

        await TelegramBot.SendMessageToOwners(message);
    }
}
```

---

### 3.6 GOOGLE SHEETS INTEGRATION

#### Sheet Structure
```
Sheet 1: STOCK LOG
| Tanggal | Produk | Sebelum | Sesudah | Selisih | Tipe | User |

Sheet 2: DAILY REPORT
| Tanggal | Omzet | Profit | Transaksi | Stok Minus |

Sheet 3: ANOMALI
| Tanggal | Produk | Issue | Level | Status |

Sheet 4: RECOMMENDATION
| Produk | Stok | Avg Sales | Rekomendasi | Prioritas |
```

#### Implementation
```csharp
public class GoogleSheetsService
{
    private SheetsService _sheetsService;
    private string _spreadsheetId;
    
    public async Task LogStockChange(StockEvent evt)
    {
        var row = new List<object>
        {
            evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            evt.ProductName,
            evt.Before,
            evt.After,
            evt.After - evt.Before,
            evt.Type.ToString(),
            evt.User
        };
        
        await AppendRowAsync("STOCK LOG", row);
    }
}
```

---

### 3.7 UNDO SYSTEM

#### Undo Last Transaction
```csharp
public async Task<UndoResult> UndoLastTransaction()
{
    // Cari dokumen terakhir yang dibuat user ini
    var lastDoc = await GetLastDocumentByUserAsync(currentUserId);
    
    if (lastDoc == null)
        return UndoResult.Failed("Tidak ada transaksi untuk di-undo");
    
    // Cek apakah sudah terlalu lama (> 5 menit)
    if (DateTime.Now - lastDoc.DateCreated > TimeSpan.FromMinutes(5))
        return UndoResult.Failed("Sudah terlalu lama untuk undo");
    
    // Delete document (cascade ke DocumentItem)
    await DeleteDocumentAsync(lastDoc.Id);
    
    // Revert stock
    await RevertStockAsync(lastDoc);
    
    return UndoResult.Success(lastDoc);
}
```

---

### 3.8 LOG SYSTEM ENHANCEMENT

#### Log Structure
```csharp
public class ActivityLog
{
    public DateTime Timestamp { get; set; }
    public string User { get; set; }
    public string ActionType { get; set; } // "RESTOCK", "INVENTORY", "DELETE"
    public string ProductName { get; set; }
    public string Details { get; set; }
    public string BeforeValue { get; set; }
    public string AfterValue { get; set; }
    public string IP { get; set; }
}
```

#### Export Log
```
Export ke:
- CSV (default)
- Excel (.xlsx)
- Filter by date range
- Filter by action type
- Filter by user
```

---

## 🎨 4. DASHBOARD ADMIN (POLES DARI YANG SUDAH ADA)

### 4.1 Struktur Views yang Sudah Ada
```
Views/
├── DashboardView.xaml         ← Home/Overview (sudah ada, dipoles)
├── StockMonitoringView.xaml   ← Stock Monitor (sudah ada, dipoles)
├── SettingsView.xaml          ← Settings (sudah ada, dipoles)
└── LogsView.xaml              ← Log Viewer (sudah ada, dipoles)
```

### 4.2 Main Window dengan Drawer Menu

#### Layout Utama
```
┌──────────────────────────────────────────────────────────────┐
│  SMART SEMBAKO ASSISTANT v2.4                     [─][□][✕] │
├────────┬─────────────────────────────────────────────────────┤
│ ☰ MENU │                                                     │
│ ┌──────────────────────────────────────────────────────┐    │
│ │ [≡] Drawer Menu (Slide dari Kiri)                   │    │
│ │                                                     │    │
│ │ ╔══════════════════════════════════╗                │    │
│ │ ║ 🏠 Dashboard                      ║ ← Active       │    │
│ │ ║ 📦 Stock Monitoring               ║                │    │
│ │ ║ ⚙️ Settings                       ║                │    │
│ │ ║ 📜 Activity Logs                  ║                │    │
│ │ ║ 🤖 AI Status                      ║                │    │
│ │ ╟──────────────────────────────────╢                │    │
│ │ ║ 📊 Reports                        ║                │    │
│ │ ║ 📤 Export Data                    ║                │    │
│ │ ╟──────────────────────────────────╢                │    │
│ │ ║ ℹ️ About                          ║                │    │
│ │ ╚══════════════════════════════════╝                │    │
│ │                                                     │    │
│ │ ┌─────────────────────────────────────┐            │    │
│ │ │ 🎮 BOT CONTROL                      │            │    │
│ │ ├─────────────────────────────────────┤            │    │
│ │ │ Status: 🟢 Bot Aktif                │            │    │
│ │ │ Uptime: 2h 15m                      │            │    │
│ │ │                                     │            │    │
│ │ │ [▶ START] [⏹ STOP] [🔄 RESTART]    │            │    │
│ │ └─────────────────────────────────────┘            │    │
│ │                                                     │    │
│ │ ┌─────────────────────────────────────┐            │    │
│ │ │ ⚡ QUICK ACTIONS                    │            │    │
│ │ ├─────────────────────────────────────┤            │    │
│ │ │ [💰 Omzet Hari Ini]                 │            │    │
│ │ │ [⚠️ Cek Stok Minus]                 │            │    │
│ │ │ [📦 Rekomendasi Restock]            │            │    │
│ │ │ [🧪 Test All Connections]           │            │    │
│ │ └─────────────────────────────────────┘            │    │
│ └──────────────────────────────────────────────────────┘    │
│                                                             │
│                  Content Area                               │
│            (Dashboard/Settings/Logs/etc)                   │
│                                                             │
└──────────────────────────────────────────────────────────────┘
```

### 4.3 Bot Control Mechanism

#### Bot Control Panel (Di Drawer)
```
┌─────────────────────────────────────────┐
│  🎮 BOT CONTROL PANEL                   │
├─────────────────────────────────────────┤
│                                         │
│  Status:    🟢 Bot Aktif                │
│  Uptime:    2h 15m 34s                  │
│  Polling:   ✅ Running                   │
│  Last Msg:  10/04 06:35 (2m ago)       │
│                                         │
│  ┌───────────────────────────────────┐ │
│  │ [▶ START]  [⏹ STOP]  [🔄 RESTART]│ │
│  └───────────────────────────────────┘ │
│                                         │
│  Auto Start on Boot: ☑ Enabled         │
│                                         │
│  [📊 View Bot Logs]                     │
│                                         │
└─────────────────────────────────────────┘
```

#### Bot States
```csharp
public enum BotState
{
    Stopped,     // Bot tidak jalan
    Starting,    // Bot sedang start
    Running,     // Bot aktif & polling
    Stopping,    // Bot sedang stop
    Error        // Bot error/crash
}

public class BotController
{
    private BotState _state = BotState.Stopped;
    private DateTime? _startTime;
    private CancellationTokenSource? _cts;
    
    public BotState State => _state;
    public TimeSpan? Uptime => _startTime != null 
        ? DateTime.Now - _startTime 
        : null;
    public bool IsRunning => _state == BotState.Running;
    
    public async Task StartAsync()
    {
        if (_state == BotState.Running)
            return; // Sudah jalan
        
        _state = BotState.Starting;
        OnStateChanged?.Invoke(this, _state);
        
        try
        {
            _cts = new CancellationTokenSource();
            _startTime = DateTime.Now;
            
            // Start Telegram Bot
            await _telegramService.StartAsync(_cts.Token);
            
            _state = BotState.Running;
            OnStateChanged?.Invoke(this, _state);
            
            _loggingService.LogInfo("Bot started successfully", "Bot");
        }
        catch (Exception ex)
        {
            _state = BotState.Error;
            OnStateChanged?.Invoke(this, _state);
            _loggingService.LogError($"Bot start failed: {ex.Message}", "Bot");
            throw;
        }
    }
    
    public async Task StopAsync()
    {
        if (_state != BotState.Running)
            return; // Tidak sedang jalan
        
        _state = BotState.Stopping;
        OnStateChanged?.Invoke(this, _state);
        
        try
        {
            _cts?.Cancel();
            await _telegramService.StopAsync();
            
            _state = BotState.Stopped;
            _startTime = null;
            OnStateChanged?.Invoke(this, _state);
            
            _loggingService.LogInfo("Bot stopped", "Bot");
        }
        catch (Exception ex)
        {
            _state = BotState.Error;
            OnStateChanged?.Invoke(this, _state);
            _loggingService.LogError($"Bot stop failed: {ex.Message}", "Bot");
            throw;
        }
    }
    
    public async Task RestartAsync()
    {
        await StopAsync();
        await Task.Delay(1000); // Wait 1s
        await StartAsync();
    }
    
    // Events untuk UI update
    public event EventHandler<BotState> OnStateChanged;
}
```

#### UI Update Saat Bot State Berubah
```csharp
// Di MainWindow.xaml.cs
public MainWindow()
{
    InitializeComponent();
    
    // Subscribe ke bot state changes
    _botController.OnStateChanged += (sender, state) =>
    {
        Dispatcher.Invoke(() => UpdateBotUI(state));
    };
}

private void UpdateBotUI(BotState state)
{
    switch (state)
    {
        case BotState.Running:
            txtBotStatus.Text = "🟢 Bot Aktif";
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
            btnRestart.IsEnabled = true;
            txtUptime.Visibility = Visibility.Visible;
            break;
            
        case BotState.Stopped:
            txtBotStatus.Text = "🔴 Bot Stop";
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnRestart.IsEnabled = false;
            txtUptime.Visibility = Visibility.Collapsed;
            break;
            
        case BotState.Error:
            txtBotStatus.Text = "⚠️ Bot Error";
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnRestart.IsEnabled = true;
            break;
    }
}
```

### 4.2 DashboardView - Status & Quick Stats ✅ COMPLETE
```
┌─────────────────────────────────────────────────────┐
│  DASHBOARD OVERVIEW                                 │
├─────────────────────────────────────────────────────┤
│  [Header - Dashboard Title]                         │
│                                                     │
│  ┌───────────┐ ┌───────────┐ ┌───────────┐         │
│  │ 🤖 Bot    │ │ 🧠 AI     │ │ 💾 DB     │         │
│  │ Status    │ │ Status    │ │ Status    │         │
│  └───────────┘ └───────────┘ └───────────┘         │
│                                                     │
│  ┌──────────────────────────────────────┐          │
│  │ ⚡ Quick Insights                    │          │
│  │ 💰 Revenue | 📈 Profit | 🔴 Critical │          │
│  └──────────────────────────────────────┘          │
│                                                     │
│  [Quick Actions]  [Recent Conversations]           │
└─────────────────────────────────────────────────────┘
```

**Status:** ✅ ALL IMPLEMENTED
- [x] Status indicator cards (Bot, AI, DB, Memory) ✅
- [x] Quick Insights cards (Revenue, Profit, Critical Stock) ✅
- [x] Auto-refresh tiap 30 detik ✅
- [x] Quick Actions buttons ✅
- [x] Recent Conversations list ✅

### 4.3 SettingsView - AI Fallback & Custom API Key ✅ COMPLETE
```
┌──────────────────────────────────────────────────────┐
│  SETTINGS                                            │
├──────────────────────────────────────────────────────┤
│  🧠 AI PRIMARY (Groq)                                │
│  • API Key + Show/Hide + Test                        │
│  • Model Dropdown (4 models)                         │
│  • Temperature & Max Tokens                          │
│                                                     │
│  🔄 AI FALLBACK (Gemini)                             │
│  • Enable Fallback Checkbox                          │
│  • API Key + Show/Hide + Test                        │
│  • Model Dropdown (4 models)                         │
│                                                     │
│  ⚙️ AI BEHAVIOR                                      │
│  • Auto-switch, Cache, Retry, Recovery               │
│                                                     │
│  [Save] [Test All Connections]                       │
└──────────────────────────────────────────────────────┘
```

**Status:** ✅ ALL IMPLEMENTED
- [x] AI PRIMARY (Groq) section ✅
- [x] AI FALLBACK (Gemini) section ✅
- [x] Model dropdowns ✅
- [x] Show/Hide API Keys ✅
- [x] Test Connection buttons ✅
- [x] AI BEHAVIOR settings ✅

### 4.4 StockMonitoringView - Quick Stats & Filter ✅ COMPLETE
```
┌─────────────────────────────────────────────────────┐
│  STOCK MONITORING                                   │
├─────────────────────────────────────────────────────┤
│  [Header]                                            │
│                                                     │
│  📊 Quick Stats: 🟢150 🟡12 🔴3 ⚠️2               │
│                                                     │
│  [Search] [Filter: Semua/Stok Rendah/Hampir Expiry] │
│  [Refresh]                                           │
│                                                     │
│  DataGrid: Produk, SKU, Kategori, Stok, Harga, dll  │
└─────────────────────────────────────────────────────┘
```

**Status:** ✅ ALL IMPLEMENTED
- [x] Quick stats bar (Aman/Rendah/Habis/Minus) ✅
- [x] Filter by status ✅
- [x] Search functionality ✅
- [x] Refresh button ✅
- [x] DataGrid dengan semua kolom ✅

### 4.5 LogsView - Filter & Export ✅ COMPLETE
```
┌─────────────────────────────────────────────────────┐
│  ACTIVITY LOGS                                      │
├─────────────────────────────────────────────────────┤
│  [Header]                                            │
│                                                     │
│  Filter: [Level ▼] [Category ▼] [Export CSV]        │
│                                                     │
│  DataGrid: Waktu, Level, Kategori, Pesan, User      │
│                                                     │
│  Stats: Total: X | Errors: Y | Warnings: Z          │
└─────────────────────────────────────────────────────┘
```

**Status:** ✅ ALL IMPLEMENTED
- [x] Filter by level & category ✅
- [x] Export CSV ✅
- [x] Stats summary ✅
- [x] DataGrid lengkap ✅

---

## 🔐 5. ROLE & PERMISSION SYSTEM

### Permission Matrix
| Feature | Owner | Kasir |
|---------|-------|-------|
| Cek Stok | ✅ | ✅ |
| Restock | ✅ | ❌ |
| Inventory | ✅ | ❌ |
| Lihat Profit | ✅ | ❌ |
| Laporan Lengkap | ✅ | ❌ |
| Settings | ✅ | ❌ |
| Undo | ✅ | ❌ |

### Implementation
```csharp
public class PermissionChecker
{
    public bool HasPermission(long chatId, string feature)
    {
        var role = GetUserRole(chatId);
        
        return feature switch
        {
            "VIEW_STOCK" => true, // Semua bisa
            "RESTOCK" => role == "Owner",
            "INVENTORY" => role == "Owner",
            "VIEW_PROFIT" => role == "Owner",
            "SETTINGS" => role == "Owner",
            "UNDO" => role == "Owner",
            _ => false
        };
    }
}
```

---

## 📁 6. STRUKTUR FOLDER BARU

```
SmartSembakoAssistant/
│
├── Core/
│   ├── DatabaseService.cs
│   ├── PosDbService.cs
│   ├── StockService.cs
│   ├── ValidationEngine.cs
│   └── EventBus.cs
│
├── AI/
│   ├── AIService.cs
│   ├── AIManager.cs (auto-switch)
│   ├── FallbackEngine.cs
│   └── PromptBuilder.cs
│
├── Bot/
│   ├── TelegramService.cs
│   ├── CommandRouter.cs
│   └── PermissionChecker.cs
│
├── Reports/
│   ├── ReportService.cs
│   ├── ExcelGenerator.cs
│   ├── GoogleSheetsService.cs
│   └── ReportScheduler.cs
│
├── Dashboard/
│   ├── MainWindow.xaml
│   ├── Views/
│   │   ├── HomeView.xaml
│   │   ├── SettingsView.xaml
│   │   ├── StockMonitorView.xaml
│   │   ├── LogViewer.xaml
│   │   └── AIStatusView.xaml
│   └── ViewModels/
│       └── ...
│
├── Models/
│   ├── Config.cs
│   ├── Product.cs
│   ├── ActivityLog.cs
│   └── StockEvent.cs
│
├── Utils/
│   ├── Logger.cs
│   ├── ConfigManager.cs
│   └── Helpers.cs
│
└── Config/
    └── settings.json (auto-generated)
```

---

## ⚙️ 7. CONFIGURATION SCHEMA

```json
{
  "ai": {
    "primary": {
      "provider": "Groq",
      "enabled": true,
      "api_key": "encrypted",
      "model": "llama-3.1-70b-versatile",
      "temperature": 0.7,
      "max_tokens": 1000,
      "timeout_ms": 30000
    },
    "fallback": {
      "provider": "Gemini",
      "enabled": true,
      "api_key": "encrypted",
      "model": "gemini-2.0-flash",
      "temperature": 0.7,
      "max_tokens": 1000,
      "timeout_ms": 30000
    },
    "behavior": {
      "auto_switch": true,
      "retry_count": 2,
      "auto_recovery_minutes": 5,
      "cache_enabled": true,
      "cache_ttl_minutes": 60
    }
  },
  "bot": {
    "telegram_token": "encrypted",
    "owner_chat_ids": [12345, 67890],
    "kasir_chat_ids": [11111],
    "mode": "SAFE",
    "enabled": true,
    "auto_start": true
  },
  "reports": {
    "daily_time": "07:00",
    "weekly_day": "Sunday",
    "weekly_time": "20:00",
    "monthly_enabled": true,
    "auto_send": true,
    "format": ["excel", "google_sheets"]
  },
  "notifications": {
    "stock_alert": true,
    "stock_minimum": 5,
    "negative_stock_alert": true,
    "daily_summary": true,
    "dead_stock_alert": true,
    "dead_stock_days": 14
  },
  "stock": {
    "safe_mode": true,
    "inventory_max_change": 100,
    "allow_negative_stock": false,
    "anti_duplicate_seconds": 3
  },
  "google_sheets": {
    "enabled": false,
    "spreadsheet_id": "",
    "credentials_file": "credentials.json"
  },
  "database": {
    "pos_db_path": "auto",
    "memory_db_path": "data/memory.db"
  }
}
```

---

## 🗺️ 8. ROADMAP IMPLEMENTASI

### Phase 3.2 - Dashboard Polish & Bot Control
**Estimasi:** 2-3 hari
**Status:** ✅ COMPLETE (100%) - **RELEASED v3.0.0**
- [x] **MainWindow**: Implementasi Drawer Menu (slide dari kiri) ✅
- [x] **MainWindow**: Bot Control Panel di Drawer (Start/Stop/Restart) ✅
- [x] **MainWindow**: Bot State Management (Stopped/Starting/Running/Stopping/Error) ✅
- [x] **MainWindow**: Auto-update UI saat bot state berubah ✅
- [x] **MainWindow**: Uptime tracker untuk bot ✅
- [x] **MainWindow**: Quick Actions panel di Drawer dengan functionality lengkap ✅
- [x] **MainWindow**: Top bar dengan page title & datetime ✅
- [x] **DashboardView**: Auto-refresh tiap 30 detik ✅
- [x] **DashboardView**: Status indicator cards (AI, Bot, DB) ✅
- [x] **StockMonitoringView**: Quick stats bar (Aman/Rendah/Habis/Minus) ✅
- [x] **StockMonitoringView**: Filter by status (Aman/Rendah/Minus) ✅
- [x] **LogsView**: Filter by level & category ✅
- [x] **LogsView**: Export to CSV ✅
- [x] **Build verification**: dotnet build --configuration Release SUCCESS ✅
- [x] **Quick Actions**: Omzet Hari Ini, Cek Stok Minus, Rekomendasi Restock, Test Connections ✅

### Phase 3.3 - AI Fallback & Custom API Key
**Estimasi:** 2 hari
**Status:** ✅ COMPLETE (100%)
- [x] **SettingsView**: AI PRIMARY (Groq) section dengan API Key Show/Hide ✅
- [x] **SettingsView**: AI FALLBACK (Gemini) section dengan enable/disable checkbox ✅
- [x] **SettingsView**: Model dropdown untuk Groq & Gemini ✅
- [x] **SettingsView**: Test Connection buttons untuk Groq & Gemini ✅
- [x] **SettingsView**: AI BEHAVIOR section (Auto-switch, Cache, Retry, Recovery) ✅
- [x] **SettingsView**: Custom API Key dengan Show/Hide toggle ✅
- [x] **Build verification**: dotnet build --configuration Release SUCCESS ✅
- [ ] **SettingsView**: Custom API Key (Groq & Gemini) dengan Show/Hide
- [ ] **SettingsView**: Test connection button per API
- [ ] **SettingsView**: "Test All Connections" button
- [ ] **SettingsView**: Auto-encrypt API key saat save
- [ ] **AIManager**: Implementasi dual AI provider (Groq + Gemini)
- [ ] **AIManager**: Auto-switch logic dengan retry
- [ ] **FallbackEngine**: Rule-based response
- [ ] **AI Status View**: Monitor AI health & provider aktif

### Phase 3.4 - Validation Engine
**Estimasi:** 1-2 hari
- [ ] Validation Engine class
- [ ] Anti-duplicate command
- [ ] Confirmation system (SAFE mode)
- [ ] Warning messages
- [ ] Danger level blocking

### Phase 3.4 - Event Bus & Enhanced Logging
**Estimasi:** 1-2 hari
- [ ] EventBus implementation
- [ ] Activity log database table
- [ ] Export log to CSV/Excel
- [ ] Log summary stats
- [ ] Auto cleanup old logs

### Phase 3.5 - Smart Recommendations
**Estimasi:** 2 hari
- [ ] Sales analytics engine
- [ ] Restock recommendation (avg daily sales)
- [ ] Dead stock detection (>14 days no sales)
- [ ] Priority system (HIGH/MEDIUM/LOW)
- [ ] Safety stock calculation

### Phase 3.6 - Scheduler & Reports
**Estimasi:** 2 hari
- [ ] Report scheduler (Timer-based)
- [ ] Daily report (07:00)
- [ ] Weekly report (Sunday 20:00)
- [ ] Excel generator (.xlsx)
- [ ] Google Sheets integration

### Phase 4.0 - Advanced Features
**Estimasi:** TBD
- [ ] Undo system (last transaction)
- [ ] OCR (Tesseract)
- [ ] Voice note support
- [ ] Multi-cabang support
- [ ] Web dashboard (optional)

---

## 🧪 9. TESTING CHECKLIST

### Validation Engine
- [ ] Input normal → langsung proses
- [ ] Input warning → konfirmasi → lanjut
- [ ] Input danger → konfirmasi 2x → lanjut/batal
- [ ] Duplicate command → reject

### AI Auto-Switch
- [ ] AI aktif → response normal
- [ ] AI timeout → retry 2x → fallback
- [ ] AI recover → auto switch back
- [ ] Cache hit → response cepat

### Reports
- [ ] Daily report terkirim jam 07:00
- [ ] Weekly report terkirim Sunday 20:00
- [ ] Google Sheets update otomatis
- [ ] Excel file tergenerate

### Dashboard
- [ ] Settings save → config update
- [ ] Test connection buttons work
- [ ] Stock monitor refresh
- [ ] Log export works

---

## 📊 10. METRICS & MONITORING

### Key Metrics
```
- AI Response Time: < 3 detik
- AI Success Rate: > 95%
- Fallback Activation: < 1% requests
- Bot Uptime: > 99%
- Report Delivery: 100% on time
- Error Rate: < 0.1%
```

### Monitoring Dashboard
```
Real-time stats:
- AI requests/hour
- Error count
- Fallback triggers
- Active users
- Command frequency
```

---

## 🔒 11. SECURITY GUIDELINES

### Data Sensitif
```
✅ Enkripsi API keys (DPAPI)
✅ Enkripsi bot token
✅ No plaintext passwords
✅ Chat ID whitelist
✅ Role-based access
✅ Audit trail semua perubahan
```

### Database Access
```
✅ pos.db: READ-ONLY untuk data
✅ pos.db: WRITE ONLY via Engine
✅ Transaction untuk operasi kritis
✅ Backup otomatis sebelum write
```

---

## 💡 12. BEST PRACTICES

### DO ✅
- Async/await untuk semua I/O
- Try-catch dengan logging
- User-friendly error messages
- Fallback mechanisms
- Parameterized SQL queries
- Transaction untuk operasi kritis
- Update changelog setiap perubahan

### DON'T ❌
- AI langsung insert DB
- Hardcode API keys
- Blocking UI thread
- Skip error handling
- Expose sensitive data di logs
- Auto-order ke supplier
- Commit tanpa test

---

## 📝 13. CHANGELOG FORMAT

Setiap entry harus mencakup:
```json
{
  "date": "YYYY-MM-DD",
  "version": "X.Y.Z",
  "title": "Judul Jelas",
  "summary": ["Ringkasan 1-2 kalimat"],
  "details": ["Detail teknis", "Files changed", "Notes"],
  "status": "draft | implemented | released",
  "breaking_changes": false,
  "migration_required": false
}
```

---

## 🎯 14. ACCEPTANCE CRITERIA

Sebelum release v3.0, harus:
- [ ] Semua validation engine tests pass
- [ ] AI auto-switch tested (simulate failure)
- [ ] Scheduler berjalan 24 jam tanpa error
- [ ] Dashboard responsive (no freezing)
- [ ] Google Sheets sync working
- [ ] Export log working
- [ ] Role-based access tested
- [ ] Build Release: 0 errors
- [ ] Manual testing: all features work
- [ ] Documentation updated

---

## 🚀 15. DEPLOYMENT CHECKLIST

### Pre-Deployment
- [ ] Backup database
- [ ] Backup config
- [ ] Test all features
- [ ] Check logs for errors

### Deployment
- [ ] Build Release
- [ ] Publish single file
- [ ] Copy to production folder
- [ ] Run migration (if any)

### Post-Deployment
- [ ] Test connection AI
- [ ] Test connection Bot
- [ ] Test connection DB
- [ ] Verify scheduler running
- [ ] Check first daily report
- [ ] Monitor logs for 24 hours

---

## 📞 16. SUPPORT & DOCUMENTATION

### Files to Update
- [ ] README.md
- [ ] TECHNICAL_DOCS.md
- [ ] QUICK_START.md
- [ ] AGENT.md
- [ ] changelog.json
- [ ] DASHBOARD_GUIDE.md (new)
- [ ] VALIDATION_GUIDE.md (new)
- [ ] TROUBLESHOOTING.md (new)

### External Resources
- [WPF Documentation](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
- [.NET 8 Documentation](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [Groq API Docs](https://console.groq.com/docs)
- [Telegram Bot API](https://core.telegram.org/bots/api)
- [Google Sheets API](https://developers.google.com/sheets/api)

---

**Last Updated:** 10 April 2026
**Current Version:** 2.4.0
**Target Version:** 3.0.0
**Status:** Planning Phase 📋

---

**Happy Coding! 🚀**
