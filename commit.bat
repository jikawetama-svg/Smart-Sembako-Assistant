@echo off
:: ============================================================
::  Smart Sembako Assistant — Commit Launcher
::  Klik ganda file ini untuk auto commit + push ke GitHub
::  Atau drag & drop ke terminal untuk opsi lanjut
:: ============================================================
title Smart Sembako Assistant — Auto Commit

:: Pindah ke direktori skrip ini berada
cd /d "%~dp0"

echo.
echo  =====================================================
echo   Smart Sembako Assistant - Auto Commit to GitHub
echo  =====================================================
echo.

:: Cek argumen: jika ada parameter, teruskan ke PowerShell
if "%~1"=="" (
    :: Mode default: commit otomatis + push
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0auto-commit.ps1"
) else if /i "%~1"=="-dryrun" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0auto-commit.ps1" -DryRun
) else if /i "%~1"=="-nopush" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0auto-commit.ps1" -NoPush
) else if /i "%~1"=="-pushonly" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0auto-commit.ps1" -PushOnly
) else (
    :: Anggap argumen pertama sebagai pesan commit custom
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0auto-commit.ps1" -Message "%~1"
)

echo.
if %ERRORLEVEL% NEQ 0 (
    echo  [GAGAL] Auto commit mengalami error. Kode: %ERRORLEVEL%
    echo  Lihat pesan error di atas untuk detail.
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo  Tekan sembarang tombol untuk menutup...
pause > nul
