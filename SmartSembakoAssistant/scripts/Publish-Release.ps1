param(
    [string]$Version = "",
    [switch]$DryRun,
    [switch]$Draft = $true,
    [switch]$NoPush,
    [string]$InnoCompilerPath = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$solutionRoot = Split-Path -Parent $projectRoot
$changelogPath = Join-Path $projectRoot "changelog.json"
$syncScript = Join-Path $scriptRoot "Sync-Version.ps1"
$buildScript = Join-Path $scriptRoot "Build-Release.ps1"
$artifactsRoot = Join-Path $projectRoot "artifacts"
$releaseGuidePath = Join-Path $artifactsRoot "RELEASE_GUIDE.md"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-LatestChangelogEntry {
    $changelog = Get-Content -LiteralPath $changelogPath -Raw -Encoding UTF8 | ConvertFrom-Json
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

function New-ReleaseNotes {
    param($Entry)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# $($Entry.title)") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("Version: v$($Entry.version)") | Out-Null
    $lines.Add("Date: $($Entry.date)") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## Summary") | Out-Null
    foreach ($item in @($Entry.summary)) {
        $lines.Add("- $item") | Out-Null
    }

    if ($Entry.details) {
        $lines.Add("") | Out-Null
        $lines.Add("## Details") | Out-Null
        foreach ($item in @($Entry.details)) {
            $lines.Add("- $item") | Out-Null
        }
    }

    return $lines -join [Environment]::NewLine
}

function Invoke-Git {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$GitArgs
    )

    if ($GitArgs.Count -eq 0) {
        throw "Invoke-Git dipanggil tanpa argumen."
    }

    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') gagal."
    }
}

if (!(Test-Path -LiteralPath $changelogPath)) {
    throw "changelog.json tidak ditemukan: $changelogPath"
}

$entry = Get-LatestChangelogEntry
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $entry.version
}

$cleanVersion = Normalize-Version -Value $Version
$tagName = "v$cleanVersion"
$releaseTitle = "Smart Sembako Assistant $tagName - $($entry.title)"
$releaseNotes = New-ReleaseNotes -Entry $entry

Set-Location $solutionRoot

Write-Step "Release plan"
Write-Host "Version : $cleanVersion"
Write-Host "Tag     : $tagName"
Write-Host "Title   : $releaseTitle"
if ($DryRun) {
    Write-Host "Mode    : DRY RUN" -ForegroundColor Yellow
}

if ($DryRun) {
    Write-Step "Release notes preview"
    Write-Host $releaseNotes
    exit 0
}

Write-Step "Sync version"
& powershell -NoProfile -ExecutionPolicy Bypass -File $syncScript -Version $cleanVersion
if ($LASTEXITCODE -ne 0) {
    throw "Sync-Version.ps1 gagal."
}

Write-Step "Build installer"
$buildArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $buildScript, "-Version", $cleanVersion)
if (![string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $buildArgs += @("-InnoCompilerPath", $InnoCompilerPath)
}
& powershell @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Build-Release.ps1 gagal."
}

$installer = Get-ChildItem -LiteralPath (Join-Path $projectRoot "artifacts\installer") -Filter "*.exe" -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (!$installer) {
    throw "Installer .exe tidak ditemukan."
}

Write-Step "Commit release metadata if needed"
$status = @(git status --porcelain)
if ($status.Count -gt 0) {
    Invoke-Git "add" "--all"
    Invoke-Git "commit" "-m" "release($tagName): $($entry.title)"
}
else {
    Write-Host "Tidak ada perubahan untuk commit." -ForegroundColor DarkGray
}

Write-Step "Create git tag"
$existingTag = git tag --list $tagName
if ($existingTag) {
    throw "Tag sudah ada: $tagName"
}
Invoke-Git "tag" "-a" $tagName "-m" $releaseTitle

if (!$NoPush) {
    Write-Step "Push branch and tag"
    Invoke-Git "push" "origin" "main"
    Invoke-Git "push" "origin" $tagName
}

Write-Step "Publish GitHub release"
$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    $notesFile = Join-Path $env:TEMP "ssa-release-$cleanVersion.md"
    Set-Content -LiteralPath $notesFile -Value $releaseNotes -Encoding UTF8

    $ghArgs = @("release", "create", $tagName, $installer.FullName, "--title", $releaseTitle, "--notes-file", $notesFile)
    if ($Draft) {
        $ghArgs += "--draft"
    }

    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) {
        throw "gh release create gagal."
    }
}
else {
    New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
    $guide = @"
# Manual GitHub Release Guide

GitHub CLI tidak ditemukan, jadi release perlu dibuat manual.

Tag: $tagName
Title: $releaseTitle
Installer: $($installer.FullName)

## Release Notes

$releaseNotes
"@
    Set-Content -LiteralPath $releaseGuidePath -Value $guide -Encoding UTF8
    Write-Host "GitHub CLI tidak ditemukan. Guide dibuat: $releaseGuidePath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Release automation selesai: $tagName" -ForegroundColor Green
