# Laporan Audit Teknis - Smart Sembako Assistant

**Tanggal audit ulang:** 27 Mei 2026, 05:41 WIB  
**Lingkup:** Codebase `SmartSembakoAssistant/`  
**Auditor ulang:** Codex  
**Basis audit:** Review kode aktual, verifikasi build, dan cek model Gemini ke dokumentasi resmi Google AI terbaru.

## Ringkasan Eksekutif

Audit lama masih berguna sebagai daftar kandidat masalah, tetapi beberapa klaimnya sudah tidak valid atau terlalu tinggi severity-nya. Hasil audit ulang menemukan:

| Prioritas | Jumlah | Catatan |
|-----------|--------|---------|
| KRITIS | 3 | Risiko keamanan/data loss/runtime penting |
| TINGGI | 6 | Bug aktif atau risk operasional tinggi |
| SEDANG | 9 | UX, maintainability, reliability |
| RENDAH | 5 | Cleanup dan polish |
| TEMUAN AUDIT LAMA YANG TIDAK VALID | 5 | Harus dihapus/turunkan severity |

Build check:

```text
dotnet build SmartSembakoAssistant.sln --no-restore
Result: Build succeeded, 33 warnings, 0 errors.
```

Referensi Gemini resmi:

- https://ai.google.dev/gemini-api/docs/models
- https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite
- https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash
- https://ai.google.dev/gemini-api/docs/models/gemini-3-flash-preview

---

## KRITIS - Harus Diprioritaskan

### K-1: `ConfigService` bisa menimpa config aktif jika JSON rusak

**File:** `Services/ConfigService.cs` line 81-88  
**Masalah:** Jika `LoadConfig()` gagal parse config, code membuat `new AppConfig()` lalu langsung `SaveConfig()`.

```csharp
catch (Exception ex)
{
    _config = new AppConfig();
    SaveConfig();
}
```

**Dampak:** Satu kesalahan edit manual di `config.json` bisa menimpa config aktif dengan config kosong/default, termasuk API key, nomor owner, path database, dan setup state.

**Rekomendasi fix:**

- Jangan overwrite config saat load gagal.
- Rename config rusak ke `config.invalid-yyyyMMdd-HHmmss.json`.
- Load fallback dari template atau config terakhir yang valid.
- Tampilkan error eksplisit di UI.

---

### K-2: Endpoint Baileys inbound tidak punya autentikasi/signature

**File:** `Services/WhatsAppHandler.cs` line 423, 672-706  
**Masalah:** `POST /baileys/events/inbound` menerima payload dari sidecar tanpa signature/shared secret. Sender ID dari payload dipakai untuk otorisasi.

```csharp
if (context.Request.HttpMethod == "POST" && path.Equals("/baileys/events/inbound", StringComparison.OrdinalIgnoreCase))
{
    await HandleBaileysInboundAsync(context);
    return;
}
```

**Dampak:** Jika port listener terekspos oleh tunnel/firewall, attacker bisa spoof payload dengan nomor owner/kasir dan memicu command automation.

**Catatan:** Signature validation sudah ada untuk WhatsApp Cloud webhook, tetapi tidak untuk endpoint Baileys local inbound.

**Rekomendasi fix:**

- Tambahkan shared secret env var dari C# host ke sidecar, misalnya `SSA_DESKTOP_INBOUND_SECRET`.
- Sidecar kirim header HMAC, misalnya `X-SSA-Signature`.
- `WhatsAppHandler` validate body signature sebelum deserialize.
- Minimal fallback: reject request non-localhost jika endpoint Baileys tidak diberi secret.

---

### K-3: Recovery config path membingungkan dan rawan edit file yang salah

**File:** `Services/ConfigService.cs` line 496-525  
**Masalah:** Dalam non-portable mode, app memilih writable config di LocalAppData. File repo `SmartSembakoAssistant/config.json` yang sering diedit developer belum tentu config runtime aktif.

**Dampak:** User/developer bisa mengubah model/API key di file repo, tetapi aplikasi tetap memakai config lain. Ini membuat bug Gemini/API key terlihat "tidak berubah" setelah diedit.

**Rekomendasi fix:**

- Tampilkan `ConfigPath` aktif di Settings dengan tombol "Open active config".
- Saat duplicate config terdeteksi, tampilkan warning lebih jelas.
- Untuk dev mode, pertimbangkan prioritas project-root config jika ada `.csproj`.

---

## TINGGI - Bug atau Reliability Risk

### T-1: `GroqService` tidak di-dispose di beberapa jalur runtime

**Files:**

