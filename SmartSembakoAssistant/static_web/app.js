// Mock Sembako Inventory Data (Offline Standalone Mode)
const mockProducts = [
    { code: "PRD-001", name: "Beras Ramos Super 5kg", category: "Beras", price: 68000, stock: 42, unit: "karung" },
    { code: "PRD-002", name: "Minyak Goreng Bimoli 2L", category: "Minyak", price: 34500, stock: 8, unit: "pouch" },
    { code: "PRD-003", name: "Gula Pasir Gulaku 1kg", category: "Gula", price: 17500, stock: 15, unit: "kg" },
    { code: "PRD-004", name: "Telur Ayam Negeri 1kg", category: "Telur", price: 28000, stock: 5, unit: "kg" },
    { code: "PRD-005", name: "Indomie Goreng Special", category: "Mie Instan", price: 3100, stock: 120, unit: "pcs" },
    { code: "PRD-006", name: "Tepung Terigu Segitiga Biru 1kg", category: "Tepung", price: 13000, stock: 4, unit: "kg" },
    { code: "PRD-007", name: "Kecap Manis Bango 520ml", category: "Bumbu", price: 24000, stock: 18, unit: "btl" },
    { code: "PRD-008", name: "Susu Kental Manis Frisian Flag", category: "Susu", price: 12500, stock: 3, unit: "kaleng" }
];

document.addEventListener("DOMContentLoaded", () => {
    initNavigation();
    renderDashboard();
    renderInventory(mockProducts);
    initChatSimulator();
    initSyncButton();
});

// Navigation Handling
function initNavigation() {
    const navItems = document.querySelectorAll(".nav-item");
    const tabContents = document.querySelectorAll(".tab-content");
    const pageTitle = document.getElementById("page-title");
    const pageSubtitle = document.getElementById("page-subtitle");

    const titles = {
        "dashboard": { title: "Dashboard Penjualan & Stok", subtitle: "Pantau aktivitas toko sembako Anda secara langsung tanpa biaya langganan." },
        "inventory": { title: "Kelola Stok Produk (Mode Offline)", subtitle: "Daftar produk aktif yang tersinkronisasi dari POS Desktop lokal." },
        "bot-demo": { title: "Simulasi Telegram Assistant", subtitle: "Uji respons kecerdasan bot dalam melayani query stok dan penjualan secara lokal." },
        "settings": { title: "Status System & Mode Statis", subtitle: "Pengaturan integrasi tanpa langganan cloud." }
    };

    navItems.forEach(item => {
        item.addEventListener("click", () => {
            const targetTab = item.getAttribute("data-tab");
            
            navItems.forEach(n => n.classList.remove("active"));
            tabContents.forEach(c => c.classList.remove("active"));

            item.classList.add("active");
            document.getElementById(`tab-${targetTab}`).classList.add("active");

            if (titles[targetTab]) {
                pageTitle.textContent = titles[targetTab].title;
                pageSubtitle.textContent = titles[targetTab].subtitle;
            }
        });
    });
}

// Render Dashboard Overview
function renderDashboard() {
    const topProductsBody = document.getElementById("top-products-list");
    const lowStockContainer = document.getElementById("low-stock-list");

    // Top Products
    const sortedBySales = [...mockProducts].sort((a, b) => b.stock - a.stock).slice(0, 4);
    topProductsBody.innerHTML = sortedBySales.map(p => `
        <tr>
            <td><strong>${p.name}</strong></td>
            <td>Rp ${p.price.toLocaleString('id-ID')}</td>
            <td><span class="badge badge-success">${Math.floor(p.stock * 1.5)} terjual</span></td>
            <td>${p.stock} ${p.unit}</td>
        </tr>
    `).join('');

    // Low Stock Alerts
    const lowStockItems = mockProducts.filter(p => p.stock <= 10);
    document.getElementById("stat-low-stock").textContent = `${lowStockItems.length} Produk`;

    lowStockContainer.innerHTML = lowStockItems.map(p => `
        <div class="alert-item">
            <div class="alert-info">
                <h4>${p.name}</h4>
                <p>Sisa stok tinggal <strong>${p.stock} ${p.unit}</strong> (Batas min: 10 ${p.unit})</p>
            </div>
            <button class="btn btn-sm btn-outline" onclick="simulatedRestock('${p.code}')">+ Restock</button>
        </div>
    `).join('');
}

