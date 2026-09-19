// Configuration & State
const API_BASE = 'http://localhost:5000/api';
let authToken = localStorage.getItem('emergency_test_token') || '';
let currentUser = null;
let currentIncident = null;
let editingIceId = null;

// DOM Elements
const backendStatusPill = document.getElementById('backend-status');
const backendStatusText = document.getElementById('backend-status-text');
const btnQuickLogin = document.getElementById('btn-quick-login');
const userBadge = document.getElementById('user-badge');
const toastEl = document.getElementById('toast');

// Initialize on DOM Ready
document.addEventListener('DOMContentLoaded', () => {
    initTabs();
    checkBackendHealth();
    initGuestSosTab();
    initMedicalIdTab();
    initIceContactsTab();
});

// ============================================================
// HELPER FUNCTIONS
// ============================================================
function showToast(message, type = 'info') {
    toastEl.textContent = message;
    toastEl.className = `toast show ${type}`;
    setTimeout(() => {
        toastEl.className = 'toast';
    }, 3500);
}

function parseArrayInput(value) {
    if (!value || typeof value !== 'string') return [];
    return value
        .split(',')
        .map(s => s.trim())
        .filter(s => s.length > 0);
}

function formatArrayForInput(arr) {
    if (!arr || !Array.isArray(arr)) return '';
    return arr.join(', ');
}

// ============================================================
// HEALTH CHECK & AUTHENTICATION
// ============================================================
async function checkBackendHealth() {
    try {
        const res = await fetch(`${API_BASE}/incidents?pageSize=1`);
        if (res.ok || res.status === 401) {
            backendStatusPill.className = 'backend-status-pill online';
            backendStatusText.textContent = 'API Backend Online (Port 5000)';
            if (!authToken) {
                await quickLogin();
            } else {
                await loadUserProfile();
            }
        } else {
            throw new Error(`Status ${res.status}`);
        }
    } catch (err) {
        backendStatusPill.className = 'backend-status-pill offline';
        backendStatusText.textContent = 'Backend Offline (Kiểm tra dotnet run)';
        console.warn('Backend connection error:', err);
    }
}

