# 🏪 Smart Sembako Agent Runtime — Rancangan Arsitektur & Roadmap

> Berdasarkan analisis `percakapan.md` & audit kodebase aktif (v7.0.0)

---

## 📊 Status Implementasi Saat Ini

### ✅ Sudah Ada (Fondasi ~55%)

| Komponen | File | Keterangan |
|---|---|---|
| **Tool System** | `bot_runtime/tools/*.py` | ✅ `inventory_tools`, `sales_tools`, `restock_tools`, `forecast_tools`, `ocr_tools`, `supplier_tools`, `voice_tools` |
| **Tool Registry** | `tools/registry.py` | ✅ BaseTool interface + ToolRegistry |
| **Specialist Agents** | `agents/specialized_agents.py` | ✅ InventoryAgent, SalesAgent, OCRAgent, AnalyticsAgent — *tapi masih thin wrapper* |
| **Master Agent** | `agents/master_agent.py` | ✅ Intent router + tool dispatch + date parsing |
| **AI Memory** | `memory/conversation_store.py` | ✅ Sliding window 12 msg, TTL 24h, Supabase persist |
| **Security/Permission** | `telegram/rbac.py` + `agents/security_agent.py` | ✅ RBAC role (Owner/Kasir/Public), keyword anomaly detect |
| **Model Manager** | `model_manager/manager.py` | ✅ Groq + Gemini cascading failover |
| **RAG Layer** | `rag/embedder.py`, `rag/vector_store.py` | ✅ Fondasi ada, *belum terhubung ke agent pipeline* |
| **Webhook Failover** | `main.py` + `webhook_manager.py` | ✅ Auto swap Desktop↔Cloud |
| **Database Service** | `Services/PosDbService.cs` | ✅ Akses langsung Aronium SQLite POS |
| **Sync Engine** | `Services/SyncService.cs` + `SupabaseClient.cs` | ✅ One-way push ke Supabase |
| **Groq AI Layer** | `Services/GroqService.cs` | ✅ LLM call di sisi C# Desktop |
| **Logging** | `Services/LoggingService.cs` | ✅ Structured logging |
| **OCR Engine** | `Services/AutomationEngine.cs` | ✅ OCR struk dengan Gemini Vision |

---

### ❌ Belum Ada (Perlu Dibangun)

| Komponen dari `percakapan.md` | Gap | Prioritas |
|---|---|---|
| **Agent Supervisor** | Tidak ada orchestrator yang memilih & menggabungkan hasil multi-agent | 🔴 Tinggi |
| **Planner Agent** | Tidak ada decomposition goal → sub-tasks otomatis | 🔴 Tinggi |
| **Reflection / Self-Check Loop** | AI tidak memvalidasi jawaban sebelum dikirim | 🔴 Tinggi |
| **Event-Driven Agent** | Tidak ada trigger real-time saat stok berubah | 🟡 Sedang |
| **Business Memory (Store Brain)** | Tidak ada profil permanen toko (supplier, kebiasaan, musim) | 🟡 Sedang |
| **Scheduler Agent** | Timer ada di C#, tapi tidak terkoneksi ke Agent pipeline | 🟡 Sedang |
| **Anomaly Detection** | SecurityAgent baru cek keyword, bukan pola transaksi | 🟡 Sedang |
| **Personalization Agent** | Tidak ada preferensi respons per user (singkat/panjang) | 🟢 Rendah |
| **Simulation Agent** | Tidak ada fitur "what-if" analisis harga/strategi | 🟢 Rendah |
| **RAG ke Knowledge Base** | RAG ada tapi tidak terpasang ke query pipeline | 🟡 Sedang |
| **Workflow Agent** | Tidak ada multi-step workflow otomatis ("tutup toko" dll.) | 🟢 Rendah |
| **Local AI Hybrid** | Tidak ada mode offline-LLM (model lokal/Ollama) | 🟢 Rendah |

---

## 🏗️ Arsitektur Target (Smart Sembako Agent Runtime)

