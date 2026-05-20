# 🎨 UI Wireframe: OCR & Google Sheets (Sesuai XAML Project Asli)

Sesuai permintaan Anda, desain ini mengikuti struktur, margin, dan resource dictionary (seperti `SectionStyle`, `InputStyle`, `SmallButtonStyle`) yang benar-benar ada di dalam `d:\HOME\smart sembako\Smart-Sembako-Assistant\SmartSembakoAssistant\Views\SettingsView.xaml`.

Berikut adalah representasi visual dan struktur XAML rill yang akan kita terapkan di bagian bawah `SettingsView.xaml` (tepat sebelum tombol "Test All Connections" & "Save Settings").

---

## 1. Section: 📷 OCR Struk Pembelian

Bagian ini menggunakan `#7C3AED` (Ungu) sebagai warna aksen judul agar selaras dengan desain section Tunnel sebelumnya.

### Visual Representation
```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ 📷 OCR Struk Pembelian                                                      │
│                                                                             │
│ [✓] Aktifkan OCR struk via Telegram                                         │
│                                                                             │
│ Caption Trigger                                                             │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ /struk                                                                  │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│ ℹ️ Kirim foto struk ke bot dengan caption ini untuk aktivasi OCR.           │
│                                                                             │
│ Tessdata Path                                                               │
│ ┌───────────────────────────────────────────────────────────────┐ ┌───────┐ │
│ │ tessdata                                                      │ │ Browse│ │
│ └───────────────────────────────────────────────────────────────┘ └───────┘ │
│                                                                             │
│ 🧠 Engine Parser OCR (Deteksi Vendor Otomatis)                              │
│ Mode: [ Auto-Detect Vendor ▼ ]                                              │
│ Parsers Aktif: TaniParser, WingsParser, GenericParser                       │
│                                                                             │
│ ─────────────────────────────────────────────────────────────────────────── │
│                                                                             │
│ 🔗 Pemetaan Nama Produk (Faktur → Database)                                 │
│ ℹ️ Setup dulu di sini sebelum OCR dijalankan. Nama di kolom KIRI = nama di  │
│    faktur/struk supplier. Nama di kolom KANAN = nama produk di database.    │
│                                                                             │
│ ┌──────────────────────────┐ ┌──────────────────────────┐ ┌───┐ ┌─────────┐ │
│ │ Nama di faktur supplier  │ │ Nama produk di database  │ │ 🔍│ │ + Tambah│ │
│ └──────────────────────────┘ └──────────────────────────┘ └───┘ └─────────┘ │
│                                                                             │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ Nama di Faktur                 Produk di Database         ID      Aksi  │ │
│ ├─────────────────────────────────────────────────────────────────────────┤ │
│ │ Sedap Mie Bag 690gr Ayam...    Mie Sedap Ayam 690gr       123    [ 🗑 ] │ │
│ │ Pop Ice                        Pop Ice Sachet             45     [ 🗑 ] │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│ Status Text Validasi (Warna Biru)                                           │
└─────────────────────────────────────────────────────────────────────────────┘
```

### XAML Code Implementation
Kode ini langsung bisa di-*copy-paste* ke `SettingsView.xaml`:

