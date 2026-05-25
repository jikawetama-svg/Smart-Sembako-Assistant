param(
    [string]$Version = "",
    [string]$ChangelogPath = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$solutionRoot = Split-Path -Parent $projectRoot

if ([string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $ChangelogPath = Join-Path $projectRoot "changelog.json"
}

$projectFile = Join-Path $projectRoot "SmartSembakoAssistant.csproj"
$installerScript = Join-Path $projectRoot "deployment\SmartSembakoAssistant.iss"
$licenseFile = Join-Path $projectRoot "deployment\LICENSE.txt"

function Get-LatestChangelogEntry {
    param([string]$Path)

    if (!(Test-Path -LiteralPath $Path)) {
        throw "changelog.json tidak ditemukan: $Path"
    }

    $changelog = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $entry = $changelog.entries |
        Where-Object { $_.version -and $_.version -ne "unreleased" -and $_.status -in @("draft", "implemented", "released") } |
        Select-Object -First 1

    if (!$entry) {
        throw "Tidak ada entry changelog aktif dengan version valid."
    }

    return $entry
}

function Normalize-Version {
    param([string]$Value)

    $source = if ($null -eq $Value) { "" } else { $Value }
    $clean = $source.Trim().TrimStart("v", "V")
    if ($clean -notmatch "^\d+\.\d+\.\d+$") {
        throw "Format versi tidak valid: '$Value'. Gunakan format x.y.z."
    }

    return $clean
}

function Set-XmlVersionTag {
    param(
        [string]$Content,
        [string]$TagName,
        [string]$Value
    )

    $pattern = "<$TagName>.*?</$TagName>"
    $replacement = "<$TagName>$Value</$TagName>"
    if ($Content -match $pattern) {
        return [regex]::Replace($Content, $pattern, $replacement, 1)
    }

    return $Content -replace "(<UseWPF>true</UseWPF>)", "`$1`r`n    $replacement"
}

function Write-FileIfChanged {
    param(
        [string]$Path,
        [string]$Content
    )

    $current = if (Test-Path -LiteralPath $Path) {
        Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    } else {
        ""
    }

    if ($current -ne $Content) {
        Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8 -NoNewline
        Write-Host "Updated: $Path" -ForegroundColor Green
    }
    else {
        Write-Host "Already current: $Path" -ForegroundColor DarkGray
    }
}

$entry = Get-LatestChangelogEntry -Path $ChangelogPath
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $entry.version
}

$cleanVersion = Normalize-Version -Value $Version
$assemblyVersion = "$cleanVersion.0"

if (!(Test-Path -LiteralPath $projectFile)) {
    throw "Project file tidak ditemukan: $projectFile"
}

$csproj = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8
$csproj = Set-XmlVersionTag -Content $csproj -TagName "Version" -Value $cleanVersion
$csproj = Set-XmlVersionTag -Content $csproj -TagName "AssemblyVersion" -Value $assemblyVersion
$csproj = Set-XmlVersionTag -Content $csproj -TagName "FileVersion" -Value $assemblyVersion
Write-FileIfChanged -Path $projectFile -Content $csproj

if (Test-Path -LiteralPath $installerScript) {
    $iss = Get-Content -LiteralPath $installerScript -Raw -Encoding UTF8
    $iss = [regex]::Replace($iss, '(?m)^#define\s+MyAppVersion\s+".*"$', "#define MyAppVersion `"$cleanVersion`"", 1)
    Write-FileIfChanged -Path $installerScript -Content $iss
}

if (Test-Path -LiteralPath $licenseFile) {
    $license = Get-Content -LiteralPath $licenseFile -Raw -Encoding UTF8
    $license = [regex]::Replace($license, '(?m)^Product\s*:\s*Smart Sembako Assistant\s+v[0-9.]+\s*$', 'Product    : Smart Sembako Assistant')
    $license = [regex]::Replace($license, '(?m)^Year\s*:\s*2026\s*$', 'Copyright (c) 2026 SA TECH.Inc')
    Write-FileIfChanged -Path $licenseFile -Content $license
}

Write-Host "Version synced to $cleanVersion from changelog entry: $($entry.title)" -ForegroundColor Cyan
