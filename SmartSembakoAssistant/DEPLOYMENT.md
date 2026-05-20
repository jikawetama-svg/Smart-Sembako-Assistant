# Deployment Smart Sembako Assistant

## Output yang dibuat
- `artifacts/installer`
  Berisi installer `.exe` final. Paket portable dan portable `.zip` tidak dibuat lagi.

Catatan: folder publish hanya dipakai sebagai staging sementara saat build. Secara default folder ini dibersihkan setelah installer berhasil dibuat.

## Cara build
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 1.0.0
```

Jika Inno Setup tidak terdeteksi otomatis:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 1.0.0 -InnoCompilerPath "C:\Path\To\ISCC.exe"
```

## Karakter installer
- Wizard `modern` untuk tampilan yang lebih dekat ke gaya Windows 11.
- Ada halaman lisensi/perjanjian otomatis dari `deployment/LICENSE.txt`.
- Ada catatan instalasi setelah wizard dari `deployment/INSTALL_NOTES.txt`.
- Installer bisa dipasang ke drive selain `C:` lewat halaman pemilihan folder.
- Jika aplikasi sedang berjalan, setup memakai `AppMutex` dan `CloseApplications=yes` untuk meminta aplikasi ditutup sebelum update.

## Catatan runtime
- Mode install menyimpan data ke `%LocalAppData%\Smart Sembako Assistant`.
- Runtime WhatsApp lokal Baileys dibundel ke `runtimes\node\node.exe`.
- Sidecar Baileys dibundel bersama `Integrations\BaileysSidecar\node_modules`.
- `cloudflared.exe` dibundel ke `runtimes\cloudflared\cloudflared.exe`.
- Perangkat target tidak perlu install Node.js atau download cloudflared manual selama installer dibuat lewat `scripts\Build-Release.ps1`.