```
                        USER
                          │
           Telegram / WhatsApp / Desktop Chat
                          │
    ══════════════════════════════════════════════
                  AGENT SUPERVISOR
    ══════════════════════════════════════════════
         │              │              │
     Intent         Memory          Security
     Router         Engine          Gate
         │
     PLANNER AGENT
    (goal decomposition)
         │
    ┌────┼─────┬──────┬───────────┐
    │    │     │      │           │
 Stock  Sales OCR  Finance  Customer
 Agent Agent Agent  Agent   Agent
    │    │     │      │           │
    └────┴─────┴──────┴───────────┘
                  │
           Tool Executor
                  │
       ┌──────────┼──────────┐
       │          │          │
   Supabase    POS.db    REST API
   (Cloud)    (Local)  (Telegram/WA)
                  │
          Context Builder
          + Reflection Loop
                  │
         ┌────────┴────────┐
         │                 │
     Local LLM         Cloud LLM
  (fallback/offline)  (Groq/Gemini)
                  │
            Final Response
```

---

## 🗺️ Roadmap 4 Fase

---

### Phase 1 — Agent Supervisor & Planner (v7.1)
**Target: 2–3 minggu**

Ini gap terbesar. Semua agent berjalan tapi tidak ada yang mengkoordinasi.

#### Yang dibangun:

**`bot_runtime/agents/supervisor.py`** — Agent Supervisor
```python
class AgentSupervisor:
    def select_agents(self, intent: str) -> List[str]: ...
    async def run_parallel(self, agents: List, params: dict) -> dict: ...
    async def merge_results(self, results: List[dict]) -> str: ...
```

**`bot_runtime/agents/planner.py`** — Planner Agent
```python
class PlannerAgent:
    async def decompose(self, goal: str, context: dict) -> List[AgentTask]: ...
    # Contoh: "kenapa profit turun?" →
    # [get_sales_7d, get_sales_14d, get_profit_7d, get_top_products, compare]
```

**`bot_runtime/agents/reflection.py`** — Reflection Loop
```python
class ReflectionAgent:
    async def validate(self, answer: str, data: dict) -> bool: ...
    async def request_more_data(self, missing: List[str]) -> dict: ...
```

#### Integrasi ke `master_agent.py`:
```
User message
    ↓
Intent Router (sudah ada)
    ↓
Planner (BARU) → decompose tasks
    ↓
Supervisor (BARU) → pilih & jalankan agents paralel
    ↓
Reflection (BARU) → validasi sebelum kirim
    ↓
ModelManager → LLM merangkai bahasa
    ↓
Response
```

---

### Phase 2 — Business Memory & Store Brain (v7.2)
**Target: 2 minggu**

Memory saat ini hanya percakapan. Belum ada "profil toko".

#### Yang dibangun:

**`bot_runtime/memory/store_brain.py`** — Store Brain
```python
class StoreBrain:
    """
    Menyimpan pengetahuan permanen tentang toko:
    - profil produk (fast-moving, dead stock)
    - kebiasaan owner (laporan singkat/panjang)
    - supplier utama per kategori
    - pola musiman
    """
    async def get_store_profile(self, user_id: int) -> dict: ...
    async def update_preference(self, user_id: int, key: str, val: Any): ...
    async def record_feedback(self, user_id: int, suggestion: str, accepted: bool): ...
```

**Supabase tabel baru:**
```sql
CREATE TABLE store_brain (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id BIGINT NOT NULL,
    category TEXT NOT NULL,  -- 'preference' | 'supplier' | 'product_pattern'
    key TEXT NOT NULL,
    value JSONB,
    confidence FLOAT DEFAULT 0.5,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
```

#### Personalisasi respons:
- Jika owner pernah bilang "singkat saja" → set `response_style: compact`
- AI otomatis mempersingkat jawaban berikutnya
- Confidence score supplier restock naik jika saran diterima owner

---

### Phase 3 — Event-Driven & Scheduler Agent (v7.3)
**Target: 3 minggu**

Bot menjadi proaktif, tidak hanya reaktif.

#### Yang dibangun:

