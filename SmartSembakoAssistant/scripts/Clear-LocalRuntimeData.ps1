param(
    [switch]$NoBackup,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$productRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Smart Sembako Assistant"
$dataRoot = Join-Path $productRoot "data"

if (!(Test-Path -LiteralPath $productRoot)) {
    Write-Host "Runtime lokal tidak ditemukan: $productRoot"
    return
}

Write-Host "Target runtime lokal:"
Write-Host "  $productRoot"
Write-Host ""
Write-Host "Yang akan dibersihkan:"
Write-Host "  data\memory.db"
Write-Host "  data\baileys-session"
Write-Host "  data\ocr_mappings.json"
Write-Host "  data\logs"
Write-Host ""
Write-Host "config.json tidak dihapus oleh script ini."
Write-Host ""

if (!$Force) {
    $answer = Read-Host "Ketik RESET untuk melanjutkan"
    if ($answer -ne "RESET") {
        Write-Host "Dibatalkan."
        return
    }
}

if (!$NoBackup) {
    $backupDir = Join-Path $productRoot "backups"
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    $backupPath = Join-Path $backupDir ("runtime-backup-{0:yyyyMMdd-HHmmss}.zip" -f (Get-Date))

    $backupItems = @()
    foreach ($relative in @("data\memory.db", "data\baileys-session", "data\ocr_mappings.json", "data\logs")) {
        $path = Join-Path $productRoot $relative
        if (Test-Path -LiteralPath $path) {
            $backupItems += $path
        }
    }

    if ($backupItems.Count -gt 0) {
        Compress-Archive -LiteralPath $backupItems -DestinationPath $backupPath -Force
        Write-Host "Backup dibuat:"
        Write-Host "  $backupPath"
    }
}

foreach ($relative in @("data\memory.db", "data\baileys-session", "data\ocr_mappings.json", "data\logs")) {
    $path = Join-Path $productRoot $relative
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host "Dihapus: $relative"
    }
}

New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
Write-Host "Selesai."