async function quickLogin() {
    try {
        const res = await fetch(`${API_BASE}/auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                email: 'admin@emergencydispatch.com',
                password: 'Admin@123456'
            })
        });

        const data = await res.json();
        if (res.ok && data.success) {
            authToken = data.data.accessToken;
            localStorage.setItem('emergency_test_token', authToken);
            currentUser = data.data.user;
            updateUserBadge();
            showToast('Đăng nhập tài khoản Test thành công!', 'success');
            loadUserProfile();
            loadIceContacts();
        } else {
            showToast('Không thể tự động đăng nhập: ' + (data.message || 'Lỗi server'), 'error');
        }
    } catch (err) {
        console.error('Quick login error:', err);
        showToast('Lỗi kết nối khi đăng nhập', 'error');
    }
}

function updateUserBadge() {
    if (currentUser) {
        userBadge.textContent = `👤 ${currentUser.fullName || currentUser.email} (${currentUser.role || 'Admin'})`;
        userBadge.classList.remove('hidden');
        btnQuickLogin.textContent = '🔄 Đăng nhập lại';
    }
}

btnQuickLogin.addEventListener('click', () => {
    quickLogin();
});

// ============================================================
// NAVIGATION TABS
// ============================================================
function initTabs() {
    const tabLinks = document.querySelectorAll('.tab-link');
    const tabPanes = document.querySelectorAll('.tab-pane');

    tabLinks.forEach(link => {
        link.addEventListener('click', () => {
            const targetId = link.getAttribute('data-tab');
            tabLinks.forEach(l => l.classList.remove('active'));
            tabPanes.forEach(p => p.classList.remove('active'));

            link.classList.add('active');
            const targetPane = document.getElementById(targetId);
            if (targetPane) targetPane.classList.add('active');

            if (targetId === 'tab-ice-contacts') {
                loadIceContacts();
            } else if (targetId === 'tab-medical-id') {
                loadUserProfile();
            }
        });
    });
}

// ============================================================
// TAB 1: GUEST SOS & TỰ HỦY SỰ CỐ
// ============================================================
function initGuestSosTab() {
    const formGuestSos = document.getElementById('form-guest-sos');
    const btnFillPreset = document.getElementById('btn-fill-sos-preset');
    const liveState = document.getElementById('incident-live-state');
    const cancelBox = document.getElementById('cancel-action-box');
    const btnCancelNow = document.getElementById('btn-cancel-incident-now');
    const cancelReasonInput = document.getElementById('cancel-reason');

    btnFillPreset.addEventListener('click', () => {
        const presets = [
            {
                title: "Chập điện bốc khói tại tầng 2",
                desc: "Đã cúp cầu dao, đang tự dập bằng cát và bình xịt CO2.",
                address: "456 Lê Hồng Phong, Phường 1, Quận 10, TP.HCM",
                lat: 10.7629,
                lng: 106.6738
            },
            {
                title: "Khói phát ra từ ban công nhà liền kề",
                desc: "Nghi ngờ đốt rác gây khói, người dân đang kiểm tra.",
                address: "789 Cách Mạng Tháng 8, Quận 3, TP.HCM",
                lat: 10.7781,
                lng: 106.6854
            }
        ];
        const sample = presets[Math.floor(Math.random() * presets.length)];
        document.getElementById('sos-title').value = sample.title;
        document.getElementById('sos-desc').value = sample.desc;
        document.getElementById('sos-address').value = sample.address;
        document.getElementById('sos-lat').value = sample.lat;
        document.getElementById('sos-lng').value = sample.lng;
        showToast('Đã nạp dữ liệu sự cố mẫu', 'info');
    });

    formGuestSos.addEventListener('submit', async (e) => {
        e.preventDefault();

        const sosData = {
            title: document.getElementById('sos-title').value.trim(),
            description: document.getElementById('sos-desc').value.trim(),
            locationAddress: document.getElementById('sos-address').value.trim(),
            latitude: parseFloat(document.getElementById('sos-lat').value),
            longitude: parseFloat(document.getElementById('sos-lng').value),
            reporterName: 'Khách Vãng Lai (Chưa đăng nhập)',
            reporterPhone: '0988776655',
            mediaUrls: []
        };

        try {
            // Không truyền header Authorization -> Gọi dạng Guest
            const res = await fetch(`${API_BASE}/incidents`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(sosData)
            });

            const result = await res.json();
            if (res.ok && result.success) {
                currentIncident = result.data;
                renderIncidentState(currentIncident);
                cancelBox.classList.remove('hidden');
                showToast('Đã phát tín hiệu SOS thành công (Guest SOS)!', 'success');
            } else {
                showToast('Lỗi gửi SOS: ' + (result.message || 'Không thể tạo sự cố'), 'error');
            }
        } catch (err) {
            console.error('Error sending guest SOS:', err);
            showToast('Lỗi kết nối tới Backend', 'error');
        }
    });

    const btnSubmitCitizenSos = document.getElementById('btn-submit-citizen-sos');
    if (btnSubmitCitizenSos) {
        btnSubmitCitizenSos.addEventListener('click', async () => {
            if (!authToken) {
                showToast('Vui lòng đăng nhập trước khi gửi SOS người dân!', 'error');
                await quickLogin();
            }

            const sosData = {
                title: document.getElementById('sos-title').value.trim(),
                description: document.getElementById('sos-desc').value.trim(),
                locationAddress: document.getElementById('sos-address').value.trim(),
                latitude: parseFloat(document.getElementById('sos-lat').value),
                longitude: parseFloat(document.getElementById('sos-lng').value),
                reporterName: currentUser?.fullName || 'Người dân đã đăng ký',
                reporterPhone: currentUser?.phoneNumber || '0901234567',
                mediaUrls: []
            };

            try {
                // Truyền Authorization Header -> Gắn ID người dùng & Medical Profile + Kích hoạt SMS
                const res = await fetch(`${API_BASE}/incidents`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': `Bearer ${authToken}`
                    },
                    body: JSON.stringify(sosData)
                });

                const result = await res.json();
                if (res.ok && result.success) {
                    currentIncident = result.data;
                    renderIncidentState(currentIncident);
                    cancelBox.classList.remove('hidden');
                    showToast('Đã phát SOS Người dân (Kèm Medical ID & kích hoạt SMS)!', 'success');
                } else {
                    showToast('Lỗi: ' + (result.message || 'Không thể gửi SOS'), 'error');
                }
            } catch (err) {
                showToast('Lỗi kết nối server', 'error');
            }
        });
    }

    btnCancelNow.addEventListener('click', async () => {
        if (!currentIncident || !currentIncident.id) {
            showToast('Chưa có sự cố nào để hủy!', 'error');
            return;
        }

        const reason = cancelReasonInput.value.trim() || 'Người báo tự hủy sự cố';

        try {
            // Khách hoặc người dân tự hủy (kèm token nếu có)
            const headers = { 'Content-Type': 'application/json' };
            if (authToken) headers['Authorization'] = `Bearer ${authToken}`;

            const res = await fetch(`${API_BASE}/incidents/${currentIncident.id}/cancel`, {
                method: 'PUT',
                headers,
                body: JSON.stringify({ reason })
            });

            const result = await res.json();
            if (res.ok && result.success) {
                currentIncident = result.data;
                renderIncidentState(currentIncident);
                cancelBox.classList.add('hidden');
                showToast('Đã tự hủy sự cố thành công!', 'success');
            } else {
                showToast('Không thể hủy: ' + (result.message || 'Lỗi server'), 'error');
            }
        } catch (err) {
            console.error('Error cancelling incident:', err);
            showToast('Lỗi kết nối khi hủy sự cố', 'error');
        }
    });

    function renderIncidentState(inc) {
        let badgeClass = 'badge-pending';
        let statusText = inc.status;
        if (inc.status === 'Cancelled') badgeClass = 'badge-cancelled';
        if (inc.status === 'Dispatched') badgeClass = 'badge-dispatched';
        if (inc.status === 'Completed') badgeClass = 'badge-completed';

        const med = inc.reporterMedicalProfile;

        liveState.innerHTML = `
            <div class="incident-card-status">
                <div class="incident-badge-row">
                    <strong style="font-size: 16px;">${inc.title || 'Sự cố'}</strong>
                    <span class="badge ${badgeClass}">${statusText}</span>
                </div>

                ${inc.emergencySmsDispatchLog ? `
                <div class="alert alert-info" style="font-size: 12px; margin-bottom: 12px;">
                    📲 <strong>HỆ THỐNG GỬI SMS TỰ ĐỘNG KHI PHÁT SOS:</strong><br>
                    ${inc.emergencySmsDispatchLog}
                </div>` : ''}

                <div class="incident-meta-list">
                    <div class="incident-meta-item">
                        <span class="incident-meta-label">Mã sự cố (ID):</span>
                        <span class="incident-meta-val">${inc.id}</span>
                    </div>
                    <div class="incident-meta-item">
                        <span class="incident-meta-label">Người báo cáo:</span>
                        <span>${inc.reporterName || 'Nặc danh / Khách vãng lai'} ${inc.reporterPhone ? `(${inc.reporterPhone})` : ''}</span>
                    </div>
                    <div class="incident-meta-item">
                        <span class="incident-meta-label">Địa chỉ:</span>
                        <span>${inc.locationAddress || 'N/A'}</span>
                    </div>
                    <div class="incident-meta-item">
                        <span class="incident-meta-label">Tọa độ GPS:</span>
                        <span class="incident-meta-val">${inc.latitude?.toFixed(4)}, ${inc.longitude?.toFixed(4)}</span>
                    </div>
                    <div class="incident-meta-item">
                        <span class="incident-meta-label">Thời gian tạo:</span>
                        <span>${new Date(inc.createdAt || Date.now()).toLocaleTimeString()}</span>
                    </div>
                    ${inc.operatorNotes ? `
                    <div class="incident-meta-item" style="color: #F87171;">
                        <span class="incident-meta-label">Ghi chú hủy / điều phối:</span>
                        <span>${inc.operatorNotes}</span>
                    </div>` : ''}
                </div>

                ${med ? `
                <!-- HỒ SƠ Y TẾ VÀ ĐẶC THÙ CỨU NẠN GẮN LIỀN VỚI SỰ CỐ -->
                <div style="background: rgba(59, 130, 246, 0.08); border: 1px solid rgba(59, 130, 246, 0.3); border-radius: 8px; padding: 12px; margin-bottom: 12px;">
                    <strong style="color: #60A5FA; font-size: 13px; display: block; margin-bottom: 8px;">
                        📋 HỒ SƠ Y TẾ & CỨU HỘ ĐÍNH KÈM (Cung cấp tức thì cho Kíp 114 / 115):
                    </strong>
                    <div style="font-size: 12px; line-height: 1.6;">
                        <div>🩸 <strong>Nhóm máu:</strong> <span style="color: #F87171; font-weight: bold;">${med.bloodType || 'Chưa rõ'}</span> | 🗣️ <strong>Ngôn ngữ:</strong> ${med.preferredLanguage || 'Tiếng Việt'}</div>
                        <div>⚠️ <strong>Bệnh nền mạn tính:</strong> ${med.chronicConditions?.length ? med.chronicConditions.join(', ') : 'Không có'}</div>
                        <div>🚨 <strong>DỊ ỨNG NGUY HIỂM:</strong> <span style="color: #FBBF24; font-weight: bold;">${med.allergies?.length ? med.allergies.join(', ') : 'Không có'}</span></div>
                        <div>💊 <strong>Thuốc đang sử dụng:</strong> ${med.currentMedications?.length ? med.currentMedications.join(', ') : 'Không có'}</div>
                        <div>♿ <strong>Hạn chế vận động:</strong> ${med.mobilityLimitations?.length ? med.mobilityLimitations.join(', ') : 'Bình thường'}</div>
                        <div>🏠 <strong>Thông tin căn hộ:</strong> ${med.householdMembersCount} người | <em>${med.dependentsNote || 'Không có người già/trẻ nhỏ phụ thuộc'}</em></div>
                        <div style="margin-top: 6px; padding-top: 6px; border-top: 1px dashed rgba(255,255,255,0.1);">
                            👨‍👩‍👧‍👦 <strong>Người thân khẩn cấp (ICE):</strong><br>
                            ${med.iceContacts?.length ? med.iceContacts.map(c => `• <strong>${c.name}</strong> (${c.relationship}) - 📞 ${c.phoneNumber} ${c.isPrimary ? '<span style="color: #FBBF24;">⭐(Chính)</span>' : ''}`).join('<br>') : 'Chưa có danh bạ người thân'}
                        </div>
                    </div>
                </div>` : `
                <div class="alert alert-warning" style="font-size: 12px;">
                    ℹ️ <strong>Khách vãng lai (Chưa đăng nhập):</strong> Sự cố không có hồ sơ y tế hay danh bạ người thân đính kèm.
                </div>`}

                ${inc.status === 'Cancelled' ? `
                <div class="alert alert-success">
                    ✅ <strong>Sự cố đã được hủy thành công bởi người báo!</strong><br>
                    Không có xe cứu hộ nào bị điều phối nhầm.
                </div>` : ''}
            </div>
        `;
    }
}

// ============================================================
// TAB 2: MEDICAL ID & SPECIAL INFO
// ============================================================
function initMedicalIdTab() {
    const formMedical = document.getElementById('form-medical-id');
    const btnFillPreset = document.getElementById('btn-fill-medical-preset');

    btnFillPreset.addEventListener('click', () => {
        document.getElementById('med-blood-type').value = 'O_Positive';
        document.getElementById('med-notes').value = 'Tiền sử phẫu thuật tim năm 2021, huyết áp dao động.';
        document.getElementById('med-chronic').value = 'Tim mạch, Tiểu đường tuýp 2, Hen phế quản';
        document.getElementById('med-allergies').value = 'Thuốc Penicillin, Kháng sinh Aspirin, Đậu phộng';
        document.getElementById('med-medications').value = 'Amlodipine 5mg (1 viên/sáng), Metformin 500mg, Ventolin xịt';
        document.getElementById('med-mobility').value = 'Hạn chế đi thang bộ, di chuyển chậm';
        document.getElementById('med-language').value = 'Tiếng Việt & English';
        document.getElementById('med-members-count').value = 4;
        document.getElementById('med-dependents').value = 'Có mẹ già 82 tuổi tai biến nhẹ và bé gái 3 tuổi';
        document.getElementById('med-auto-sms').checked = true;
        document.getElementById('med-sms-template').value = 'CẤP CỨU: Tôi vừa bấm SOS tại tọa độ {lat},{lng}. Cần hỗ trợ cứu hộ khẩn cấp!';
        showToast('Đã điền dữ liệu Hồ sơ Y tế mẫu', 'info');
    });

    formMedical.addEventListener('submit', async (e) => {
        e.preventDefault();

        if (!authToken) {
            showToast('Vui lòng đăng nhập trước khi cập nhật hồ sơ!', 'error');
            return;
        }

        const updateData = {
            fullName: currentUser?.fullName || 'System Administrator',
            phoneNumber: currentUser?.phoneNumber || '0901234567',
            bloodType: document.getElementById('med-blood-type').value || null,
            medicalNotes: document.getElementById('med-notes').value.trim(),
            chronicConditions: parseArrayInput(document.getElementById('med-chronic').value),
            allergies: parseArrayInput(document.getElementById('med-allergies').value),
            currentMedications: parseArrayInput(document.getElementById('med-medications').value),
            mobilityLimitations: parseArrayInput(document.getElementById('med-mobility').value),
            preferredLanguage: document.getElementById('med-language').value.trim(),
            householdMembersCount: parseInt(document.getElementById('med-members-count').value) || 1,
            dependentsNote: document.getElementById('med-dependents').value.trim(),
            autoSendSmsOnSos: document.getElementById('med-auto-sms').checked,
            smsTemplate: document.getElementById('med-sms-template').value.trim()
        };

        try {
            const res = await fetch(`${API_BASE}/users/me`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${authToken}`
                },
                body: JSON.stringify(updateData)
            });

            const result = await res.json();
            if (res.ok && result.success) {
                currentUser = result.data;
                populateMedicalFields(result.data);
                showToast('Hồ sơ Y tế & Thông tin cứu nạn đã được lưu thành công!', 'success');
            } else {
                showToast('Lỗi cập nhật: ' + (result.message || 'Không thể lưu'), 'error');
            }
        } catch (err) {
            console.error('Error updating profile:', err);
            showToast('Lỗi kết nối khi lưu Medical ID', 'error');
        }
    });
}