**`bot_runtime/agents/event_agent.py`** — Event-Driven Agent
```python
class EventAgent:
    TRIGGERS = {
        "low_stock": lambda p: p["stock"] <= p["min_threshold"],
        "void_spike": lambda t: t["void_rate"] > 0.3,
        "daily_summary": lambda: datetime.now().hour == 7,
    }
    async def watch_and_notify(self, user_id: int): ...
```

**`bot_runtime/agents/scheduler_agent.py`** — Scheduler Agent
```python
class SchedulerAgent:
    JOBS = [
        {"time": "07:00", "task": "morning_briefing"},
        {"time": "20:00", "task": "evening_summary"},
        {"time": "*/6h", "task": "low_stock_check"},
    ]
    async def run_job(self, task: str, user_id: int): ...
```

**Integrasi C# Desktop:**
- `SyncService.cs` kirim event ke Supabase `events_queue` tabel
- Cloud Bot polling `events_queue` setiap 5 menit
- Trigger notifikasi proaktif ke Telegram

**Contoh output proaktif:**
```
⏰ Selamat pagi, Pak Saef!

📊 Ringkasan Kemarin:
• Omset: Rp 4.250.000
• Profit: Rp 620.000 (14.6%)

⚠️ Perlu Perhatian:
• Minyak Goreng Bimoli: sisa 3 kg (kritis!)
• Aqua 600ml: sisa 12 pcs (2 hari lagi habis)

💡 Saran Hari Ini:
Restock minyak goreng dari Pak Hadi (terakhir beli 50rb/liter)
```

---

### Phase 4 — Anomaly Detection & Simulation (v8.0)
**Target: 4 minggu**

Meningkatkan keamanan dan kemampuan analitik strategis.

#### Yang dibangun:

**`bot_runtime/agents/anomaly_agent.py`** — Anomaly Detection
```python
class AnomalyAgent:
    async def analyze_transactions(self, date: str) -> List[Anomaly]:
        # Deteksi: void spike, diskon berlebihan, kas tidak cocok
        # Bandingkan pola dengan baseline 30 hari
        ...
    async def alert_owner(self, anomaly: Anomaly, user_id: int): ...
```

**`bot_runtime/agents/simulation_agent.py`** — Simulation "What-If"
```python
class SimulationAgent:
    async def simulate_price_change(self, product: str, new_price: float) -> dict:
        # Hitung prediksi penjualan, profit, dan dampak ke kompetitor
        ...
    async def simulate_restock_scenario(self, product: str, qty: int) -> dict: ...
```

**RAG Knowledge Base (aktivasi penuh):**
- Hubungkan `rag/embedder.py` ke pipeline query
- Index: SOP toko, kebijakan hutang, daftar supplier, aturan retur
- Query: "Bagaimana aturan hutang pelanggan?" → cari dari knowledge base dulu

---

## 📁 Struktur Direktori Target

```
bot_runtime/
├── agents/
│   ├── master_agent.py        ✅ ada
│   ├── supervisor.py          ❌ BARU (Phase 1)
│   ├── planner.py             ❌ BARU (Phase 1)
│   ├── reflection.py          ❌ BARU (Phase 1)
│   ├── event_agent.py         ❌ BARU (Phase 3)
│   ├── scheduler_agent.py     ❌ BARU (Phase 3)
│   ├── anomaly_agent.py       ❌ BARU (Phase 4)
│   ├── simulation_agent.py    ❌ BARU (Phase 4)
│   ├── security_agent.py      ✅ ada (perlu upgrade)
│   └── specialized_agents.py  ✅ ada (perlu upgrade)
├── memory/
│   ├── conversation_store.py  ✅ ada
│   └── store_brain.py         ❌ BARU (Phase 2)
├── tools/
│   ├── registry.py            ✅ ada
│   ├── inventory_tools.py     ✅ ada
│   ├── sales_tools.py         ✅ ada
│   ├── restock_tools.py       ✅ ada
│   ├── forecast_tools.py      ✅ ada
│   ├── ocr_tools.py           ✅ ada
│   ├── supplier_tools.py      ✅ ada
│   ├── customer_tools.py      ❌ BARU (Phase 3)
│   └── document_tools.py      ❌ BARU (Phase 4)
├── rag/
│   ├── embedder.py            ✅ ada (tidak aktif)
│   ├── vector_store.py        ✅ ada (tidak aktif)
│   └── compressor.py          ✅ ada (tidak aktif)
├── model_manager/             ✅ ada (Groq + Gemini)
├── main.py                    ✅ ada
└── webhook_manager.py         ✅ ada
```

