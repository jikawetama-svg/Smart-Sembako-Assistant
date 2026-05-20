# OCR Struk Pembelian — Final Plan (v2)
> **Revisi dari user:**
> 1. **Product Mapping Table** — user setup dulu pemetaan nama faktur → nama DB sebelum OCR jalan
> 2. **UI Settings OCR** — section di `SettingsView` untuk manage mapping (DataGrid)
> 3. **Satu faktur = satu bulk Purchase Document** (bukan N dokumen per produk)

# OCR Struk Pembelian → Purchase Document + Auto Stok + Google Sheets

## Latar Belakang

Saat ini bot sudah punya:
- ✅ `/restock` manual via teks → buat Purchase Document (Type 1) → update Stock
- ✅ Google.Apis.Sheets.v4 sudah ada di `.csproj`
- ✅ Tesseract 5.2.0 sudah ada di `.csproj`
- ✅ `TelegramBotService` sudah handle foto tapi masih reply "OCR belum diaktifkan"
- ✅ `CreatePurchaseDocumentAsync()` di `PosDbService.cs` sudah teruji

Yang perlu dibangun adalah **pipeline lengkap**:

```
📸 Foto struk dikirim via Telegram
        ↓
🔍 Download foto + OCR (Tesseract)
        ↓
🧠 Parse baris produk (token-based dari kanan)
        ↓
🔎 Fuzzy match nama produk ke database Aronium
        ↓
📋 Preview hasil parsing → konfirmasi ke owner
        ↓
✅ Owner konfirmasi → CreatePurchaseDocumentAsync() per item
        ↓
📊 Update Stock otomatis (sudah di dalam CreatePurchaseDocumentAsync)
        ↓
📤 Push ke Google Spreadsheet (sheet "Pembelian")
        ↓
📢 Reply sukses ke Telegram
```

---

## User Review Required

> [!NOTE]
> **Semua pertanyaan dari versi sebelumnya sudah dijawab user.** Rencana di bawah sudah final.

> [!IMPORTANT]
> **Format struk yang didukung:** Berdasarkan `percakapan.md`, ada 2 tipe utama:
> - **Tipe A (Surat Jalan supplier):** `[Qty] [Kode] [Nama Produk] [Harga] [Total]` (contoh: Wings, seperti di `image.png`)
> - **Tipe B (Struk kasir POS):** `[Nama] [Harga] [Qty] [Satuan] [Total]` (contoh: Tani Makmur Putra)
> 
> **Parser harus auto-detect tipe layout.** Konfirmasi apakah ada supplier lain yang formatnya berbeda?

> [!IMPORTANT]
> **Google Sheet target:** Perlu nama/ID spreadsheet dan nama sheet tab yang menjadi tujuan push data pembelian. Apakah sudah ada sheet yang dipakai sebelumnya, atau perlu buat baru?

> [!WARNING]
> **Tesseract language data:** Tesseract perbutuhkan file `tessdata/ind.traineddata` (bahasa Indonesia) di folder output aplikasi. Perlu memastikan file ini tersedia sebelum build/run.

---

## Keputusan Desain (Final)

| Topik | Keputusan |
|---|---|
| Trigger foto | Caption `/struk` (tidak auto-trigger semua foto) |
| Product matching | User setup **Product Mapping Table** dulu via Settings UI |
| Satu faktur | Satu **bulk Purchase Document** (semua item dalam 1 dokumen) |
| Item tidak di-mapping | Skip + tampil di preview sbg ⚠️ (tidak dibuat dokumen) |
| Harga jika kosong | Hitung `Total / Qty`, fallback ke `Product.Cost` dari DB |
| Supplier | Simpan sebagai teks di field `Note` dokumen |
| Google Sheets | Satu baris per item, satu kolom No Dokumen bersama |

---

## Proposed Changes

### Component 0 — Product Mapping Table (KUNCI UTAMA)

#### [MODIFY] `config.json` — tambah section `OcrReceipt`
```json
"OcrReceipt": {
  "Enabled": true,
  "TessdataPath": "tessdata",
  "TriggerCaption": "/struk",
  "ProductMappings": [
    {
      "InvoiceName": "Sedap Mie Bag 690gr Ayam Special",
      "DatabaseProductId": "123",
      "DatabaseProductName": "Mie Sedap Ayam 690gr"
    },
    {
      "InvoiceName": "Pop Ice",
      "DatabaseProductId": "45",
      "DatabaseProductName": "Pop Ice Sachet"
    }
  ]
},
"GoogleSheets": {
  "Enabled": true,
  "ServiceAccountJsonPath": "credentials/service-account.json",
  "SpreadsheetId": "YOUR_SPREADSHEET_ID",
  "PurchaseSheetName": "Pembelian"
}
```

