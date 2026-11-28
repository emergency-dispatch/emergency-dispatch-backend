// Tạo dữ liệu nền cho load test: 3 trạm, 50 xe, ~600 sự cố (DB local trống, chỉ có admin bootstrap)
//   docker run --rm --network ed-loadtest_default -v ./loadtest:/lt grafana/k6:0.54.0 run /lt/k6/seed.js
import http from 'k6/http';
import { check } from 'k6';
import { BASE, login, authHeaders, postSos, vuIp } from './lib.js';

export const options = {
  scenarios: {
    incidents: { executor: 'shared-iterations', vus: Math.min(20, Number(__ENV.INCIDENTS || 600)), iterations: Number(__ENV.INCIDENTS || 600), maxDuration: '10m' },
  },
};

const STATIONS = [
  { code: 'LT-ST1', name: 'Load test station 1', lat: 10.8450, lng: 106.7950 },
  { code: 'LT-ST2', name: 'Load test station 2', lat: 10.8350, lng: 106.8200 },
  { code: 'LT-ST3', name: 'Load test station 3', lat: 10.8550, lng: 106.8150 },
];

export function setup() {
  const token = login();
  const existing = http.get(`${BASE}/api/stations?pageSize=100`, authHeaders(token));
  const body = existing.body || '';
  const stationIds = [];
  for (const s of STATIONS) {
    if (body.includes(s.code)) continue;
    const res = http.post(`${BASE}/api/stations`, JSON.stringify({
      code: s.code, name: s.name, address: `${s.name}, Khu CNC`, latitude: s.lat, longitude: s.lng,
      phoneNumber: '0281234567', isActive: true, coverageRadiusKm: 10,
    }), authHeaders(token));
    check(res, { 'station created': (r) => r.status === 200 || r.status === 201 });
    const id = res.json('data.id');
    if (id) stationIds.push(id);
  }
  // 50 xe chia đều cho các trạm mới tạo (dùng cho kịch bản SignalR GPS)
  let created = 0;
  stationIds.forEach((sid, si) => {
    const n = si === 0 ? 18 : 16;
    for (let i = 0; i < n; i++) {
      const res = http.post(`${BASE}/api/rescue-units`, JSON.stringify({
        plateNumber: `LT-${si + 1}-${String(i + 1).padStart(2, '0')}`,
        unitType: i % 2 === 0 ? 'FireTruck' : 'Ambulance',
        stationId: sid, initialLat: STATIONS[si].lat, initialLng: STATIONS[si].lng,
      }), authHeaders(token));
      if (check(res, { 'unit created': (r) => r.status === 200 || r.status === 201 })) created++;
    }
  });
  console.log(`stations created: ${stationIds.length}, units created: ${created}`);
}

export default function () {
  // 1/3 có ảnh (đi qua AI worker → lỗi → Level 0), 2/3 không ảnh (Pending)
  const res = postSos(__ITER % 3 === 0, vuIp(__VU, 7 + (__ITER % 50)), 'seed sos');
  if (!check(res, { 'sos accepted': (r) => r.status === 200 || r.status === 201 })) {
    console.warn(`sos rejected: ${res.status} ${String(res.body).slice(0, 300)}`);
  }
}