---

## 🔑 Prinsip Desain (dari percakapan.md)

> **"LLM bukan pemegang kunci gudang. Dia manajer yang pintar bicara. Yang pegang kunci tetap agent + tool."**

1. **LLM hanya untuk bahasa** — reasoning, penjelasan, laporan
2. **Agent untuk keputusan** — tool calling, data fetching, permission
3. **Memory berlapis** — short-term (conversation) + long-term (store brain)
4. **Reflection sebelum jawab** — validasi data cukup sebelum kirim ke user
5. **Event-driven bukan hanya reaktif** — bot proaktif mengingatkan owner
6. **LLM bisa diganti kapan saja** — arsitektur tidak bergantung pada provider spesifik

---

## 📋 Tabel Pemetaan Lengkap `percakapan.md` → Status

| Ide dari percakapan.md | Status | Fase |
|---|---|---|
| Intent Router | ✅ Selesai (classify_intent) | — |
| Tool System (IAgentTool) | ✅ Selesai (BaseTool + Registry) | — |
| Database Agent | ✅ Partial (tools query Supabase) | — |
| Memory Agent (conversation) | ✅ Selesai (conversation_store.py) | — |
| Security/Permission Layer | ✅ Selesai (RBAC + SecurityAgent) | — |
| Model Manager (LLM abstraction) | ✅ Selesai (Groq + Gemini failover) | — |
| Specialist Agents (Stock, Sales, OCR) | ✅ Partial (thin wrapper) | Phase 1 |
| Agent Supervisor | ❌ Belum ada | Phase 1 |
| Planner Agent | ❌ Belum ada | Phase 1 |
| Reflection / Self-Check | ❌ Belum ada | Phase 1 |
| Business Memory / Store Brain | ❌ Belum ada | Phase 2 |
| Personalization Agent | ❌ Belum ada | Phase 2 |
| Event-Driven Agent | ❌ Belum ada | Phase 3 |
| Scheduler Agent (pagi/malam) | ❌ Belum ada (parsial di C#) | Phase 3 |
| Customer Agent | ❌ Belum ada | Phase 3 |
| RAG Knowledge Base (aktif) | ❌ Ada tapi tidak aktif | Phase 3 |
| Anomaly Detection (pola) | ❌ Belum ada (hanya keyword) | Phase 4 |
| Simulation "What-If" | ❌ Belum ada | Phase 4 |
| Document Agent | ❌ Belum ada | Phase 4 |
| Local AI Hybrid (Ollama) | ❌ Belum ada | Future |
| Agent Marketplace / Plugin | ❌ Belum ada | Future |

---

## ❓ Open Questions

> [!IMPORTANT]
> **Apakah Phase 1 (Supervisor + Planner) menjadi prioritas utama sekarang?**
> Ini yang paling berdampak — tanpa Supervisor, semua specialist agent tidak terkoordinasi.

> [!IMPORTANT]
> **Business Memory (Store Brain) perlu konfirmasi struktur data:**
> Apakah disimpan per `user_id` Telegram (owner) atau per `store_id` (untuk multi-toko di masa depan)?

> [!NOTE]
> **RAG Knowledge Base** sudah ada fondasinya. Dokumen apa yang akan dimasukkan ke knowledge base? (SOP toko, daftar supplier, aturan hutang?)

> [!NOTE]
> **Scheduler Agent** — Jam berapa notifikasi pagi/malam yang diinginkan? Apakah bisa dikonfigurasi via chat ("ingatkan saya tiap hari jam 7 pagi")?
