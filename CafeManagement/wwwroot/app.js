const statusText = document.getElementById('connection-status');
const statusBadge = document.getElementById('status-badge');
const orderCountEl = document.getElementById('order-count');
const orderCountNum = document.getElementById('order-count-num');
const noOrdersEl = document.getElementById('no-orders');
const gridEl = document.getElementById('orders-grid');
const toastContainer = document.getElementById('toast-container');

let eventSource = null;
let lastOrderCount = -1;
let wakeLock = null;

// ── WakeLock: Keep screen active ──
async function requestWakeLock() {
    if ('wakeLock' in navigator) {
        try {
            wakeLock = await navigator.wakeLock.request('screen');
            document.addEventListener('visibilitychange', async () => {
                if (document.visibilityState === 'visible' && wakeLock === null) {
                    wakeLock = await navigator.wakeLock.request('screen');
                }
            });
        } catch (e) {
            // WakeLock not active, ignore
        }
    }
}

// ── Notification Permissions ──
async function requestNotificationPermission() {
    if ('Notification' in window && Notification.permission === 'default') {
        await Notification.requestPermission();
    }
}

function sendNotification(title, body) {
    if ('Notification' in window && Notification.permission === 'granted') {
        new Notification(title, {
            body: body,
            icon: 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><text y=".9em" font-size="90">🍳</text></svg>',
            tag: 'kitchen-alert',
            renotify: true,
            requireInteraction: false
        });
    }
}

// ── Audio Alert System ──
function playAlertSound() {
    if (localStorage.getItem('kitchen_sound_disabled') === 'true') return;
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const times = [0, 0.22, 0.44];
        times.forEach(offset => {
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.frequency.value = 880;
            osc.type = 'sine';
            gain.gain.setValueAtTime(0, ctx.currentTime + offset);
            gain.gain.linearRampToValueAtTime(0.5, ctx.currentTime + offset + 0.04);
            gain.gain.linearRampToValueAtTime(0, ctx.currentTime + offset + 0.18);
            osc.start(ctx.currentTime + offset);
            osc.stop(ctx.currentTime + offset + 0.22);
        });
    } catch (e) {
        // AudioContext blocked or not supported, ignore
    }
}

// ── Vibration System ──
function vibrate() {
    if ('vibrate' in navigator) {
        navigator.vibrate([200, 100, 200, 100, 400]);
    }
}

// ── Custom Toasts ──
function showToast(message) {
    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.innerHTML = '<span>🔔</span> <span>' + message + '</span>';
    toastContainer.appendChild(toast);
    
    // Auto-remove animation
    setTimeout(() => {
        toast.classList.add('toast-fadeout');
        setTimeout(() => toast.remove(), 400);
    }, 2800);
}

// ── Trigger Alerts ──
function triggerAlert(newCount) {
    const itemText = newCount + ' món cần chế biến';
    sendNotification('🍳 Có đơn mới!', itemText);
    playAlertSound();
    vibrate();
    showToast('Đơn mới! ' + itemText);
}

// ── SSE Connection Manager ──
function connectSSE() {
    statusText.innerText = 'Đang kết nối...';
    statusBadge.className = 'status-indicator disconnected';

    eventSource = new EventSource('/api/kitchen/events');

    eventSource.onopen = () => {
        statusText.innerText = 'Đã kết nối';
        statusBadge.className = 'status-indicator connected';
        loadOrders();
    };

    eventSource.onerror = () => {
        statusText.innerText = 'Mất kết nối - Thử lại...';
        statusBadge.className = 'status-indicator disconnected';
        eventSource.close();
        setTimeout(connectSSE, 3000);
    };

    eventSource.addEventListener('ping', () => {});

    eventSource.addEventListener('update', () => {
        loadOrders(true);
    });
}

// ── Load Orders ──
async function loadOrders(fromUpdate = false) {
    try {
        const res = await fetch('/api/kitchen/pending');
        if (!res.ok) throw new Error();
        const orders = await res.json();
        const activeCount = orders.filter(item => item.status !== 'Served').length;

        // Alert only when there are new items added
        if (fromUpdate && lastOrderCount >= 0 && activeCount > lastOrderCount) {
            triggerAlert(activeCount);
        }
        
        lastOrderCount = activeCount;
        
        // Update header count badge
        if (activeCount > 0) {
            orderCountNum.innerText = activeCount;
            orderCountEl.style.display = 'block';
        } else {
            orderCountEl.style.display = 'none';
        }
        
        renderOrders(orders);
    } catch (err) {
        console.error('Error loading orders:', err);
    }
}