**Mekanisme matching saat OCR:**
1. Parse nama produk dari struk → `"Sedap Mie Bag 690gr Ayam Special"`
2. Cek `ProductMappings` → temukan entry dengan `InvoiceName` cocok (case-insensitive Contains)
3. Jika ada → gunakan `DatabaseProductId` langsung (skip fuzzy match)
4. Jika tidak ada di mapping → coba fuzzy match ke seluruh produk DB
5. Jika keduanya gagal → mark sebagai ⚠️ skip

---

### Component 1 — OCR Service (BARU)

#### [NEW] `Services/OcrReceiptService.cs`
Service baru yang menangani seluruh OCR pipeline:
- **`DownloadPhotoAsync(fileId)`** — download foto dari Telegram Bot API ke temp file
- **`ExtractTextFromImageAsync(imagePath)`** — jalankan Tesseract OCR dengan preprocessing (grayscale + threshold)
- **`ParseReceiptLines(ocrText)`** — auto-detect layout (Tipe A vs Tipe B), parse per baris dengan token parsing dari kanan (total → unit → qty → harga → nama)
- **`FuzzyMatchProductsAsync(parsedItems)`** — untuk setiap item hasil parse, cari produk di Aronium dengan fuzzy match (Contains + Levenshtein fallback)
- **`BuildOcrPreviewMessage(matchedItems)`** — format pesan preview untuk konfirmasi ke owner

**Model internal:**
```csharp
public class OcrLineItem {
    public string RawLine { get; set; }
    public string ParsedName { get; set; }
    public decimal Qty { get; set; }
    public string? Unit { get; set; }
    public decimal Price { get; set; }
    public decimal Total { get; set; }
    public Product? MatchedProduct { get; set; }
    public bool IsMatched { get; set; }
    public bool IsSkipped { get; set; }
}
```

---

### Component 1b — Bulk Purchase Method (BARU di PosDbService)

#### [MODIFY] `Services/PosDbService.cs`
Tambah method `CreateBulkPurchaseDocumentAsync()` — **satu dokumen untuk semua item**:
```csharp
public async Task<RestockResult> CreateBulkPurchaseDocumentAsync(
    List<BulkPurchaseItem> items,  // {ProductId, Qty, Price}
    int userId = 1,
    string? supplierNote = null)
```
- Buat **satu** header `Document` (Type 1 Purchase)
- Loop setiap item → INSERT `DocumentItem` dalam transaksi yang sama
- `UPDATE Stock` untuk setiap produk dalam transaksi yang sama
- Satu `COMMIT` di akhir = atomik
- Return: `DocumentNumber`, `DocumentId`, `TotalKeseluruhan`

---

### Component 2 — Google Sheets Service (BARU)

#### [NEW] `Services/GoogleSheetsService.cs`
Service untuk push data ke Google Spreadsheet:
- **`AppendPurchaseRowsAsync(items, docNumber, supplierName, invoiceDate)`** — append baris ke sheet "Pembelian" dengan kolom:
  `Tanggal | No Dokumen | Supplier | Produk | Qty | Satuan | Harga Satuan | Total | Status`
- **Authentication:** Service Account JSON (path dari `config.json`)

---

### Component 3 — Config Extension

#### [MODIFY] `Services/ConfigService.cs`
Tambah model C# baru:
```csharp
public class OcrReceiptConfig {
    public bool Enabled { get; set; }
    public string TessdataPath { get; set; } = "tessdata";
    public string TriggerCaption { get; set; } = "/struk";
    public List<OcrProductMapping> ProductMappings { get; set; } = new();
}

public class OcrProductMapping {
    public string InvoiceName { get; set; } = "";
    public string DatabaseProductId { get; set; } = "";
    public string DatabaseProductName { get; set; } = "";
}

public class GoogleSheetsConfig {
    public bool Enabled { get; set; }
    public string ServiceAccountJsonPath { get; set; } = "";
    public string SpreadsheetId { get; set; } = "";
    public string PurchaseSheetName { get; set; } = "Pembelian";
}
```

---

### Component 4 — OCR Settings UI (BARU)

#### [MODIFY] `Views/SettingsView.xaml`
Tambah section baru **di akhir** `StackPanel` (sebelum tombol Save), setelah section Database:

