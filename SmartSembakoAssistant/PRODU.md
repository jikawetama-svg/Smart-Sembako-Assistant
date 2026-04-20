# 🎨 PRODU.md - Product UI Redesign Document

**Smart Sembako Assistant v4.0 - Complete UI Overhaul**

**Tanggal:** 10 April 2026  
**Status:** Planning Phase  
**Target:** Production Ready - Modern, Clean, User-Friendly

---

## 📋 DAFTAR ISI

1. [Ringkasan Eksekutif](#1-ringkasan-eksekutif)
2. [Analisis UI Saat Ini](#2-analisis-ui-saat-ini)
3. [Prinsip Desain Baru](#3-prinsip-desain-baru)
4. [Design System](#4-design-system)
5. [Arsitektur Navigasi](#5-arsitektur-navigasi)
6. [Detail Setiap Halaman](#6-detail-setiap-halaman)
7. [Komponen UI Reusable](#7-komponen-ui-reusable)
8. [Responsive & Accessibility](#8-responsive--accessibility)
9. [Animasi & Micro-interactions](#9-animasi--micro-interactions)
10. [Implementation Roadmap](#10-implementation-roadmap)
11. [Testing Checklist](#11-testing-checklist)

---

## 1. RINGKASAN EKSEKUTIF

### 🎯 Tujuan
Rombak **penuh** UI aplikasi Smart Sembako Assistant dari dasar dengan:
- ✅ **Modern Design** - Fluent Design Windows 11
- ✅ **User-Friendly** - Intuitive, mudah dipelajari
- ✅ **Responsive** - Support berbagai ukuran window
- ✅ **Accessible** - Mudah dibaca & digunakan
- ✅ **Performant** - Ringan, tidak lag

### 🚀 Fitur yang Tetap Ada (Tidak Berubah)
Semua fitur dari Phase 1-3 tetap ada:
- ✅ Bot Control (Start/Stop/Restart)
- ✅ AI Fallback (Groq + Gemini)
- ✅ Dashboard dengan auto-refresh
- ✅ Stock Monitoring dengan quick stats
- ✅ Activity Logs dengan filter & export
- ✅ Settings dengan test connections
- ✅ Quick Actions
- ✅ Drawer Menu

### 🎨 Apa yang Berubah
- **Layout** - Dari sidebar tradisional ke modern drawer
- **Typography** - Font yang lebih readable
- **Color Scheme** - Warna yang lebih profesional
- **Spacing** - White space yang lebih baik
- **Components** - Card-based design
- **Navigation** - Drawer menu yang smooth
- **Feedback** - Loading states, toasts, skeletons

---

## 2. ANALISIS UI SAAT INI

### ❌ Masalah yang Ditemukan

#### A. MainWindow
- Sidebar terlalu lebar (260px)
- Bot control terlalu kecil
- Quick actions tidak terlalu terlihat
- Tidak ada visual hierarchy yang jelas
- Top bar terlalu sederhana

#### B. DashboardView
- Status cards terlalu kecil
- Quick insights tidak cukup visual
- Tidak ada comparison metrics (vs kemarin)
- Recent conversations terlalu panjang
- Tidak ada refresh indicator

#### C. StockMonitoringView
- Quick stats terlalu sederhana
- Tidak ada visual indicator untuk status
- DataGrid terlalu padat
- Tidak ada loading state
- Filter tidak intuitive

#### D. LogsView
- Terlalu teknis untuk user awam
- Tidak ada severity colors
- Stats terlalu sederhana
- Tidak ada date picker
- Export button kurang visible

#### E. SettingsView
- Terlalu panjang (scroll jauh)
- Tidak ada section grouping
- Test results tidak jelas
- Tidak ada validation feedback
- Save button tidak sticky

---

## 3. PRINSIP DESAIN BARU

### 🎨 Design Philosophy

#### A. Less is More
- Minimalisir clutter
- Fokus pada informasi penting
- White space yang cukup

#### B. Visual Hierarchy
- Size: Penting = Besar
- Color: Status = Warna
- Position: Atas = Prioritas

#### C. Consistency
- Spacing: 8px grid system
- Colors: Design tokens
- Typography: 3 font sizes max
- Shadows: 3 levels max

#### D. Feedback
- Loading: Skeleton screens
- Success: Toast notifications
- Error: Clear error messages
- Empty: Helpful empty states

---

## 4. DESIGN SYSTEM

### 🎨 Color Palette

#### Primary Colors
```
Primary:        #2E7D32 (Green 800)
Primary Light:  #4CAF50 (Green 500)
Primary Dark:   #1B5E20 (Green 900)
```

#### Status Colors
```
Success:    #4CAF50 (Green)
Warning:    #FF9800 (Orange)
Error:      #F44336 (Red)
Info:       #2196F3 (Blue)
```

#### Neutral Colors
```
Background:     #F5F5F5 (Grey 100)
Surface:        #FFFFFF (White)
Border:         #E0E0E0 (Grey 300)
Text Primary:   #212121 (Grey 900)
Text Secondary: #757575 (Grey 600)
Text Disabled:  #BDBDBD (Grey 400)
```

### 📐 Spacing System (8px Grid)
```
xxs: 4px
xs:  8px
sm:  12px
md:  16px
lg:  24px
xl:  32px
xxl: 48px
```

### 🔤 Typography
```
Font Family: Segoe UI, system fonts

Sizes:
- Display:  32px (Page titles)
- Heading:  24px (Section titles)
- Title:    20px (Card titles)
- Body:     16px (Main text)
- Caption:  14px (Secondary text)
- Small:    12px (Labels, hints)
- Tiny:     10px (Badges, tags)
```

### 🌑 Shadows
```
Small:  0 1px 3px rgba(0,0,0,0.12)
Medium: 0 3px 6px rgba(0,0,0,0.15)
Large:  0 8px 16px rgba(0,0,0,0.2)
```

### 🔘 Border Radius
```
Small:  4px (Buttons, inputs)
Medium: 8px (Cards)
Large:  12px (Dialogs)
Full:   9999px (Badges, avatars)
```

---

## 5. ARSITEKTUR NAVIGASI

### 🏗️ Layout Structure

```
┌────────────────────────────────────────────────────────────┐
│  SMART SEMBAKO ASSISTANT v4.0                  [─][□][✕]  │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌────────────┐  ┌──────────────────────────────────┐    │
│  │            │  │                                  │    │
│  │            │  │  ┌──────────────────────────┐   │    │
│  │            │  │  │  Top Bar                 │   │    │
│  │  DRAWER    │  │  │  Title | Search | User   │   │    │
│  │            │  │  └──────────────────────────┘   │    │
│  │  • Home    │  │                                  │    │
│  │  • Stock   │  │  ┌──────────────────────────┐   │    │
│  │  • Logs    │  │  │                          │   │    │
│  │  • Settings│  │  │   Content Area           │   │    │
│  │            │  │  │                          │   │    │
│  │  ───────── │  │  │                          │   │    │
│  │            │  │  │                          │   │    │
│  │  BOT CTRL  │  │  └──────────────────────────┘   │    │
│  │  [⏹] [▶]  │  │                                  │    │
│  │            │  └──────────────────────────────────┘    │
│  └────────────┘                                          │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

### 📱 Drawer Menu

#### Structure
```
┌─────────────────────────┐
│ 🏪 Smart Sembako        │
│    Assistant v4.0       │
├─────────────────────────┤
│                         │
│ 📊 Dashboard            │ ← Active
│ 📦 Stock Monitoring     │
│ 📜 Activity Logs        │
│ ⚙️ Settings             │
│                         │
├─────────────────────────┤
│ 🎮 Bot Control          │
│ ┌─────────────────────┐│
│ │ 🟢 Bot Aktif        ││
│ │ Uptime: 2h 15m     ││
│ │                     ││
│ │ [▶] [⏹] [🔄]       ││
│ └─────────────────────┘│
│                         │
├─────────────────────────┤
│ ⚡ Quick Actions        │
│ 💰 Omzet Hari Ini       │
│ ⚠️ Cek Stok Minus       │
│ 📦 Rekomendasi Restock  │
│ 🧪 Test Connections     │
│                         │
└─────────────────────────┘
```

#### Drawer Behavior
- **Width:** 280px (expanded), 48px (collapsed)
- **Toggle:** Click ☰ icon to expand/collapse
- **Overlay:** On narrow windows (<1200px)
- **Animation:** Smooth slide-in/out (200ms ease-out)
- **Persistence:** Remember state across sessions

---

## 6. DETAIL SETIAP HALAMAN

### 6.1 🏠 DASHBOARD PAGE

#### Layout
```
┌────────────────────────────────────────────────────────────┐
│  Dashboard                                      [🔄 Auto] │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐    │
│  │ 🤖 Bot   │ │ 🧠 AI    │ │ 💾 DB    │ │ 📊 Sales │    │
│  │ 🟢 On    │ │ 🟢 Groq  │ │ ✅ OK    │ │ 💰 1.2M  │    │
│  │ 2h 15m   │ │ LLaMA70B │ │ pos.db   │ │ ↑ 15%    │    │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘    │
│                                                            │
│  ┌─────────────────────┐ ┌─────────────────────┐        │
│  │ 💰 Revenue          │ │ 📈 Profit           │        │
│  │ Rp 1.250.000        │ │ Rp 125.000          │        │
│  │ ↑ 15% vs kemarin    │ │ Margin: 10%         │        │
│  │                     │ │ ↓ 2% vs kemarin     │        │
│  └─────────────────────┘ └─────────────────────┘        │
│                                                            │
│  ┌─────────────────────┐ ┌─────────────────────┐        │
│  │ 🧾 Transaksi        │ │ ⚠️ Stok Bermasalah  │        │
│  │ 25 nota             │ │ 🔴 Minus: 3 produk  │        │
│  │ Avg: Rp 50k/trans   │ │ 🟡 Rendah: 12       │        │
│  │                     │ │ 🔴 Habis: 5         │        │
│  └─────────────────────┘ └─────────────────────┘        │
│                                                            │
│  ┌──────────────────────────────────────────────┐        │
│  │ 📊 Penjualan 7 Hari Terakhir                 │        │
│  │ [Line Chart Placeholder]                     │        │
│  │ Mon: 800k | Tue: 950k | Wed: 1.1M | ...     │        │
│  └──────────────────────────────────────────────┘        │
│                                                            │
│  [Lihat Detail]  [Export]  [Refresh]                      │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

#### Components
1. **Status Cards (4 kolom)**
   - Bot: Status + Uptime
   - AI: Provider + Model
   - DB: Status + Path
   - Sales: Revenue hari ini + trend

2. **Metric Cards (2x2 grid)**
   - Revenue + vs kemarin
   - Profit + margin
   - Transaksi + average
   - Stok Bermasalah + breakdown

3. **Chart Section**
   - Penjualan 7 hari (line chart)
   - Bisa expand untuk detail

4. **Action Bar**
   - Lihat Detail button
   - Export button
   - Refresh button dengan auto-toggle

#### Auto-Refresh
- **Default:** Every 30 seconds
- **Indicator:** 🔄 Auto di top bar
- **Toggle:** Click untuk pause/resume
- **Loading:** Skeleton screen saat refresh

---

### 6.2 📦 STOCK MONITORING PAGE

#### Layout
```
┌────────────────────────────────────────────────────────────┐
│  Stock Monitoring                                          │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌─ Quick Stats ──────────────────────────────────────┐  │
│  │ 🟢 Aman: 150  2 Rendah: 12  🔴 Habis: 5  ⚠️ Minus: 3 │
│  └─────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ 🔍 Search: [_____________________] [🔍]            │  │
│  │                                                     │  │
│  │ Filter: [Semua ▼]  Sort: [Nama A-Z ▼]              │  │
│  │         [🟢 Aman] [🟡 Rendah] [🔴 Habis] [⚠️ Minus] │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ Produk          │ Stok │ Status │ Harga    │ Aksi │  │
│  ├────────────────────────────────────────────────────┤  │
│  │ Gula Pasir     │ 150  │ 🟢     │ 12.000   │ [📝] │  │
│  │ Minyak Goreng  │ 3    │ 🟡     │ 14.000   │ [📝] │  │
│  │ Kopi Kapal Api │ -2   │ 🔴     │ 8.000    │ [📝] │  │
│  │ ...                                               │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
│  Showing 1-20 of 1,234 products  [◄] [►]                  │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

#### Features
1. **Quick Stats Bar**
   - Color-coded badges
   - Clickable untuk filter langsung
   - Auto-update saat data berubah

2. **Search & Filter**
   - Real-time search (debounced 300ms)
   - Filter by status (buttons)
   - Sort by column (click header)
   - Clear all button

3. **DataGrid**
   - Virtualized untuk performance
   - Row hover effect
   - Clickable rows untuk detail
   - Action buttons per row

4. **Pagination**
   - 20 items per page
   - Page navigation
   - Jump to page input

---

### 6.3 📜 ACTIVITY LOGS PAGE

#### Layout
```
┌────────────────────────────────────────────────────────────┐
│  Activity Logs                                             │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌─ Filters ───────────────────────────────────────────┐  │
│  │ Date: [📅 10/04/2026] to [📅 10/04/2026]            │  │
│  │ Level: [All ▼]  Category: [All ▼]  User: [All ▼]    │  │
│  │                                                      │  │
│  │ [🔍 Apply Filters]  [❌ Clear]  [📥 Export CSV]     │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌─ Summary ───────────────────────────────────────────┐  │
│  │ Total: 1,234  ✅ Info: 1,100  ⚠️ Warning: 122  ❌ Error: 12 │
│  └──────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ Waktu            │ Level │ Kategori  │ Pesan       │  │
│  ├────────────────────────────────────────────────────┤  │
│  │ 10/04 06:35:12 │ ✅    │ INVENTORY │ 2B PENCIL 0 │  │
│  │ 10/04 06:20:05 │ ✅    │ RESTOCK   │ Gula 50     │  │
│  │ 10/04 06:15:00 │ ⚠️    │ ALERT     │ Stok minus  │  │
│  │ 10/04 06:10:00 │ ❌    │ AI        │ Fallback    │  │
│  │ ...                                               │  │
│  └────────────────────────────────────────────────────┘  │
│                                                            │
│  Showing 1-50 of 1,234 logs  [◄] [►]  [🗑️ Clear Old]      │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

#### Features
1. **Advanced Filters**
   - Date range picker
   - Level filter (Info, Warning, Error)
   - Category filter (Command, AI, System, dll)
   - User filter (Owner, Kasir, System)

2. **Summary Bar**
   - Total logs
   - Breakdown by level dengan colors
   - Clickable untuk filter cepat

3. **Log Table**
   - Color-coded levels
   - Truncated messages (hover for full)
   - Sortable columns
   - Virtualized untuk performance

4. **Actions**
   - Export to CSV
   - Clear old logs (>30 days)
   - Auto-refresh toggle

---

### 6.4 ⚙️ SETTINGS PAGE

#### Layout
```
┌────────────────────────────────────────────────────────────┐
│  Settings                                                  │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌─ AI Settings ───────────────────────────────────────┐  │
│  │                                                      │  │
│  │  🧠 AI PRIMARY (Groq)                               │  │
│  │  ┌────────────────────────────────────────────────┐ │  │
│  │  │ API Key: [••••••••••] [👁️ Show] [🧪 Test]    │ │  │
│  │  │ Model: [llama-3.1-70b-versatile ▼]            │ │  │
│  │  │ Temperature: [0.7 ◄────●────► 1.0]            │ │  │
│  │  │ Max Tokens: [1000]                             │ │  │
│  │  └────────────────────────────────────────────────┘ │  │
│  │                                                      │  │
│  │  🔄 AI FALLBACK (Gemini)                            │  │
│  │  ┌────────────────────────────────────────────────┐ │  │
│  │  │ ☑ Enable Fallback                              │ │  │
│  │  │ API Key: [••••••••••] [👁️ Show] [🧪 Test]    │ │  │
│  │  │ Model: [gemini-2.0-flash ▼]                   │ │  │
│  │  └────────────────────────────────────────────────┘ │  │
│  │                                                      │  │
│  │  ⚙️ AI BEHAVIOR                                     │  │
│  │  ┌────────────────────────────────────────────────┐ │  │
│  │  │ ☑ Auto-switch ke Fallback saat error           │ │  │
│  │  │ ☑ Cache AI responses                           │ │  │
│  │  │ Retry Count: [2]  Auto Recovery: [5 min]       │ │  │
│  │  └────────────────────────────────────────────────┘ │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌─ Telegram Settings ─────────────────────────────────┐  │
│  │ Bot Token: [••••••••••] [👁️ Show] [🧪 Test]       │  │
│  │ Owner Chat IDs: [12345, 67890]                      │  │
│  │ Kasir Chat IDs: [11111]                             │  │
│  │ Mode: [SAFE ▼]                                      │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                            │
│  ┌─ Database Settings ─────────────────────────────────┐  │
│  │ pos.db Path: [_______________] [Browse]             │  │
│  │ ☑ Auto-detect                                       │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                            │
│  [💾 Save Settings]  [🔄 Reset to Default]                 │
│  [🧪 Test All Connections]                                 │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

#### Features
1. **Section Cards**
   - Grouped by category
   - Collapsible sections
   - Visual separators

2. **Input Controls**
   - Password fields dengan show/hide
   - Test buttons per section
   - Validation feedback inline
   - Helper text di bawah inputs

3. **Save Behavior**
   - Sticky save button di bottom
   - Unsaved changes indicator
   - Auto-save draft (optional)
   - Confirmation on navigation away

4. **Test Connections**
   - Individual test buttons
   - "Test All" button di bottom
   - Results modal dengan detail
   - Retry mechanism

---

## 7. KOMPONEN UI REUSABLE

### 📦 Component Library

#### A. Cards
```
StatusCard:    Untuk status indicators (Bot, AI, DB)
MetricCard:    Untuk metrics (Revenue, Profit, dll)
ActionCard:    Untuk action groups
InfoCard:      Untuk informational content
```

#### B. Buttons
```
Primary:   Main actions (Save, Start)
Secondary: Secondary actions (Cancel, Stop)
Tertiary:  Text buttons (Learn more)
Icon:      Icon-only buttons (Refresh, Delete)
Floating:  FAB untuk quick actions
```

#### C. Inputs
```
TextInput:     Standard text input
PasswordInput: Password dengan toggle
SelectInput:   Dropdown selects
DateInput:     Date picker
ToggleInput:   On/off switches
```

#### D. Feedback
```
Toast:         Success/error notifications
Snackbar:      Temporary messages
Modal:         Dialogs untuk confirmation
Skeleton:      Loading placeholders
EmptyState:    Empty content placeholders
```

---

## 8. RESPONSIVE & ACCESSIBILITY

### 📱 Responsive Breakpoints
```
Mobile:     < 768px
Tablet:     768px - 1024px
Desktop:    1024px - 1440px
Large:      > 1440px
```

### ♿ Accessibility
- **Keyboard Navigation:** Semua fitur accessible via keyboard
- **Screen Reader:** Proper ARIA labels
- **Contrast:** Minimum 4.5:1 ratio
- **Focus Indicators:** Clear focus rings
- **Font Scaling:** Support up to 200%

---

## 9. ANIMASI & MICRO-INTERACTIONS

### 🎬 Animations
```
Drawer Toggle:    200ms ease-out
Page Transition:  150ms fade
Button Hover:     100ms scale
Loading Spinners: 1s linear rotate
Toast In/Out:     200ms slide
Skeleton Pulse:   1.5s ease-in-out infinite
```

### 💫 Micro-interactions
- Button click: Scale down 5%
- Card hover: Lift up 2px
- Input focus: Border color change
- Status change: Color transition
- Success: Checkmark animation

---

## 10. IMPLEMENTATION ROADMAP

### Phase 4.0 - UI Overhaul (3-4 minggu)

#### Week 1: Design System & Foundation
- [ ] Setup design tokens (colors, spacing, typography)
- [ ] Create component library (Cards, Buttons, Inputs)
- [ ] Implement responsive layout system
- [ ] Setup animation framework

#### Week 2: Main Window & Navigation
- [ ] Redesigned MainWindow dengan drawer menu
- [ ] Drawer expand/collapse animation
- [ ] Bot control panel redesign
- [ ] Quick actions panel redesign
- [ ] Top bar redesign

#### Week 3: Pages Redesign
- [ ] DashboardView complete redesign
- [ ] StockMonitoringView complete redesign
- [ ] LogsView complete redesign
- [ ] SettingsView complete redesign

#### Week 4: Polish & Testing
- [ ] Add loading states & skeletons
- [ ] Add empty states
- [ ] Add error states
- [ ] Performance optimization
- [ ] Accessibility audit
- [ ] User testing
- [ ] Bug fixes

---

## 11. TESTING CHECKLIST

### Visual Testing
- [ ] All pages render correctly
- [ ] Responsive at all breakpoints
- [ ] Colors match design tokens
- [ ] Typography hierarchy correct
- [ ] Spacing consistent

### Functional Testing
- [ ] All buttons work
- [ ] All inputs validated
- [ ] All filters functional
- [ ] All exports working
- [ ] All tests passing

### Accessibility Testing
- [ ] Keyboard navigation works
- [ ] Screen reader compatible
- [ ] Contrast ratios adequate
- [ ] Focus indicators visible
- [ ] Font scaling works

### Performance Testing
- [ ] Page load < 2 seconds
- [ ] No UI lag on interactions
- [ ] Virtualization working
- [ ] Memory usage stable
- [ ] No memory leaks

---

**Last Updated:** 10 April 2026  
**Version:** 4.0.0  
**Status:** Planning Phase 📋

---

**Happy Designing! 🎨**
