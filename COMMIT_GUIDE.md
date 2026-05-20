# 🚀 Auto Commit Guide — Smart Sembako Assistant

> **SA TECH.Inc** | Repo: [Smart-Sembako-Assistant](https://github.com/jikawetama-svg/Smart-Sembako-Assistant)

---

## 📁 File yang Dibuat

| File | Keterangan |
|------|-----------|
| `auto-commit.ps1` | Skrip PowerShell utama — logika commit & push |
| `commit.bat` | Wrapper batch — klik ganda untuk langsung commit |
| `COMMIT_GUIDE.md` | Dokumen panduan ini |

---

## ⚡ Cara Cepat (Klik Ganda)

1. Buka **Windows Explorer** → arahkan ke folder proyek
2. **Klik ganda** `commit.bat`
3. Selesai — semua perubahan otomatis di-commit + push ke GitHub

---

## 🖥️ Cara Pakai via Terminal (PowerShell)

### Commit otomatis (pesan dibuat sendiri)
```powershell
.\auto-commit.ps1
```

### Commit dengan pesan custom
```powershell
.\auto-commit.ps1 -Message "fix: perbaikan pairing Baileys v7"
```

### Preview saja tanpa push (Dry Run)
```powershell
.\auto-commit.ps1 -DryRun
```

### Commit tapi jangan push dulu
```powershell
.\auto-commit.ps1 -NoPush
```

### Push saja (tidak ada perubahan baru)
```powershell
.\auto-commit.ps1 -PushOnly
```

---

## 🖱️ Cara Pakai via commit.bat

```
commit.bat                          → commit + push otomatis
commit.bat -dryrun                  → preview saja
commit.bat -nopush                  → commit tapi tidak push
commit.bat -pushonly                → push saja
commit.bat "fix: pairing fix v5"    → commit pesan custom
```

---

## 🧠 Logika Pesan Commit Otomatis

Skrip membaca jenis file yang berubah dan membuat prefix otomatis:

| Kondisi | Prefix |
|---------|--------|
| Ada file `.cs` atau `.js` | `feat` |
| Hanya file `.md` berubah | `docs` |
| `changelog.json` berubah | `release` |
| Lainnya | `chore` |

**Contoh pesan yang dibuat otomatis:**
```
feat(v5.0.1): 3 C# file, 1 JS file, 2 MD doc [2026-05-21]

Docs diperbarui:
  - DEPLOYMENT.md
  - COMMIT_GUIDE.md

Source diperbarui:
  - BaileysSidecarService.cs
  - BaileysPairingWindow.xaml.cs
  - RuntimePaths.cs

Sidecar/JS diperbarui:
  - index.js
```

---

## 📋 File yang Diabaikan (tidak di-commit)

Sesuai `.gitignore`, file berikut **tidak akan ikut commit**:

- `config.json` — konfigurasi sensitif (API key, token)
- `data/memory.db` — database lokal
- `data/logs/*` — log file
- `artifacts/` — build output
- `bin/`, `obj/` — hasil kompilasi
- `*.log` — log files

---

## ⚠️ Troubleshooting

### Error: "tidak dapat dimuat karena menjalankan skrip dinonaktifkan"
```powershell
# Jalankan PowerShell sebagai Administrator, lalu:
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
```
Atau gunakan `commit.bat` yang sudah menangani ini otomatis (`-ExecutionPolicy Bypass`).

### Error: "Push gagal / rejected"
Ada perubahan di remote yang belum di-pull:
```powershell
git pull origin main --rebase
.\auto-commit.ps1 -PushOnly
```

### Error: "Please tell me who you are" (git identity)
```powershell
git config --global user.email "email@kamu.com"
git config --global user.name "Nama Kamu"
```

---

## 🔄 Alur Kerja Disarankan

```
Selesai coding / update MD
        ↓
Klik ganda commit.bat
        ↓
Skrip otomatis:
  1. Baca versi dari changelog.json
  2. Deteksi semua file berubah
  3. Klasifikasi (.cs / .md / .json / dll)
  4. Buat pesan commit cerdas
  5. git add --all
  6. git commit -m "..."
  7. git push origin main
        ↓
Tampilkan link commit di GitHub ✅
```

---

*Dibuat oleh SA TECH.Inc — Smart Sembako Assistant v5.x*