```xml
<!-- OCR Receipt Settings -->
<Border Style="{StaticResource SectionStyle}">
  <StackPanel>
    <TextBlock Text="📷 OCR Struk Pembelian" FontSize="14" FontWeight="SemiBold"
               Margin="0,0,0,10" Foreground="#7C3AED"/>
    <CheckBox x:Name="ChkOcrEnabled" Content="Aktifkan OCR struk via Telegram"
              Margin="0,0,0,8"/>
    <TextBlock Text="Caption Trigger" FontSize="11" Margin="0,0,0,5" Foreground="#374151"/>
    <TextBox x:Name="TxtOcrTriggerCaption" Style="{StaticResource InputStyle}"
             Margin="0,0,0,4" Text="/struk"/>
    <TextBlock Style="{StaticResource HelpTextStyle}"
               Text="Kirim foto struk ke bot dengan caption ini untuk aktivasi OCR."/>
    <TextBlock Text="Tessdata Path" FontSize="11" Margin="0,8,0,5" Foreground="#374151"/>
    <Grid Margin="0,0,0,4">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBox x:Name="TxtTessdataPath" Grid.Column="0"
               Style="{StaticResource InputStyle}" Margin="0,0,8,0"/>
      <Button x:Name="BtnBrowseTessdata" Grid.Column="1"
              Content="Browse" Style="{StaticResource SmallButtonStyle}"
              Click="BtnBrowseTessdata_Click"/>
    </Grid>

    <!-- Product Mapping Table -->
    <TextBlock Text="🔗 Pemetaan Nama Produk (Faktur → Database)"
               FontSize="12" FontWeight="SemiBold" Margin="0,14,0,6"
               Foreground="#374151"/>
    <TextBlock Style="{StaticResource HelpTextStyle}"
               Text="Setup dulu di sini sebelum OCR dijalankan. Nama di kolom KIRI = nama di faktur/struk supplier. Nama di kolom KANAN = nama produk di database Aronium."/>

    <!-- Add Mapping Row -->
    <Grid Margin="0,0,0,8">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBox x:Name="TxtMappingInvoiceName" Grid.Column="0"
               Style="{StaticResource InputStyle}" Margin="0,0,6,0"
               Tag="Nama di faktur supplier"/>
      <TextBox x:Name="TxtMappingDbName" Grid.Column="1"
               Style="{StaticResource InputStyle}" Margin="0,0,6,0"
               Tag="Nama produk di database"/>
      <Button x:Name="BtnSearchDbProduct" Grid.Column="2"
              Content="🔍" Style="{StaticResource SmallButtonStyle}"
              Margin="0,0,4,0" Click="BtnSearchDbProduct_Click"
              ToolTip="Cari produk di database Aronium"/>
      <Button x:Name="BtnAddMapping" Grid.Column="3"
              Content="+ Tambah" Style="{StaticResource SaveButtonStyle}"
              Padding="10,6" FontSize="11"
              Click="BtnAddMapping_Click"/>
    </Grid>

    <!-- Mapping DataGrid -->
    <DataGrid x:Name="DgProductMappings"
              AutoGenerateColumns="False"
              CanUserAddRows="False"
              CanUserDeleteRows="False"
              HeadersVisibility="Column"
              Height="180"
              FontSize="11"
              BorderBrush="#E5E7EB" BorderThickness="1">
      <DataGrid.Columns>
        <DataGridTextColumn Header="Nama di Faktur" Width="*"
                            Binding="{Binding InvoiceName}"/>
        <DataGridTextColumn Header="Produk di Database" Width="*"
                            Binding="{Binding DatabaseProductName}"/>
        <DataGridTextColumn Header="ID" Width="60"
                            Binding="{Binding DatabaseProductId}"
                            IsReadOnly="True"/>
        <DataGridTemplateColumn Header="" Width="55">
          <DataGridTemplateColumn.CellTemplate>
            <DataTemplate>
              <Button Content="🗑" Tag="{Binding InvoiceName}"
                      Style="{StaticResource SmallButtonStyle}"
                      Click="BtnDeleteMapping_Click"/>
            </DataTemplate>
          </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
      </DataGrid.Columns>
    </DataGrid>
    <TextBlock x:Name="TxtOcrValidation" Style="{StaticResource StatusTextStyle}"/>
  </StackPanel>
</Border>

<!-- Google Sheets Settings -->
<Border Style="{StaticResource SectionStyle}">
  <StackPanel>
    <TextBlock Text="📊 Google Sheets Export" FontSize="14" FontWeight="SemiBold"
               Margin="0,0,0,10" Foreground="#0F766E"/>
    <CheckBox x:Name="ChkSheetsEnabled" Content="Aktifkan export ke Google Sheets"
              Margin="0,0,0,8"/>
    <TextBlock Text="Service Account JSON Path" FontSize="11" Margin="0,0,0,5" Foreground="#374151"/>
    <Grid Margin="0,0,0,4">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBox x:Name="TxtSheetsCredentialPath" Grid.Column="0"
               Style="{StaticResource InputStyle}" Margin="0,0,8,0"/>
      <Button x:Name="BtnBrowseSheetsCredential" Grid.Column="1"
              Content="Browse" Style="{StaticResource SmallButtonStyle}"
              Click="BtnBrowseSheetsCredential_Click"/>
    </Grid>
    <TextBlock Text="Spreadsheet ID" FontSize="11" Margin="0,8,0,5" Foreground="#374151"/>
    <TextBox x:Name="TxtSheetsSpreadsheetId" Style="{StaticResource InputStyle}" Margin="0,0,0,4"/>
    <TextBlock Text="Nama Sheet Tab" FontSize="11" Margin="0,8,0,5" Foreground="#374151"/>
    <TextBox x:Name="TxtSheetsPurchaseTabName" Style="{StaticResource InputStyle}"
             Text="Pembelian" Margin="0,0,0,4"/>
    <Button x:Name="BtnTestSheets" Content="Test Koneksi Sheets"
            Style="{StaticResource SmallButtonStyle}" HorizontalAlignment="Right"
            Click="BtnTestSheets_Click"/>
    <TextBlock x:Name="TxtSheetsValidation" Style="{StaticResource StatusTextStyle}"/>
  </StackPanel>
</Border>
```