// ── Render Orders Grid ──
function renderOrders(orders) {
    gridEl.innerHTML = '';
    if (orders.length === 0) {
        noOrdersEl.style.display = 'flex';
        gridEl.style.display = 'none';
        return;
    }

    // Group items by table
    const groups = {};
    orders.forEach(item => {
        const key = item.tableNumber || 'Mang đi';
        if (!groups[key]) {
            groups[key] = {
                tableName: key,
                items: [],
                minCreatedAt: new Date(item.createdAt)
            };
        } else {
            const itemDate = new Date(item.createdAt);
            if (itemDate < groups[key].minCreatedAt) {
                groups[key].minCreatedAt = itemDate;
            }
        }
        groups[key].items.push(item);
    });

    // Sort tables: oldest minCreatedAt first
    const sortedGroups = Object.values(groups).sort((a, b) => a.minCreatedAt - b.minCreatedAt);

    // Filter out groups where ALL items have been served
    const activeGroups = sortedGroups.filter(group => {
        return group.items.some(item => item.status !== 'Served');
    });

    if (activeGroups.length === 0) {
        noOrdersEl.style.display = 'flex';
        gridEl.style.display = 'none';
        return;
    }

    noOrdersEl.style.display = 'none';
    gridEl.style.display = 'grid';

    activeGroups.forEach(group => {
        const card = document.createElement('div');
        card.className = 'table-card';

        // Calculate time elapsed
        const elapsed = Math.floor((new Date() - group.minCreatedAt) / 60000);
        let urgencyBorder = '5px solid #10b981';
        
        if (elapsed >= 15) {
            urgencyBorder = '5px solid #ef4444';
            card.style.animation = 'pulse-glow 2s infinite';
        } else if (elapsed >= 8) {
            urgencyBorder = '5px solid #f59e0b';
        }
        
        const d = group.minCreatedAt;
        const hh = String(d.getHours()).padStart(2, '0');
        const mm = String(d.getMinutes()).padStart(2, '0');
        const timeText = hh + ':' + mm;

        // Card Header
        let cardHtml = `
            <div class="table-card-header" style="border-left: ${urgencyBorder}">
                <div class="table-title">${escapeHtml(group.tableName)}</div>
                <div class="table-time">🕒 ${timeText} (${elapsed}m)</div>
            </div>
            <div class="table-card-body">
        `;

        // Render Card Items
        group.items.forEach(item => {
            let noteHtml = item.note ? '<span class="item-note">' + escapeHtml(item.note) + '</span>' : '';
            
            // Map statuses to CSS classes, labels, and button texts
            let rowClass = 'item-row status-pending';
            let btnText = 'Chế biến xong';
            let btnDisabled = '';
            let stageBadge = '<span class="stage-badge stage-pending">⏳ Chờ chế biến</span>';
            
            if (item.status === 'Completed') {
                rowClass = 'item-row status-completed';
                btnText = 'Chờ in';
                btnDisabled = 'disabled';
                stageBadge = '<span class="stage-badge stage-completed">✅ Chờ in (Bếp)</span>';
            } else if (item.status === 'Served') {
                rowClass = 'item-row status-served';
                btnText = 'Đã in & chuyển';
                btnDisabled = 'disabled';
                stageBadge = '<span class="stage-badge stage-served">👍 Đã giao</span>';
            }
            
            cardHtml += `
                <div class="${rowClass}">
                    <div class="item-detail">
                        <div class="item-product-name">${escapeHtml(item.productName)}</div>
                        <div class="item-qty-and-note">
                            <span class="item-qty">SL: ${item.quantity}</span>
                            ${stageBadge}
                            ${noteHtml}
                        </div>
                    </div>
                    <button class="btn-complete-item" ${btnDisabled} onclick="completeItem(${item.id}, this)">${btnText}</button>
                </div>
            `;
        });

        cardHtml += `</div>`;
        card.innerHTML = cardHtml;
        gridEl.appendChild(card);
    });
}

// ── Complete Item Action ──
async function completeItem(id, btn) {
    btn.disabled = true;
    const oldText = btn.innerText;
    btn.innerText = '...';
    try {
        const res = await fetch('/api/kitchen/complete/' + id, { method: 'POST' });
        if (res.ok) {
            loadOrders();
        } else {
            showToast('Không thể cập nhật món. Thử lại sau.');
            btn.disabled = false;
            btn.innerText = oldText;
        }
    } catch (err) {
        console.error('Error completing item:', err);
        showToast('Kết nối thất bại. Thử lại sau.');
        btn.disabled = false;
        btn.innerText = oldText;
    }
}

// ── Helpers ──
function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;')
              .replace(/</g, '&lt;')
              .replace(/>/g, '&gt;')
              .replace(/"/g, '&quot;')
              .replace(/'/g, '&#039;');
}

// ── Initialization ──
requestWakeLock();
requestNotificationPermission();
connectSSE();
setInterval(() => {
    // Keep elapsed time indicator fresh every minute
    loadOrders(false);
}, 60000);