- `Views/DashboardView.xaml.cs` line 106
- `MainWindow.xaml.cs` line 720
- `Services/BotController.cs` line 80
- `Views/SettingsView.xaml.cs` line 1596, 1620, 1986

**Masalah:** `GroqService` implement `IDisposable` dan membungkus `HttpClient`, tetapi beberapa instance dibuat tanpa `using` atau lifecycle dispose.

**Koreksi dari audit lama:** Masalah ini valid, tetapi tidak terbukti terjadi tiap 30 detik. `RefreshDataAsync()` dashboard tidak memanggil `TestGroqConnectionAsync()`.

**Rekomendasi fix:**

- Untuk test sementara, pakai `using var groqService = new GroqService(...)`.
- Untuk runtime panjang, dispose di `BotController.StopCoreAsync()` dan `MainWindow.OnClosing()`.
- Lebih baik lagi: gunakan shared `HttpClient` atau `IHttpClientFactory`.

---

### T-2: `DashboardView.LoadDashboardData()` adalah `async void`

**File:** `Views/DashboardView.xaml.cs` line 74, 216-218  
**Masalah:** `LoadDataAsync()` memanggil `LoadDashboardData()` tanpa await karena method itu `async void`.

```csharp
public async Task LoadDataAsync()
{
    LoadDashboardData();
}
```

**Dampak:** Caller mengira reload selesai padahal masih berjalan. Error handling dan urutan refresh data bisa tidak deterministik.

**Rekomendasi fix:** Ubah `LoadDashboardData()` menjadi `private async Task LoadDashboardDataAsync()` dan await semua caller.

---

### T-3: `VisionModel` di config lokal memakai model yang kemungkinan tidak valid

**File:** `config.json` line 7  
**Masalah:** Config lokal berisi:

```json
"VisionModel": "gemini-3.1-flash"
```

Dokumentasi resmi saat audit menunjukkan model general yang valid antara lain:

- `gemini-3.1-flash-lite`
- `gemini-3.5-flash`
- `gemini-3-flash-preview`
- `gemini-2.5-flash`
- `gemini-2.5-flash-lite`

Yang tidak terlihat sebagai general `generateContent` model code adalah `gemini-3.1-flash`. Ada Gemini 3.1 Flash Live/TTS, tetapi itu bukan pengganti langsung untuk OCR image `generateContent`.

**Rekomendasi fix:** Ganti `VisionModel` ke `gemini-3.5-flash` atau `gemini-3.1-flash-lite`.

---

### T-4: Settings UI tidak menyimpan `VisionModel`

**Files:**

- `Views/SettingsView.xaml.cs` line 83, 498
- `Views/SettingsView.xaml` line 160-164
- `Models/AppConfig.cs` line 27-28

**Masalah:** UI hanya memilih dan menyimpan `FallbackModel`, tetapi `GroqService` membaca `VisionModel` terpisah untuk OCR vision.

**Dampak:** User bisa merasa sudah mengganti model Gemini di Settings, tetapi OCR Vision tetap memakai `VisionModel` lama dari config.

**Rekomendasi fix:**

- Tambahkan field/dropdown `VisionModel` di Settings, atau
- Saat save, set `current.Groq.VisionModel = current.Groq.FallbackModel` jika belum ada pilihan khusus.

---

### T-5: Tombol Reports tidak membuka `ReportsView`

**File:** `MainWindow.xaml.cs` line 273-277  
**Masalah:** `BtnReports_Click` memanggil `LoadAnalytics()` dan menandai `BtnAnalytics`, bukan `LoadReports()`.

```csharp
private void BtnReports_Click(object sender, RoutedEventArgs e)
{
    UpdatePageTitle("Penjualan");
    SetActiveButton(BtnAnalytics);
    LoadAnalytics();
}
```

**Dampak:** `ReportsView` ada, tetapi tidak bisa diakses dari sidebar. Ini bug UX aktif.

**Rekomendasi fix:** Panggil `SetActiveButton(BtnReports)` dan `LoadReports()`.

---

### T-6: Fire-and-forget logging menyembunyikan error

**Files:**

- `MainWindow.xaml.cs` line 98, 136
- `Services/ConfigService.cs` line 85
- `Views/ReportsView.xaml.cs` line 235
- `Views/SalesAnalyticsView.xaml.cs` line 373

**Masalah:** Beberapa call `LogErrorAsync(...)` tidak di-await. Build warning CS4014 sudah mengindikasikan ini.

**Dampak:** Error logging bisa gagal diam-diam, terutama saat database log sedang locked.

**Rekomendasi fix:** Await di handler async, atau gunakan helper fire-and-forget yang menangkap exception.