#### [MODIFY] `Views/SettingsView.xaml.cs`
Tambah handler:
- `BtnAddMapping_Click` — validasi + tambah ke `_mappings` list + refresh DataGrid
- `BtnDeleteMapping_Click` — hapus dari list
- `BtnSearchDbProduct_Click` — buka search dialog / combobox live search ke PosDbService
- `BtnBrowseTessdata_Click` — folder picker
- `BtnBrowseSheetsCredential_Click` — file picker JSON
- `BtnTestSheets_Click` — test write ke Google Sheets
- Load/Save mapping dari/ke `config.json` via `ConfigService`

---

### Component 5 — Telegram Photo Handler

#### [MODIFY] `Services/TelegramBotService.cs`
Update handler foto di `HandleUpdateAsync()` (line 196-203):
- Ganti response "OCR belum diaktifkan" dengan actual OCR flow
- Download foto highest resolution
- Panggil `OcrReceiptService`
- Simpan hasil pending ke `DatabaseService` dengan command `ocr_receipt`
- Reply preview + tombol **[✅ KONFIRMASI]** / **[❌ BATAL]**

---

### Component 6 — AutomationEngine OCR Flow

#### [MODIFY] `Services/AutomationEngine.cs`
- Tambah command `/struk` di `HandleCommandAsync()` switch:
  ```csharp
  "/struk" => context.IsOwner ? "Kirim foto struk sebagai foto (bukan file) dengan caption /struk" : BuildOwnerOnlyDeniedMessage(),
  ```
- Tambah `ConfirmPendingActionAsync()` handler untuk command `ocr_receipt`:
  - Loop semua `OcrLineItem` yang di-match
  - Panggil `CreatePurchaseDocumentAsync()` per item (atau satu bulk document)
  - Panggil `GoogleSheetsService.AppendPurchaseRowsAsync()`
  - Build success summary message
- Tambah `HandleOcrPhotoAsync(message, photoFileId)` — dipanggil dari `TelegramBotService` saat foto masuk

---

### Component 7 — ShouldAttachConfirmationKeyboard

#### [MODIFY] `Services/TelegramBotService.cs`
Tambah pattern OCR receipt ke `ShouldAttachConfirmationKeyboard()`:
```csharp
|| message.Text.StartsWith("🧾 PREVIEW STRUK OCR", StringComparison.OrdinalIgnoreCase)
```

---

### Component 8 — Help Text Update

#### [MODIFY] `Services/AutomationEngine.cs`
Update `BuildHelpText()` untuk tambahkan dokumentasi perintah OCR.

---

## Flow Detail

