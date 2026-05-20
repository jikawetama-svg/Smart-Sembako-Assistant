# ============================================================
#  Smart Sembako Assistant -- Auto Commit Script
#  Repo  : https://github.com/jikawetama-svg/Smart-Sembako-Assistant
#  Author: SA TECH.Inc
#
#  Usage:
#    .\auto-commit.ps1                      -> commit semua, pesan otomatis
#    .\auto-commit.ps1 -Message "fix: ..."  -> pesan commit custom
#    .\auto-commit.ps1 -DryRun              -> preview tanpa push
#    .\auto-commit.ps1 -NoPush              -> commit tanpa push
#    .\auto-commit.ps1 -PushOnly            -> push saja
# ============================================================

param(
    [string]$Message  = "",
    [switch]$DryRun   = $false,
    [switch]$PushOnly = $false,
    [switch]$NoPush   = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# -- Helper warna -----------------------------------------------
function Write-Color {
    param([string]$Text, [string]$Color = "White")
    Write-Host $Text -ForegroundColor $Color
}
function Write-Header { Write-Color ("=" * 62) Cyan }
function Write-OK   ([string]$msg) { Write-Color "  [OK]   $msg" Green }
function Write-WARN ([string]$msg) { Write-Color "  [WARN] $msg" Yellow }
function Write-ERR  ([string]$msg) { Write-Color "  [ERR]  $msg" Red }
function Write-INFO ([string]$msg) { Write-Color "  [INFO] $msg" Cyan }
function Write-STEP ([string]$msg) { Write-Color "`n>> $msg" DarkCyan }

# -- Konstanta --------------------------------------------------
$ROOT       = $PSScriptRoot
$CHANGELOG  = Join-Path $ROOT "SmartSembakoAssistant\changelog.json"
$BRANCH     = "main"
$REMOTE     = "origin"
$TIMESTAMP  = Get-Date -Format "yyyy-MM-dd HH:mm"
$DATE_SHORT = Get-Date -Format "yyyy-MM-dd"

# -- Banner -----------------------------------------------------
Write-Header
Write-Color "  Smart Sembako Assistant -- Auto Commit to GitHub" Cyan
Write-Color "  Date   : $TIMESTAMP WIB" Gray
Write-Color "  Folder : $ROOT" Gray
if ($DryRun)   { Write-Color "  MODE   : DRY RUN (tidak ada push)" Yellow }
if ($PushOnly) { Write-Color "  MODE   : PUSH ONLY" Yellow }
Write-Header

# -- Validasi repo ----------------------------------------------
Write-STEP "Memvalidasi git repository..."
if (-not (Test-Path (Join-Path $ROOT ".git"))) {
    Write-ERR "Folder ini bukan git repository!"
    exit 1
}

Set-Location $ROOT

# -- Ambil versi dari changelog.json ---------------------------
Write-STEP "Membaca versi dari changelog.json..."
$VERSION = "unknown"
if (Test-Path $CHANGELOG) {
    try {
        $cl     = Get-Content $CHANGELOG -Raw -Encoding UTF8 | ConvertFrom-Json
        $latest = $cl.entries | Where-Object { $_.status -in @("draft","implemented","released") } | Select-Object -First 1
        if ($latest) {
            $VERSION = "v$($latest.version)"
            Write-OK "Versi: $VERSION -- $($latest.title)"
        }
    } catch {
        Write-WARN "Gagal baca changelog.json: $_"
    }
} else {
    Write-WARN "changelog.json tidak ditemukan."
}

# -- Cek status git ---------------------------------------------
Write-STEP "Mengecek perubahan file..."
$gitStatus    = git status --porcelain 2>&1
$changedFiles = @($gitStatus | Where-Object { $_.Trim() -ne "" })

if ($PushOnly) {
    Write-INFO "Mode PUSH ONLY -- melewati stage dan commit."
} elseif ($changedFiles.Count -eq 0) {
    Write-OK "Tidak ada perubahan untuk di-commit."
    if (-not $NoPush) {
        Write-STEP "Push ke $REMOTE/$BRANCH..."
        if (-not $DryRun) { git push $REMOTE $BRANCH }
        Write-OK "Push selesai (tidak ada perubahan baru)."
    }
    exit 0
} else {
    Write-INFO "Ditemukan $($changedFiles.Count) file berubah:"

    # -- Klasifikasikan per ekstensi ----------------------------
    $mdFiles    = @()
    $csFiles    = @()
    $jsonFiles  = @()
    $xamlFiles  = @()
    $jsFiles    = @()
    $otherFiles = @()

    foreach ($line in $changedFiles) {
        $status   = $line.Substring(0,2).Trim()
        $filePath = $line.Substring(3).Trim().Trim('"')
        $ext      = [IO.Path]::GetExtension($filePath).ToLower()
        $fileName = [IO.Path]::GetFileName($filePath)

        $icon = switch ($status) {
            "M"  { "[M]" }; "MM" { "[M]" }; " M" { "[M]" }
            "A"  { "[A]" }; "AM" { "[A]" }
            "D"  { "[D]" }; " D" { "[D]" }
            "R"  { "[R]" }
            "??" { "[N]" }
            default { "[?]" }
        }

        Write-Color "     $icon  $status  $filePath" Gray

        switch ($ext) {
            ".md"   { $mdFiles    += $fileName }
            ".cs"   { $csFiles    += $fileName }
            ".json" { $jsonFiles  += $fileName }
            ".xaml" { $xamlFiles  += $fileName }
            ".js"   { $jsFiles    += $fileName }
            default { $otherFiles += $fileName }
        }
    }

    # -- Buat pesan commit otomatis ----------------------------
    Write-STEP "Menyusun pesan commit..."

    if ($Message -ne "") {
        $commitMsg = $Message
        Write-INFO "Pakai pesan custom: $commitMsg"
    } else {
        # Prefix
        $prefix = "chore"
        if ($csFiles.Count -gt 0 -or $jsFiles.Count -gt 0)   { $prefix = "feat"    }
        if ($mdFiles.Count -gt 0 -and $csFiles.Count -eq 0)  { $prefix = "docs"    }
        if ($jsonFiles -contains "changelog.json")            { $prefix = "release" }

        # Subjek
        $parts = @()
        if ($csFiles.Count   -gt 0) { $parts += "$($csFiles.Count) C# file"   }
        if ($jsFiles.Count   -gt 0) { $parts += "$($jsFiles.Count) JS file"   }
        if ($xamlFiles.Count -gt 0) { $parts += "$($xamlFiles.Count) XAML"    }
        if ($jsonFiles.Count -gt 0) { $parts += "$($jsonFiles.Count) JSON"     }
        if ($mdFiles.Count   -gt 0) { $parts += "$($mdFiles.Count) MD doc"    }
        if ($otherFiles.Count -gt 0){ $parts += "$($otherFiles.Count) lainnya"}

        $summary   = if ($parts.Count -gt 0) { $parts -join ", " } else { "update project" }
        $commitMsg = "$prefix($VERSION): $summary [$DATE_SHORT]"

        # Body detail
        $bodyLines = @()
        if ($mdFiles.Count -gt 0) {
            $bodyLines += ""
            $bodyLines += "Docs diperbarui:"
            foreach ($f in $mdFiles)  { $bodyLines += "  - $f" }
        }
        if ($csFiles.Count -gt 0) {
            $bodyLines += ""
            $bodyLines += "Source diperbarui:"
            foreach ($f in ($csFiles | Select-Object -Unique | Select-Object -First 8)) { $bodyLines += "  - $f" }
        }
        if ($jsFiles.Count -gt 0) {
            $bodyLines += ""
            $bodyLines += "Sidecar/JS diperbarui:"
            foreach ($f in $jsFiles)  { $bodyLines += "  - $f" }
        }
        if ($xamlFiles.Count -gt 0) {
            $bodyLines += ""
            $bodyLines += "XAML diperbarui:"
            foreach ($f in $xamlFiles) { $bodyLines += "  - $f" }
        }

        if ($bodyLines.Count -gt 0) {
            $commitMsg = "$commitMsg`n$($bodyLines -join "`n")"
        }
    }

    # Tampilkan preview pesan
    Write-Color (("-" * 50)) Gray
    Write-Color $commitMsg White
    Write-Color (("-" * 50)) Gray

    # -- Stage semua perubahan ---------------------------------
    Write-STEP "Staging semua perubahan (git add --all)..."
    if (-not $DryRun) {
        git add --all
        Write-OK "Staging selesai."
    } else {
        Write-WARN "[DRY RUN] git add --all dilewati."
    }

    # -- Commit ------------------------------------------------
    Write-STEP "Membuat commit..."
    if (-not $DryRun) {
        git commit -m $commitMsg
        if ($LASTEXITCODE -ne 0) {
            Write-ERR "Commit gagal! Exit code: $LASTEXITCODE"
            exit 1
        }
        Write-OK "Commit berhasil."
    } else {
        Write-WARN "[DRY RUN] git commit dilewati."
    }
}

# -- Push ke GitHub --------------------------------------------
if ($NoPush) {
    Write-WARN "Flag -NoPush aktif -- push dilewati."
} elseif ($DryRun) {
    Write-WARN "[DRY RUN] git push $REMOTE $BRANCH dilewati."
} else {
    Write-STEP "Push ke GitHub ($REMOTE/$BRANCH)..."
    git push $REMOTE $BRANCH
    if ($LASTEXITCODE -ne 0) {
        Write-ERR "Push gagal! Coba: git pull $REMOTE $BRANCH --rebase"
        exit 1
    }
    $lastHash = git rev-parse --short HEAD
    Write-OK "Push berhasil!"
    Write-Color "  Link : https://github.com/jikawetama-svg/Smart-Sembako-Assistant/commit/$lastHash" DarkYellow
}

Write-Header
Write-Color "  [DONE] Auto Commit Selesai -- $TIMESTAMP WIB" Green
Write-Header
Write-Host ""
