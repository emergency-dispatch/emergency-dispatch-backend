// Hàm dùng chung cho các kịch bản k6 (load test local, xem loadtest/README.md)
import http from 'k6/http';
import { check, fail } from 'k6';

export const BASE = __ENV.BASE_URL || 'http://api:8080';
const ADMIN_EMAIL = __ENV.ADMIN_EMAIL || 'admin@gmail.com';
const ADMIN_PASSWORD = __ENV.ADMIN_PASSWORD || '123123';

// Khu CNC TP.HCM (Quận 9 cũ) — tâm và bán kính rải điểm báo cáo
const CENTER = { lat: 10.8411, lng: 106.8098 };

export function login() {
  const res = http.post(`${BASE}/api/auth/login`, JSON.stringify({ email: ADMIN_EMAIL, password: ADMIN_PASSWORD }), {
    headers: { 'Content-Type': 'application/json', 'X-Forwarded-For': '10.250.0.1' },
    tags: { name: 'login' },
  });
  if (!check(res, { 'login 200': (r) => r.status === 200 })) {
    fail(`login failed: ${res.status} ${res.body}`);
  }
  return res.json('data.accessToken');
}

export function authHeaders(token, extra = {}) {
  return { headers: Object.assign({ Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, extra) };
}

// Mỗi người dùng ảo một IP riêng (proxy chuyển tiếp X-Forwarded-For) — giống người dân gửi từ nhiều điện thoại
export function vuIp(vu, salt = 0) {
  const n = vu + salt * 10000;
  return `10.${(n >> 16) & 255}.${(n >> 8) & 255}.${(n & 255) || 1}`;
}

export function randomPoint(radiusKm = 4) {
  const r = (radiusKm / 111) * Math.sqrt(Math.random());
  const t = 2 * Math.PI * Math.random();
  return { lat: CENTER.lat + r * Math.cos(t), lng: CENTER.lng + (r * Math.sin(t)) / Math.cos((CENTER.lat * Math.PI) / 180) };
}

let seq = 0;
export function sosBody(withPhoto) {
  const p = randomPoint();
  seq += 1;
  const body = {
    title: 'Load test SOS',
    description: 'k6 load test — not a real incident',
    latitude: p.lat,
    longitude: p.lng,
    locationAddress: 'Khu Công nghệ cao, TP. Thủ Đức (load test)',
    reporterName: 'k6',
    // Số ngẫu nhiên mỗi lần gửi: BR-CIT-01 chặn cùng một số gửi lần 2 trong 3 phút, kịch bản chạy liền nhau không được trùng số
    reporterPhone: `09${String(Math.floor(Math.random() * 1e8)).padStart(8, '0')}`,
    reportedEventType: Math.random() < 0.5 ? 'FireExplosion' : 'TrafficAccident',
    idempotencyKey: `k6-${__VU}-${__ITER}-${seq}-${Date.now()}`,
    mediaUrls: [],
  };
  if (withPhoto) {
    // Chế độ mock Cloudinary: URL images.unsplash.com hợp lệ, AI worker được kích hoạt
    body.mediaUrls = [`https://images.unsplash.com/photo-1500000000000-k6${__VU}x${seq}`];
  }
  return JSON.stringify(body);
}

export function postSos(withPhoto, ip, tagName) {
  return http.post(`${BASE}/api/incidents/sos`, sosBody(withPhoto), {
    headers: { 'Content-Type': 'application/json', 'X-Forwarded-For': ip },
    tags: { name: tagName || (withPhoto ? 'POST /api/incidents/sos (photo)' : 'POST /api/incidents/sos') },
  });
}