---

## SEDANG - Desain, Maintainability, UX

### S-1: `AutomationEngine.cs` terlalu besar

**File:** `Services/AutomationEngine.cs`  
**Ukuran:** 597,809 bytes, 13,676 lines.

**Masalah:** Satu class menanggung routing, OCR, outbox, scheduler, RBAC, export, pending confirmation, product matching, dan template.

**Dampak:** Sulit dites, sulit review, dan rawan regression.

**Rekomendasi refactor bertahap:**

- `OcrReceiptPipeline`
- `OutboundQueueService`
- `AutomationScheduler`
- `IntentRouter`
- `ProductMatchingService`

---

### S-2: `PosDbService.cs` terlalu besar dan query tersebar

**File:** `Services/PosDbService.cs`  
**Ukuran:** 285,326 bytes, 6,187 lines.

**Masalah:** Banyak query SQLite langsung dalam satu service. Ada puluhan `new SqliteConnection(...)` di seluruh file.

**Koreksi dari audit lama:** Tidak benar jika disebut "tidak ada pooling" sebagai fakta pasti. `Microsoft.Data.Sqlite` punya behavior connection management sendiri, tetapi code tetap sulit diaudit dan performa query dashboard/chat bisa tersebar.

**Rekomendasi:** Pecah menjadi repository/query services per domain: sales, inventory, purchase, customer, schema validation.

---

### S-3: `SettingsView` membuat `DatabaseService` dan `LoggingService` manual

**File:** `Views/SettingsView.xaml.cs` line 36-42  
**Masalah:** Settings membuat service sendiri, bukan memakai instance utama dari `MainWindow`.

**Dampak:** Potensi behavior tidak konsisten dan lebih sulit mengontrol lifecycle/logging. Dampaknya tidak selalu fatal karena `DatabaseService` biasanya mengarah ke path runtime yang sama.

**Rekomendasi:** Inject `DatabaseService` dan `LoggingService` dari `MainWindow`.

---

### S-4: `MessageRouter` adalah stub dan tidak dipakai

**File:** `Services/MessageRouter.cs`  
**Masalah:** Class mengembalikan `"OCR belum diaktifkan..."` untuk image dan tidak terhubung ke pipeline OCR sebenarnya.

**Koreksi dari audit lama:** Ini bukan kritis karena tidak ditemukan reference produksi ke `MessageRouter`. Search hanya menemukan definisinya sendiri.

**Rekomendasi:** Hapus class jika benar tidak dipakai, atau sambungkan ke `AutomationEngine.ProcessInboundMessageAsync()`.

---

### S-5: AI Chat tidak punya cancellation token untuk request AI

**File:** `Views/AIChatView.xaml.cs` line 117-211  
**Masalah:** Request AI tidak bisa dibatalkan ketika user pindah view atau app closing.

**Dampak:** Request tetap berjalan dan bisa update UI setelah konteks user berubah.

**Rekomendasi:** Tambahkan `CancellationTokenSource` per request dan cancel di unload/clear.

---

### S-6: Context export AI Chat bisa stale

**File:** `Views/AIChatView.xaml.cs` line 226-257  
**Masalah:** Format-only export seperti "csv/pdf/excel" memakai `_lastSalesContextStartDate` jika ada, tanpa expiry.

**Dampak:** User bisa export periode lama tanpa sadar jika konteks chat sebelumnya masih tersimpan.

**Rekomendasi:** Beri expiry context, tampilkan konfirmasi periode, atau require tanggal eksplisit untuk format-only export setelah beberapa menit.

---

### S-7: `ExtractKeywordAfter()` terlalu agresif

**File:** `Views/AIChatView.xaml.cs` line 470-481  
**Masalah:** Menghapus kata seperti `stok`, `barang`, `produk` dari input. Jika nama produk mengandung kata itu, hasil pencarian bisa salah.

**Rekomendasi:** Gunakan parser intent yang mempertahankan quoted phrase, atau hapus hanya command prefix.

---

### S-8: `LoggingService` belum punya retention/filter level otomatis

**File:** `Services/LoggingService.cs`  
**Masalah:** Semua log masuk database. Ada fitur clear manual, tetapi tidak terlihat filter level/retention otomatis berdasarkan config.

**Dampak:** DB log bisa membesar di runtime panjang.

**Rekomendasi:** Terapkan retention berdasarkan `App.MaxLogDays` dan optional minimum log level.

---

### S-9: Ada beberapa unreachable code di `AutomationEngine`

**File:** `Services/AutomationEngine.cs`  
**Build warnings:** CS0162 di beberapa lokasi.