### Saat Foto Dikirim
```
1. TelegramBotService.HandleUpdateAsync()
   → cek apakah MessageType.Photo
   → cek caption mengandung "/struk" ATAU config AutoTriggerAllPhotos=true
   → panggil _automationEngine.HandleOcrPhotoAsync(message, fileId)

2. HandleOcrPhotoAsync()
   → Download foto via GetFileAsync() + HTTP stream ke temp file
   → OcrReceiptService.ExtractTextFromImageAsync() → raw text
   → OcrReceiptService.ParseReceiptLines() → List<OcrLineItem>
   → OcrReceiptService.FuzzyMatchProductsAsync() → produk dicocokkan
   → SavePendingConfirmation(command="ocr_receipt", data=JSON items)
   → return preview message
```

### Saat Konfirmasi YA
```
3. ConfirmPendingActionAsync() → command == "ocr_receipt"
   → deserialize items JSON
   → filter hanya item yang IsMatched = true
   → PosDbService.CreateBulkPurchaseDocumentAsync(matchedItems)
     ↳ SATU Document header (Type 1)
     ↳ N DocumentItem dalam satu transaksi
     ↳ UPDATE Stock semua produk
     ↳ COMMIT
   → GoogleSheetsService.AppendPurchaseRowsAsync(allItems, docNumber)
   → BuildOcrSuccessMessage()
```

---

## Preview Message Format

```
🧾 PREVIEW STRUK OCR

📋 Supplier: WINGS FOOD (terdeteksi)
📅 Tanggal: 30-03-2026

Item yang ditemukan (6):
✅ Sedap Mie Bag 690gr    5 pcs × Rp 99.400  = Rp 497.000
✅ Sedap Mie Bag 770gr    2 pcs × Rp 108.380 = Rp 216.760
✅ Krisbee 7 Bag 656gr   15 pcs × Rp  5.750  = Rp  86.250
⚠️ "Potabee Reg BBQ" → tidak cocok (skip)
✅ Palmia 200gr            1 dus × Rp 367.000 = Rp 367.000
⚠️ "Minyak 20kg" → tidak cocok (skip)

Total: Rp 1.167.010
Akan buat 1 dokumen Purchase bulk (4 item) + update stok + push ke Sheets.

Lanjutkan? [✅ YA] [❌ BATAL]
```

---

## Success Message Format

```
✅ STRUK BERHASIL DIPROSES

📦 Dokumen Purchase: 26-100-000010
   Sedap Mie Bag 690gr     ×5   = Rp 497.000
   Sedap Mie Bag 770gr     ×2   = Rp 216.760
   Krisbee 7 Bag 656gr    ×15   = Rp  86.250
   Palmia 200gr             ×1   = Rp 367.000
   ─────────────────────────────────────────
   Total                         Rp 1.167.010

📊 Stok 4 produk otomatis diperbarui di Aronium.
📤 Data dikirim ke Google Sheets tab "Pembelian".
⚠️ 2 item diskip (tidak ada di mapping/database).
```

---

## Verification Plan

### Automated Build Check
```powershell
cd "d:\HOME\smart sembako\Smart-Sembako-Assistant\SmartSembakoAssistant"
dotnet build SmartSembakoAssistant.csproj
```

### Manual Test Flow
1. Kirim foto `image.png` (surat jalan Wings) ke Telegram bot dengan caption `/struk`
2. Bot harus reply preview dengan ≥4 produk matched
3. Klik YA → cek Aronium database ada dokumen Purchase baru
4. Cek Google Sheet tab "Pembelian" ada baris baru
5. Kirim struk Tani Makmur Putra (teks copy-paste) → cek parser Tipe B

### Files yang Dibuat/Dimodifikasi
| File | Aksi | Estimasi |
|------|------|----------|
| `Services/OcrReceiptService.cs` | NEW | ~400 baris |
| `Services/GoogleSheetsService.cs` | NEW | ~200 baris |
| `Services/TelegramBotService.cs` | MODIFY | +80 baris |
| `Services/AutomationEngine.cs` | MODIFY | +250 baris |
| `Services/PosDbService.cs` | MODIFY | +150 baris (CreateBulkPurchaseDocumentAsync) |
| `Services/ConfigService.cs` | MODIFY | +50 baris (model baru) |
| `Views/SettingsView.xaml` | MODIFY | +130 baris (OCR + Sheets sections) |
| `Views/SettingsView.xaml.cs` | MODIFY | +200 baris (handlers mapping) |
| `config.json` | MODIFY | tambah OcrReceipt + GoogleSheets sections |