async function loadUserProfile() {
    if (!authToken) return;

    try {
        const res = await fetch(`${API_BASE}/users/me`, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });

        const result = await res.json();
        if (res.ok && result.success) {
            currentUser = result.data;
            updateUserBadge();
            populateMedicalFields(result.data);
        }
    } catch (err) {
        console.warn('Error loading user profile:', err);
    }
}

function populateMedicalFields(user) {
    if (!user) return;
    if (user.bloodType) document.getElementById('med-blood-type').value = user.bloodType;
    document.getElementById('med-notes').value = user.medicalNotes || '';
    document.getElementById('med-chronic').value = formatArrayForInput(user.chronicConditions);
    document.getElementById('med-allergies').value = formatArrayForInput(user.allergies);
    document.getElementById('med-medications').value = formatArrayForInput(user.currentMedications);
    document.getElementById('med-mobility').value = formatArrayForInput(user.mobilityLimitations);
    document.getElementById('med-language').value = user.preferredLanguage || 'Tiếng Việt';
    document.getElementById('med-members-count').value = user.householdMembersCount || 1;
    document.getElementById('med-dependents').value = user.dependentsNote || '';
    document.getElementById('med-auto-sms').checked = user.autoSendSmsOnSos !== false;
    document.getElementById('med-sms-template').value = user.smsTemplate || 'CẤP CỨU: Tôi vừa bấm SOS tại tọa độ {lat},{lng}. Cần hỗ trợ khẩn cấp!';
}