// Render Inventory Table
function renderInventory(products) {
    const tbody = document.getElementById("inventory-table-body");
    
    tbody.innerHTML = products.map(p => {
        const isLow = p.stock <= 10;
        const statusBadge = isLow 
            ? `<span class="badge badge-warning">Stok Kritis</span>` 
            : `<span class="badge badge-success">Aman</span>`;

        return `
            <tr>
                <td><code>${p.code}</code></td>
                <td><strong>${p.name}</strong></td>
                <td>${p.category}</td>
                <td>Rp ${p.price.toLocaleString('id-ID')}</td>
                <td>${p.stock} ${p.unit}</td>
                <td>${statusBadge}</td>
            </tr>
        `;
    }).join('');
}

// Filter & Search Inventory
const searchInput = document.getElementById("search-inventory");
if (searchInput) {
    searchInput.addEventListener("input", (e) => {
        const query = e.target.value.toLowerCase();
        const filtered = mockProducts.filter(p => 
            p.name.toLowerCase().includes(query) || 
            p.code.toLowerCase().includes(query) ||
            p.category.toLowerCase().includes(query)
        );
        renderInventory(filtered);
    });
}

// Chat Simulator (Offline Rule-Based AI)
function initChatSimulator() {
    const btnSend = document.getElementById("btn-send-chat");
    const chatInput = document.getElementById("chat-input");

    if (btnSend && chatInput) {
        btnSend.addEventListener("click", () => handleSendMessage());
        chatInput.addEventListener("keypress", (e) => {
            if (e.key === "Enter") handleSendMessage();
        });
    }
}

function sendQuickReply(text) {
    const chatInput = document.getElementById("chat-input");
    if (chatInput) {
        chatInput.value = text;
        handleSendMessage();
    }
}

function handleSendMessage() {
    const chatInput = document.getElementById("chat-input");
    const text = chatInput.value.trim();
    if (!text) return;

    appendMessage("user", text);
    chatInput.value = "";

    // Simulated Bot Typing & Response
    setTimeout(() => {
        const botReply = generateOfflineBotReply(text);
        appendMessage("bot", botReply);
    }, 600);
}

function appendMessage(sender, text) {
    const messagesContainer = document.getElementById("chat-messages");
    const msgDiv = document.createElement("div");
    msgDiv.className = `message ${sender}`;

    msgDiv.innerHTML = `
        <div class="msg-bubble">
            ${text}
        </div>
    `;

    messagesContainer.appendChild(msgDiv);
    messagesContainer.scrollTop = messagesContainer.scrollHeight;
}

function generateOfflineBotReply(userMsg) {
    const lower = userMsg.toLowerCase();

    if (lower.includes("minyak")) {
        const item = mockProducts.find(p => p.name.toLowerCase().includes("minyak"));
        return `📦 <b>Info Stok Minyak:</b><br>${item.name}: sisa <b>${item.stock} ${item.unit}</b> (Rp ${item.price.toLocaleString('id-ID')}).`;
    } 
    else if (lower.includes("omset") || lower.includes("penjualan")) {
        return `💰 <b>Ringkasan Omset Toko Hari Ini:</b><br>- Total Omset: <b>Rp 3.480.000</b><br>- Transaksi: <b>48 Struk</b><br>- Produk terlaris: Beras Ramos Super 5kg.`;
    }
    else if (lower.includes("restock") || lower.includes("kritis")) {
        const criticals = mockProducts.filter(p => p.stock <= 10).map(p => `- ${p.name}: sisa ${p.stock} ${p.unit}`).join("<br>");
        return `⚠️ <b>Daftar Produk Perlu Restock:</b><br>${criticals}`;
    }
    else if (lower.includes("beras")) {
        const item = mockProducts.find(p => p.name.toLowerCase().includes("beras"));
        return `📦 <b>Info Stok Beras:</b><br>${item.name}: sisa <b>${item.stock} ${item.unit}</b> (Rp ${item.price.toLocaleString('id-ID')}).`;
    }
    else {
        return `🤖 <b>Smart Assistant Mode Offline:</b><br>Saya memahami query Anda tentang <i>"${userMsg}"</i>. Data tersinkronisasi langsung dari database POS toko.`;
    }
}

// Simulated Restock Action
function simulatedRestock(code) {
    const item = mockProducts.find(p => p.code === code);
    if (item) {
        item.stock += 20;
        renderDashboard();
        renderInventory(mockProducts);
        alert(`✅ Stok ${item.name} berhasil ditambah +20 ${item.unit}!`);
    }
}

// Sync Demo Button Action
function initSyncButton() {
    const btnSync = document.getElementById("btn-sync-demo");
    if (btnSync) {
        btnSync.addEventListener("click", () => {
            btnSync.innerHTML = "⏳ Memproses Sync...";
            setTimeout(() => {
                btnSync.innerHTML = "⚡ Sync POS Lokal";
                alert("✅ Sinkronisasi POS lokal berhasil! Data 8 produk dan transaksi toko telah diperbarui.");
            }, 800);
        });
    }
}