```xml
<Border Style="{StaticResource SectionStyle}">
    <StackPanel>
        <TextBlock Text="📷 OCR Struk Pembelian" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,10" Foreground="#7C3AED"/>

        <CheckBox x:Name="ChkOcrEnabled" Content="Aktifkan OCR struk via Telegram" Margin="0,0,0,8" Checked="ChkOcrEnabled_Changed" Unchecked="ChkOcrEnabled_Changed"/>

        <TextBlock Text="Caption Trigger" FontSize="11" Margin="0,0,0,5" Foreground="#374151"/>
        <TextBox x:Name="TxtOcrTriggerCaption" Style="{StaticResource InputStyle}" Margin="0,0,0,4"/>
        <TextBlock Style="{StaticResource HelpTextStyle}" Text="Kirim foto struk ke bot dengan caption ini untuk aktivasi OCR."/>

        <TextBlock Text="Tessdata Path" FontSize="11" Margin="0,8,0,5" Foreground="#374151"/>
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="TxtTessdataPath" Grid.Column="0" Style="{StaticResource InputStyle}" Margin="0,0,8,0"/>
            <Button x:Name="BtnBrowseTessdata" Grid.Column="1" Content="Browse" Style="{StaticResource SmallButtonStyle}" Click="BtnBrowseTessdata_Click"/>
        </Grid>

        <!-- OCR Parser Engine Info -->
        <TextBlock Text="🧠 Engine Parser OCR" FontSize="12" FontWeight="SemiBold" Margin="0,14,0,6" Foreground="#374151"/>
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Text="Mode Deteksi:" FontSize="11" Margin="0,0,8,0" VerticalAlignment="Center" Foreground="#374151"/>
            <ComboBox x:Name="CmbParserMode" Grid.Column="1" Style="{StaticResource ComboStyle}" Width="200" HorizontalAlignment="Left">
                <ComboBoxItem Content="Auto-Detect (Factory Pattern)" IsSelected="True"/>
                <ComboBoxItem Content="Paksa Generic Parser"/>
            </ComboBox>
        </Grid>
        <TextBlock Style="{StaticResource HelpTextStyle}" Text="Parsers Aktif: Tani Makmur (TaniParser), Wings Food (WingsParser), Fallback (GenericParser)"/>

        <!-- Product Mapping Table -->
        <TextBlock Text="🔗 Pemetaan Nama Produk (Faktur → Database)" FontSize="12" FontWeight="SemiBold" Margin="0,14,0,6" Foreground="#374151"/>
        <TextBlock Style="{StaticResource HelpTextStyle}" Text="Setup dulu di sini sebelum OCR dijalankan. Nama di kolom KIRI = nama di faktur/struk supplier. Nama di kolom KANAN = nama produk di database Aronium."/>

        <!-- Add Mapping Row -->
        <Grid Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="TxtMappingInvoiceName" Grid.Column="0" Style="{StaticResource InputStyle}" Margin="0,0,6,0" Tag="Nama di faktur supplier"/>
            <TextBox x:Name="TxtMappingDbName" Grid.Column="1" Style="{StaticResource InputStyle}" Margin="0,0,6,0" Tag="Nama produk di database"/>
            <Button x:Name="BtnSearchDbProduct" Grid.Column="2" Content="🔍" Style="{StaticResource SmallButtonStyle}" Margin="0,0,4,0" Click="BtnSearchDbProduct_Click" ToolTip="Cari produk di database Aronium"/>
            <Button x:Name="BtnAddMapping" Grid.Column="3" Content="+ Tambah" Style="{StaticResource SaveButtonStyle}" Padding="10,6" FontSize="11" Click="BtnAddMapping_Click"/>
        </Grid>

        <!-- Mapping DataGrid -->
        <DataGrid x:Name="DgProductMappings" AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False" HeadersVisibility="Column" Height="180" FontSize="11" BorderBrush="#E5E7EB" BorderThickness="1">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Nama di Faktur" Width="*" Binding="{Binding InvoiceName}"/>
                <DataGridTextColumn Header="Produk di Database" Width="*" Binding="{Binding DatabaseProductName}"/>
                <DataGridTextColumn Header="ID" Width="60" Binding="{Binding DatabaseProductId}" IsReadOnly="True"/>
                <DataGridTemplateColumn Header="" Width="55">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="🗑" Tag="{Binding InvoiceName}" Style="{StaticResource SmallButtonStyle}" Click="BtnDeleteMapping_Click"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>

        <TextBlock x:Name="TxtOcrValidation" Style="{StaticResource StatusTextStyle}"/>
    </StackPanel>
</Border>
```

---

## 2. Section: 📊 Google Sheets Export

Bagian ini menggunakan `#0F766E` (Teal gelap) sebagai aksen judul, sama seperti section Baileys Lokal.

### Visual Representation
```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ 📊 Google Sheets Export                                                     │
│                                                                             │
│ [✓] Aktifkan export ke Google Sheets                                        │
│                                                                             │
│ Service Account JSON Path                                                   │
│ ┌───────────────────────────────────────────────────────────────┐ ┌───────┐ │
│ │ credentials/service-account.json                              │ │ Browse│ │
│ └───────────────────────────────────────────────────────────────┘ └───────┘ │
│                                                                             │
│ Spreadsheet ID                                                              │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ 1BxiMVs0XRYFgPNexI...                                                   │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│ Nama Sheet Tab Pembelian                                                    │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ Pembelian                                                               │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│                                                ┌──────────────────────────┐ │
│                                                │   Test Koneksi Sheets    │ │
│                                                └──────────────────────────┘ │
│ Status Text Validasi (Warna Biru)                                           │
└─────────────────────────────────────────────────────────────────────────────┘
```