**Contoh:** `NeedsConfirmation()` return lebih awal lalu masih punya return lain setelahnya.

**Dampak:** Bukan crash, tetapi menandakan logic lama tertinggal dan bisa membingungkan maintenance.

**Rekomendasi:** Hapus unreachable branch atau gabungkan logic yang masih dibutuhkan.

---

## RENDAH - Cleanup dan Polish

### R-1: `AIChatError.log` ditulis ke working directory

**File:** `Views/AIChatView.xaml.cs` line 71  
**Masalah:** Log error UI ditulis ke path relatif.

**Rekomendasi:** Pakai `RuntimePaths.LogsDirectory`.

---

### R-2: PDF export hard-limit 600 baris

**File:** `Services/ExportService.cs` line 133  
**Masalah:** PDF hanya menampilkan `items.Take(600)`.

**Koreksi:** Code sudah menampilkan catatan jika data dipotong. Jadi ini bukan bug fatal, tapi tetap perlu dibuat configurable.

---

### R-3: BrushConverter dibuat berulang di UI

**File:** `Views/DashboardView.xaml.cs` beberapa lokasi  
**Masalah:** Warna dibuat runtime via `new BrushConverter()`.

**Rekomendasi:** Pindahkan ke resource XAML/static brush.

---

### R-4: Reports/Sales overlap perlu keputusan produk

**Files:** `Views/SalesAnalyticsView.*`, `Views/ReportsView.*`  
**Masalah:** Ada overlap, tetapi bug utamanya saat ini adalah Reports tidak bisa dibuka.

**Rekomendasi:** Fix navigation dulu, baru putuskan merge atau tetap dipisah.

---

### R-5: Beberapa teks/icon di source tampak mojibake

**Files:** Banyak file UI/log string  
**Masalah:** Banyak string muncul seperti `âš ï¸`, `ðŸ...`. Ini indikasi encoding lama pernah rusak.

**Dampak:** UI/log bisa terlihat tidak profesional dan sulit dibaca.

**Rekomendasi:** Normalisasi encoding file dan ganti simbol rusak dengan teks ASCII atau emoji valid secara konsisten.

---

## Temuan Audit Lama yang Perlu Dikoreksi

### C-1: Klaim `gemini-2.5-flash-lite` "mungkin tidak ada" tidak valid

Dokumentasi Google resmi mencantumkan `gemini-2.5-flash-lite`.

### C-2: Klaim `gemini-3.1-flash-lite` "tidak ada" tidak valid

Dokumentasi Google resmi mencantumkan model code `gemini-3.1-flash-lite`, stable, latest update Mei 2026.

### C-3: Klaim Dashboard melakukan real Groq API call tiap 30 detik tidak terbukti

`SetupAutoRefresh()` memanggil `RefreshDataAsync()`, dan method itu tidak memanggil `TestGroqConnectionAsync()`. Test Groq terjadi di initial/full dashboard load.

### C-4: Klaim tidak ada health endpoint tidak valid

`WhatsAppHandler` sudah punya `GET /health/integrations`.

### C-5: `MessageRouter` bukan critical path

`MessageRouter` memang stub, tetapi tidak ditemukan pemakaian produksi. Severity seharusnya rendah/sedang, bukan kritis.

---

## Quick Wins yang Disarankan

1. Fix `ConfigService` agar load gagal tidak overwrite config.
2. Tambah HMAC/shared secret untuk `/baileys/events/inbound`.
3. Ganti `VisionModel` lokal dari `gemini-3.1-flash` ke `gemini-3.5-flash` atau `gemini-3.1-flash-lite`.
4. Tambah field `VisionModel` di Settings atau samakan dengan `FallbackModel`.
5. Fix `BtnReports_Click` agar membuka `ReportsView`.
6. Ubah `DashboardView.LoadDashboardData()` dari `async void` ke `Task`.
7. Dispose semua `GroqService` transient.

---

## Prioritas Implementasi

| Urutan | Item | Effort | Dampak |
|--------|------|--------|--------|
| 1 | K-1 Config overwrite guard | Rendah-Sedang | Sangat tinggi |
| 2 | K-2 Baileys inbound signature | Sedang | Sangat tinggi |
| 3 | T-3/T-4 Gemini Vision config | Rendah | Tinggi |
| 4 | T-5 Reports navigation | Rendah | Tinggi |
| 5 | T-2 Dashboard async fix | Rendah | Tinggi |
| 6 | T-1 Dispose GroqService | Rendah-Sedang | Tinggi |
| 7 | T-6 Await logging | Rendah | Sedang |

