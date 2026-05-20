param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "1.0.0",
    [string]$InnoCompilerPath = "",
    [string]$NodeRuntimeSourceDir = "",
    [string]$NodeBinaryPath = "",
    [string]$CloudflaredBinaryPath = "",
    [switch]$KeepStaging
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$solutionRoot = Split-Path -Parent $projectRoot
$projectFile = Join-Path $projectRoot "SmartSembakoAssistant.csproj"
$artifactsRoot = Join-Path $projectRoot "artifacts"
$stagingRoot = Join-Path $artifactsRoot "staging"
$publishDir = Join-Path $stagingRoot "publish\$RuntimeIdentifier"
$installerScript = Join-Path $projectRoot "deployment\SmartSembakoAssistant.iss"
$installerOutDir = Join-Path $artifactsRoot "installer"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)

    Write-Host "OK  $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)

    Write-Host "    $Message" -ForegroundColor DarkGray
}

function Reset-Directory {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Initialize-CleanRuntimeLayout {
    param([string]$Root)

    $configFile = Join-Path $Root "config.json"
    $dataDir = Join-Path $Root "data"
    $logsDir = Join-Path $dataDir "logs"
    $sessionDir = Join-Path $dataDir "baileys-session"
    $portableMarker = Join-Path $Root "portable.mode"

    foreach ($path in @($configFile, $portableMarker)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    if (Test-Path -LiteralPath $dataDir) {
        Remove-Item -LiteralPath $dataDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null
}

function Resolve-InnoCompiler {
    param([string]$ExplicitPath)

    if (!([string]::IsNullOrWhiteSpace($ExplicitPath))) {
        if (Test-Path -LiteralPath $ExplicitPath) {
            return (Resolve-Path -LiteralPath $ExplicitPath).Path
        }

        throw "Inno Setup compiler tidak ditemukan di path: $ExplicitPath"
    }

    $programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
    $programFiles = [Environment]::GetFolderPath("ProgramFiles")
    $candidates = @(
        (Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"),
        (Join-Path $programFiles "Inno Setup 6\ISCC.exe"),
        "D:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { ![string]::IsNullOrWhiteSpace($_) }

    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($found) {
        return (Resolve-Path -LiteralPath $found).Path
    }

    throw @"
Inno Setup compiler (ISCC.exe) tidak ditemukan.

Install Inno Setup 6 dulu, lalu jalankan lagi:
  https://jrsoftware.org/isdl.php

Atau jalankan script dengan path manual:
  powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version $Version -InnoCompilerPath "C:\Path\To\ISCC.exe"
"@
}

function Assert-FileExists {
    param(
        [string]$Path,
        [string]$Label
    )

    if (!(Test-Path -LiteralPath $Path)) {
        throw "$Label tidak ditemukan: $Path"
    }
}

function Resolve-NodeRuntimeSource {
    if (!([string]::IsNullOrWhiteSpace($NodeRuntimeSourceDir))) {
        if (Test-Path -LiteralPath (Join-Path $NodeRuntimeSourceDir "node.exe")) {
            return @{
                Type = "Directory"
                Path = (Resolve-Path -LiteralPath $NodeRuntimeSourceDir).Path
            }
        }

        throw "Node runtime source tidak valid. node.exe tidak ditemukan di: $NodeRuntimeSourceDir"
    }

    if (!([string]::IsNullOrWhiteSpace($NodeBinaryPath))) {
        if (Test-Path -LiteralPath $NodeBinaryPath) {
            return @{
                Type = "File"
                Path = (Resolve-Path -LiteralPath $NodeBinaryPath).Path
            }
        }

        throw "Node binary tidak ditemukan: $NodeBinaryPath"
    }

    $runtimeDirCandidates = @(
        (Join-Path $projectRoot "vendor\node-win-x64"),
        (Join-Path $projectRoot ".build-cache\node-win-x64"),
        (Join-Path $solutionRoot ".build-cache\node-win-x64")
    )

    foreach ($candidate in $runtimeDirCandidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "node.exe")) {
            return @{
                Type = "Directory"
                Path = (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if ($nodeCommand -and (Test-Path -LiteralPath $nodeCommand.Source)) {
        return @{
            Type = "File"
            Path = (Resolve-Path -LiteralPath $nodeCommand.Source).Path
        }
    }

    throw @"
Node.js runtime tidak ditemukan untuk dibundel.

Sediakan salah satu:
  - folder Node portable berisi node.exe lewat -NodeRuntimeSourceDir
  - file node.exe lewat -NodeBinaryPath
  - install Node.js di mesin build agar script bisa mengambil node.exe dari PATH
"@
}

function Resolve-CloudflaredBinary {
    if (!([string]::IsNullOrWhiteSpace($CloudflaredBinaryPath))) {
        if (Test-Path -LiteralPath $CloudflaredBinaryPath) {
            return (Resolve-Path -LiteralPath $CloudflaredBinaryPath).Path
        }

        throw "cloudflared.exe tidak ditemukan: $CloudflaredBinaryPath"
    }

    $candidates = @(
        (Join-Path $solutionRoot "cloudflared.exe"),
        (Join-Path $projectRoot "cloudflared.exe"),
        (Join-Path $projectRoot "vendor\cloudflared\cloudflared.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw @"
cloudflared.exe tidak ditemukan untuk dibundel.

Letakkan cloudflared.exe di root repo atau jalankan:
  powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -CloudflaredBinaryPath "C:\Path\cloudflared.exe"
"@
}

function Add-BundledRuntimeAssets {
    param([string]$Root)

    Write-Step "Membundel runtime Node dan cloudflared"

    $nodeTargetDir = Join-Path $Root "runtimes\node"
    $cloudflaredTargetDir = Join-Path $Root "runtimes\cloudflared"

    New-Item -ItemType Directory -Path $nodeTargetDir -Force | Out-Null
    New-Item -ItemType Directory -Path $cloudflaredTargetDir -Force | Out-Null

    $nodeSource = Resolve-NodeRuntimeSource
    if ($nodeSource.Type -eq "Directory") {
        Copy-Item -Path (Join-Path $nodeSource.Path "*") -Destination $nodeTargetDir -Recurse -Force
        Write-Ok "Node runtime folder dibundel"
        Write-Info $nodeSource.Path
    }
    else {
        Copy-Item -LiteralPath $nodeSource.Path -Destination (Join-Path $nodeTargetDir "node.exe") -Force
        Write-Ok "node.exe dibundel"
        Write-Info $nodeSource.Path
    }

    $cloudflaredSource = Resolve-CloudflaredBinary
    Copy-Item -LiteralPath $cloudflaredSource -Destination (Join-Path $cloudflaredTargetDir "cloudflared.exe") -Force
    Write-Ok "cloudflared.exe dibundel"
    Write-Info $cloudflaredSource
}

function Assert-ReleasePayload {
    param([string]$Root)

    Write-Step "Validasi payload installer"

    $requiredFiles = @(
        "SmartSembakoAssistant.exe",
        "config.template.json",
        "runtimes\node\node.exe",
        "runtimes\cloudflared\cloudflared.exe",
        "Integrations\BaileysSidecar\index.js",
        "Integrations\BaileysSidecar\package.json",
        "Integrations\BaileysSidecar\package-lock.json",
        "Integrations\BaileysSidecar\node_modules\@whiskeysockets\baileys\package.json",
        "Integrations\BaileysSidecar\node_modules\pino\package.json"
    )

    foreach ($relative in $requiredFiles) {
        $path = Join-Path $Root $relative
        if (!(Test-Path -LiteralPath $path)) {
            throw "Payload installer tidak lengkap. File wajib tidak ditemukan: $relative"
        }
    }

    Write-Ok "Payload installer lengkap"
}

try {
    Write-Host "Smart Sembako Assistant - Release Builder" -ForegroundColor White
    Write-Info "Output rilis dibuat sebagai installer .exe saja. Paket portable/zip tidak dibuat."

    Assert-FileExists -Path $projectFile -Label "Project file"
    Assert-FileExists -Path $installerScript -Label "Inno Setup script"

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (!$dotnet) {
        throw ".NET SDK tidak ditemukan. Install .NET SDK yang sesuai lalu jalankan ulang script."
    }

    Write-Step "Mencari Inno Setup compiler"
    $innoCompiler = Resolve-InnoCompiler -ExplicitPath $InnoCompilerPath
    Write-Ok "ISCC.exe ditemukan"
    Write-Info $innoCompiler

    Write-Step "Menyiapkan folder build"
    Reset-Directory -Path $artifactsRoot
    New-Item -ItemType Directory -Path $installerOutDir -Force | Out-Null
    Write-Ok "Folder artifacts siap"

    Write-Step "Publish aplikasi ($Configuration, $RuntimeIdentifier)"
    dotnet publish $projectFile `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        /p:PublishSingleFile=false `
        /p:DebugType=None `
        /p:DebugSymbols=false `
        -o $publishDir

    Initialize-CleanRuntimeLayout -Root $publishDir
    Add-BundledRuntimeAssets -Root $publishDir
    Assert-ReleasePayload -Root $publishDir
    Write-Ok "Publish selesai"
    Write-Info $publishDir

    Write-Step "Membuat installer .exe"
    & $innoCompiler "/DMyAppVersion=$Version" "/DMyAppSourceDir=$publishDir" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup gagal membuat installer. Exit code: $LASTEXITCODE"
    }

    $installer = Get-ChildItem -LiteralPath $installerOutDir -Filter "*.exe" -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (!$installer) {
        throw "Build selesai tanpa error, tetapi file installer .exe tidak ditemukan di: $installerOutDir"
    }

    if (!$KeepStaging) {
        Write-Step "Membersihkan staging publish"
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
        Write-Ok "Staging dibersihkan"
    }
    else {
        Write-Info "Staging publish dipertahankan karena -KeepStaging dipakai: $publishDir"
    }

    Write-Host ""
    Write-Host "SELESAI" -ForegroundColor Green
    Write-Host "Installer siap:" -ForegroundColor White
    Write-Host "  $($installer.FullName)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Catatan:" -ForegroundColor White
    Write-Host "  - Tidak ada paket portable atau portable.zip yang dibuat."
    Write-Host "  - Jalankan installer .exe ini di komputer target."
}
catch {
    Write-Host ""
    Write-Host "BUILD GAGAL" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Periksa pesan di atas, lalu jalankan ulang script setelah masalahnya diperbaiki." -ForegroundColor Yellow
    exit 1
}
