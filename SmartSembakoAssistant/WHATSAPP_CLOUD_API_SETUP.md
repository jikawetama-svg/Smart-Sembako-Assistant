# WhatsApp Cloud API Setup

Tanggal: 2026-05-18

## Mode Lokal Dulu

Aplikasi menjalankan webhook lokal di:

```text
http://localhost:8090/whatsapp/webhook
```

Port mengikuti nilai `WhatsApp.LocalWebhookPort`.

Tombol `Test Webhook` di Settings tetap sukses untuk mode lokal walaupun `Public Base URL` kosong. Ini cocok untuk development di PC/laptop. Meta tetap membutuhkan URL HTTPS publik jika webhook ingin menerima event langsung dari Meta.

## Jika Mau Online

Isi salah satu:

```text
WhatsApp.PublicWebhookUrl
Tunnel.PublicUrl
```

Nilainya boleh base URL:

```text
https://domain-atau-tunnel.example.com
```

atau URL callback lengkap:

```text
https://domain-atau-tunnel.example.com/whatsapp/webhook
```

Aplikasi akan memakai endpoint final:

```text
https://domain-atau-tunnel.example.com/whatsapp/webhook
```

Untuk production, isi `WhatsApp.AppSecret` agar signature webhook dari Meta divalidasi. Tanpa App Secret, webhook tetap jalan untuk lokal/test tetapi belum production-ready.

## Test Number Meta

Saat masih memakai test number dari Meta, nomor tujuan harus masuk allowed list di dashboard Meta:

```text
WhatsApp > API Setup > To > Add recipient phone number
```

Nomor `Owner/Admin`, `Kasir`, atau nomor untuk tombol test di aplikasi harus memakai format internasional tanpa `+`, misalnya:

```text
6285864106457
```

Jangan isi nomor bot/test number Meta sebagai nomor owner. Nomor bot adalah `From` / `Phone Number ID`; nomor owner adalah nomor WhatsApp pribadi yang menerima pesan.

Contoh:

```text
Nomor test Meta / From: +1 (555) 151-2088
Phone Number ID: 1060642770465141
WhatsApp Business Account ID: 1877673336245264
Owner/Admin: 6285864106457
```

Kalau owner mengirim `/start` dari `6285864106457` ke nomor test Meta, log `Pesan WhatsApp text terkirim ke 6285864106457` adalah benar. Itu berarti aplikasi membalas pengirim, bukan terbalik.

Jika Meta menolak dengan `(#131030) Recipient phone number not in allowed list`, tambahkan nomor tujuan ke allowed list Meta atau ganti nomor owner/kasir di Settings.

Perintah `curl` dari dokumentasi Meta harus dijalankan di terminal/PowerShell, bukan dikirim sebagai pesan WhatsApp. Jika access token pernah terlihat di chat atau screenshot, buat token baru di Meta dan ganti di Settings aplikasi.

## Cloudflare Tunnel

Target tunnel tetap port lokal aplikasi:

```text
http://localhost:8090
```

Untuk menghindari error `Bad Request - Invalid Hostname`, jalankan `cloudflared` dengan host header lokal:

```text
cloudflared tunnel --url http://localhost:8090 --http-host-header localhost:8090
```

Di Settings aplikasi, `Args Template` yang disarankan:

```text
tunnel --url http://localhost:{port} --http-host-header localhost:{port}
```

Aplikasi juga mencoba listener wildcard agar hostname dari tunnel diterima. Jika Windows menolak wildcard prefix, aplikasi fallback ke `localhost`; dalam kondisi itu `--http-host-header localhost:{port}` wajib dipakai.

## Template Message WhatsApp

`WhatsApp.EnableTemplateMessages` default `false`.

Alasannya: pesan WhatsApp Cloud yang proaktif di luar 24-hour customer service window harus memakai template Meta yang sudah approved. Kalau template dimatikan, aplikasi tidak mengirim alert WhatsApp Cloud otomatis supaya tidak memicu biaya atau penolakan API karena aplikasi mengirim pesan duluan.

Jika template sudah approved, aktifkan di Settings:

```text
Enable WhatsApp Cloud template messages
```

Lalu isi mapping:

```text
StockAlert=ssa_low_stock_alert|id|1
Schedule=ssa_daily_summary|id|1
ReceivableAlert=ssa_receivable_alert|id|1
ExpiryAlert=ssa_expiry_alert|id|1
AnomalyAlert=ssa_anomaly_alert|id|1
Test=ssa_test_message|id|1
```

Format:

```text
Key=template_name|language_code|jumlah_parameter_body
```

Gunakan tombol `Test Template` untuk mencoba template `Test`.