// ============================================================
// TAB 3: ICE CONTACTS CRUD
// ============================================================
function initIceContactsTab() {
    const formIce = document.getElementById('form-ice-contact');
    const btnRefresh = document.getElementById('btn-refresh-ice-list');
    const btnCancelEdit = document.getElementById('btn-cancel-edit-ice');

    btnRefresh.addEventListener('click', () => {
        loadIceContacts();
    });

    btnCancelEdit.addEventListener('click', () => {
        resetIceForm();
    });

    formIce.addEventListener('submit', async (e) => {
        e.preventDefault();

        if (!authToken) {
            showToast('Vui lòng đăng nhập trước!', 'error');
            return;
        }

        const iceId = document.getElementById('ice-contact-id').value;
        const payload = {
            name: document.getElementById('ice-name').value.trim(),
            phoneNumber: document.getElementById('ice-phone').value.trim(),
            relationship: document.getElementById('ice-relationship').value.trim(),
            isPrimary: document.getElementById('ice-is-primary').checked
        };

        try {
            const isEditing = Boolean(iceId);
            const url = isEditing ? `${API_BASE}/users/ice-contacts/${iceId}` : `${API_BASE}/users/ice-contacts`;
            const method = isEditing ? 'PUT' : 'POST';

            const res = await fetch(url, {
                method,
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${authToken}`
                },
                body: JSON.stringify(payload)
            });

            const result = await res.json();
            if (res.ok && result.success) {
                showToast(isEditing ? 'Cập nhật người thân thành công!' : 'Thêm người thân ICE thành công!', 'success');
                resetIceForm();
                loadIceContacts();
            } else {
                showToast('Lỗi: ' + (result.message || 'Không thể lưu người thân'), 'error');
            }
        } catch (err) {
            console.error('Error saving ICE contact:', err);
            showToast('Lỗi kết nối khi lưu người thân', 'error');
        }
    });
}

async function loadIceContacts() {
    const listContainer = document.getElementById('ice-contacts-list');
    if (!authToken) {
        listContainer.innerHTML = `<div class="empty-state">Vui lòng đăng nhập để xem danh bạ khẩn cấp ICE.</div>`;
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/users/ice-contacts`, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });

        const result = await res.json();
        if (res.ok && result.success) {
            renderIceContacts(result.data);
        } else {
            listContainer.innerHTML = `<div class="empty-state">Lỗi: ${result.message || 'Không thể tải danh sách'}</div>`;
        }
    } catch (err) {
        console.error('Error fetching ICE contacts:', err);
        listContainer.innerHTML = `<div class="empty-state">Lỗi kết nối server</div>`;
    }
}

function renderIceContacts(contacts) {
    const listContainer = document.getElementById('ice-contacts-list');
    if (!contacts || contacts.length === 0) {
        listContainer.innerHTML = `
            <div class="empty-state">
                Chưa có người thân nào trong danh bạ khẩn cấp.<br>
                Hãy dùng form bên cạnh để thêm người thân đầu tiên!
            </div>`;
        return;
    }

    listContainer.innerHTML = contacts.map(c => `
        <div class="ice-card ${c.isPrimary ? 'is-primary' : ''}">
            <div class="ice-info">
                <div class="ice-title-line">
                    <span class="ice-name">${c.name}</span>
                    ${c.isPrimary ? '<span class="ice-primary-badge">⭐ Người liên hệ chính</span>' : ''}
                </div>
                <div class="ice-rel">Mối quan hệ: ${c.relationship || 'Chưa rõ'}</div>
                <div class="ice-phone">📞 ${c.phoneNumber}</div>
            </div>
            <div class="ice-actions">
                ${!c.isPrimary ? `
                    <button class="btn btn-secondary btn-xs" onclick="setAsPrimary('${c.id}')" title="Đặt làm chính">
                        ⭐ Đặt làm chính
                    </button>
                ` : ''}
                <button class="btn btn-secondary btn-xs" onclick="editIceContact('${c.id}', '${escapeAttr(c.name)}', '${escapeAttr(c.phoneNumber)}', '${escapeAttr(c.relationship)}', ${c.isPrimary})">
                    ✏️ Sửa
                </button>
                <button class="btn btn-danger btn-xs" onclick="deleteIceContact('${c.id}')">
                    🗑️ Xóa
                </button>
            </div>
        </div>
    `).join('');
}

function escapeAttr(str) {
    if (!str) return '';
    return str.replace(/'/g, "\\'").replace(/"/g, '&quot;');
}

window.editIceContact = function(id, name, phone, relationship, isPrimary) {
    document.getElementById('ice-contact-id').value = id;
    document.getElementById('ice-name').value = name;
    document.getElementById('ice-phone').value = phone;
    document.getElementById('ice-relationship').value = relationship;
    document.getElementById('ice-is-primary').checked = isPrimary;

    document.getElementById('form-ice-title').textContent = 'Chỉnh Sửa Người Thân';
    document.getElementById('btn-submit-ice').textContent = '💾 CẬP NHẬT THÔNG TIN';
    document.getElementById('btn-cancel-edit-ice').classList.remove('hidden');
};

window.setAsPrimary = async function(id) {
    try {
        const res = await fetch(`${API_BASE}/users/ice-contacts/${id}`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${authToken}`
            },
            body: JSON.stringify({ isPrimary: true })
        });

        const result = await res.json();
        if (res.ok && result.success) {
            showToast('Đã đặt làm Người liên hệ chính!', 'success');
            loadIceContacts();
        } else {
            showToast('Lỗi: ' + (result.message || 'Không thể cập nhật'), 'error');
        }
    } catch (err) {
        showToast('Lỗi kết nối khi cập nhật', 'error');
    }
};

window.deleteIceContact = async function(id) {
    if (!confirm('Bạn có chắc chắn muốn xóa người thân này khỏi danh bạ khẩn cấp?')) {
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/users/ice-contacts/${id}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${authToken}` }
        });

        const result = await res.json();
        if (res.ok && result.success) {
            showToast('Đã xóa người thân khỏi danh bạ ICE!', 'info');
            loadIceContacts();
        } else {
            showToast('Lỗi xóa: ' + (result.message || 'Không thể xóa'), 'error');
        }
    } catch (err) {
        showToast('Lỗi kết nối khi xóa', 'error');
    }
};

function resetIceForm() {
    document.getElementById('ice-contact-id').value = '';
    document.getElementById('form-ice-contact').reset();
    document.getElementById('form-ice-title').textContent = 'Thêm Người Thân Mới';
    document.getElementById('btn-submit-ice').textContent = '➕ THÊM VÀO DANH BẠ ICE';
    document.getElementById('btn-cancel-edit-ice').classList.add('hidden');
}