### XAML Code Implementation
```xml
<Border Style="{StaticResource SectionStyle}">
    <StackPanel>
        <TextBlock Text="📊 Google Sheets Export" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,10" Foreground="#0F766E"/>
        
        <CheckBox x:Name="ChkSheetsEnabled" Content="Aktifkan export ke Google Sheets" Margin="0,0,0,8" Checked="ChkSheetsEnabled_Changed" Unchecked="ChkSheetsEnabled_Changed"/>
        
        <TextBlock Text="Service Account JSON Path" FontSize="11" Margin="0,0,0,5" Foreground="#374151"/>
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="TxtSheetsCredentialPath" Grid.Column="0" Style="{StaticResource InputStyle}" Margin="0,0,8,0"/>
            <Button x:Name="BtnBrowseSheetsCredential" Grid.Column="1" Content="Browse" Style="{StaticResource SmallButtonStyle}" Click="BtnBrowseSheetsCredential_Click"/>
        </Grid>
        
        <TextBlock Text="Spreadsheet ID" FontSize="11" Margin="0,8,0,5" Foreground="#374151"/>
        <TextBox x:Name="TxtSheetsSpreadsheetId" Style="{StaticResource InputStyle}" Margin="0,0,0,4"/>
        
        <TextBlock Text="Nama Sheet Tab Pembelian" FontSize="11" Margin="0,8,0,5" Foreground="#374151"/>
        <TextBox x:Name="TxtSheetsPurchaseTabName" Style="{StaticResource InputStyle}" Text="Pembelian" Margin="0,0,0,10"/>
        
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="BtnTestSheets" Content="Test Koneksi Sheets" Style="{StaticResource SmallButtonStyle}" Click="BtnTestSheets_Click"/>
        </StackPanel>
        
        <TextBlock x:Name="TxtSheetsValidation" Style="{StaticResource StatusTextStyle}"/>
    </StackPanel>
</Border>
```

---

## 3. Section: 🕒 Antrean Perbaikan OCR (Retry Queue)

Bagian ini digunakan sebagai antrean dokumen faktur yang memiliki status `NEEDS_REVIEW` (Tingkat Toleransi Hybrid).

### Visual Representation
```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ 🕒 Antrean Perbaikan OCR                                                    │
│ ℹ️ Item di bawah ini memiliki nilai error saat OCR, klik tombol Perbaiki    │
│    untuk input manual nama/harga ke database.                               │
│                                                                             │
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ Tanggal        Supplier      Teks OCR Mentah          Status      Aksi  │ │
│ ├─────────────────────────────────────────────────────────────────────────┤ │
│ │ 04-05-2026     WINGS FOOD    "Potabee Reg BBQ"        REVIEW     [ ✏️ ] │ │
│ │ 04-05-2026     TANI MAKMUR   "aci sob 10000"          REVIEW     [ ✏️ ] │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Fitur Window Dialog Pencarian (SearchDBProductWindow)

Karena WPF bawaan tidak memiliki dialog picker out-of-the-box, saat user klik 🔍, kita akan memunculkan sebuah Window terpisah berukuran kecil (`WindowStartupLocation="CenterOwner"`) berisikan `TextBox` pencarian dan `ListBox` hasil pencarian.

### Visual Representation (Search Window)
```text
┌────────────────────────────────────────────────────────┐
│ Cari Produk Aronium                                  ✖ │
├────────────────────────────────────────────────────────┤
│ ┌───────────────────────────────────────────┐ ┌──────┐ │
│ │ mie sedap                                 │ │ Cari │ │
│ └───────────────────────────────────────────┘ └──────┘ │
│                                                        │
│ ┌────────────────────────────────────────────────────┐ │
│ │ [26-100-001] Mie Sedap Goreng 90gr                 │ │
│ │ [26-100-002] Mie Sedap Soto 400gr                  │ │
│ │ [26-100-003] Mie Sedap Ayam 690gr                  │ │
│ └────────────────────────────────────────────────────┘ │
│                                                        │
│                                           [ Pilih ]    │
└────────────────────────────────────────────────────────┘
```

Apakah representasi kode XAML rill beserta tampilannya ini sudah cukup presisi dan akurat dengan aplikasi yang sedang berjalan? Jika setuju, saya akan langsung memasukkannya ke `SettingsView.xaml`!
