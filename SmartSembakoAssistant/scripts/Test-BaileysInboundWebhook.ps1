param(
    [ValidateSet("live", "empty-upsert", "append", "old-history", "unauthorized", "duplicate")]
    [string]$Scenario = "live",
    [string]$SenderId = "628123456789",
    [int]$Port = 8090,
    [string]$Text = "/start",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sourceSidecar = Join-Path $repoRoot "Integrations\BaileysSidecar\index.js"
$debugSidecar = Join-Path $repoRoot "bin\Debug\net8.0-windows\Integrations\BaileysSidecar\index.js"

function Get-BuildTag([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return "-"
    }

    $match = Select-String -LiteralPath $Path -Pattern 'sidecarBuildTag\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($match -and $match.Matches.Count -gt 0) {
        return $match.Matches[0].Groups[1].Value
    }

    return "missing"
}

$sourceTag = Get-BuildTag $sourceSidecar
$debugTag = Get-BuildTag $debugSidecar
Write-Host "Source sidecar build tag: $sourceTag"
Write-Host "Debug sidecar build tag : $debugTag"

$now = Get-Date
$timestamp = $now.ToUniversalTime()
$sidecarStartedAt = $now.AddSeconds(-10).ToUniversalTime()
$upsertType = "notify"
$messageId = "SIM-$Scenario-$([Guid]::NewGuid().ToString('N'))"
$scenarioText = $Text

switch ($Scenario) {
    "empty-upsert" {
        $upsertType = ""
    }
    "append" {
        $upsertType = "append"
    }
    "old-history" {
        $timestamp = $now.AddMinutes(-20).ToUniversalTime()
        $sidecarStartedAt = $now.ToUniversalTime()
    }
    "unauthorized" {
        $SenderId = "6280000000000"
        $scenarioText = "halo dari nomor tidak terdaftar"
    }
    "duplicate" {
        $messageId = "SIM-DUPLICATE-FIXED-ID"
    }
}

$payload = [ordered]@{
    senderId = $SenderId
    senderName = "Simulator"
    text = $scenarioText
    caption = $scenarioText
    mediaUrl = $null
    mediaMimeType = $null
    fileName = $null
    messageId = $messageId
    rawSenderJid = "$SenderId@s.whatsapp.net"
    resolvedSenderJid = "$SenderId@s.whatsapp.net"
    appInstanceId = "simulator"
    machineName = $env:COMPUTERNAME
    sidecarBuildTag = $sourceTag
    upsertType = $upsertType
    originalUpsertType = $upsertType
    sidecarStartedAt = $sidecarStartedAt.ToString("o")
    receivedAt = $now.ToUniversalTime().ToString("o")
    messageTimestampMs = [int64](([DateTimeOffset]$timestamp).ToUnixTimeMilliseconds())
    remoteJid = "$SenderId@s.whatsapp.net"
    fromMe = $false
    timestamp = $timestamp.ToString("o")
}

$url = "http://localhost:$Port/baileys/events/inbound"
Write-Host "POST $url"
Write-Host "Scenario: $Scenario; sender=$SenderId; upsert='$upsertType'; messageId=$messageId"

$json = $payload | ConvertTo-Json -Depth 8
if ($DryRun) {
    Write-Host "Dry run payload:"
    Write-Host $json
    return
}

$response = Invoke-WebRequest -Uri $url -Method Post -Body $json -ContentType "application/json"
Write-Host "HTTP $($response.StatusCode) $($response.StatusDescription)"

if ($Scenario -eq "duplicate") {
    Write-Host "Kirim ulang command yang sama untuk memastikan duplicate message id diabaikan:"
    Write-Host "  .\scripts\Test-BaileysInboundWebhook.ps1 -Scenario duplicate -SenderId $SenderId -Port $Port"
}
