<#
.SYNOPSIS
    Script otomatisasi untuk mendeploy folder bot_runtime ke Hugging Face Spaces.
.PARAMETER SpaceRepoUrl
    URL repository Space HuggingFace (contoh: https://huggingface.co/spaces/USERNAME/smart-sembako-bot)
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$SpaceRepoUrl
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$BotRuntimeDir = Join-Path $ProjectDir "bot_runtime"
$TempDeployDir = Join-Path $ProjectDir "temp_hf_space_deploy"

Write-Host "🚀 Memulai Deployment Smart Sembako Cloud Bot ke Hugging Face Space..." -ForegroundColor Green

if (Test-Path $TempDeployDir) {
    Remove-Item $TempDeployDir -Recurse -Force
}

Write-Host "📦 Cloning target Space repo: $SpaceRepoUrl" -ForegroundColor Cyan
git clone $SpaceRepoUrl $TempDeployDir

Write-Host "📋 Menyalin komponen bot_runtime ke repository Space..." -ForegroundColor Cyan
Get-ChildItem -Path $BotRuntimeDir | Copy-Item -Destination $TempDeployDir -Recurse -Force

Set-Location $TempDeployDir

Write-Host "📌 Menambahkan file ke Git commit..." -ForegroundColor Cyan
git add .
git commit -m 'Deploy Smart Sembako Cloud Bot'
git push

Write-Host "✅ Deployment berhasil dikirim ke Hugging Face Space!" -ForegroundColor Green
Write-Host "🔗 Buka tab Settings -> Secrets pada Space Anda untuk mengisikan SUPABASE_URL, TELEGRAM_BOT_TOKEN, dll." -ForegroundColor Yellow

Set-Location $ProjectDir
Remove-Item $TempDeployDir -Recurse -Force
