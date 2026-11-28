// TC_F5_PERF_01 — độ trễ API đọc của Operator dưới tải bậc thang 20 / 100 / 200 VU, mỗi bậc 5 phút
import http from 'k6/http';
import exec from 'k6/execution';
import { check, sleep } from 'k6';
import { BASE, login, authHeaders } from './lib.js';

export const options = {
  scenarios: {
    operators: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 20 }, { duration: '5m', target: 20 },
        { duration: '30s', target: 100 }, { duration: '5m', target: 100 },
        { duration: '30s', target: 200 }, { duration: '5m', target: 200 },
        { duration: '30s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    'http_req_duration{expected_response:true}': ['p(95)<300'],
    http_req_failed: ['rate<0.005'],
    'http_req_duration{name:GET /api/incidents,stage:vu20}': ['p(95)<300'],
    'http_req_duration{name:GET /api/incidents,stage:vu100}': ['p(95)<300'],
    'http_req_duration{name:GET /api/incidents,stage:vu200}': ['p(95)<300'],
    'http_req_duration{name:GET /api/incidents/queue,stage:vu20}': ['p(95)<300'],
    'http_req_duration{name:GET /api/incidents/queue,stage:vu100}': ['p(95)<300'],
    'http_req_duration{name:GET /api/incidents/queue,stage:vu200}': ['p(95)<300'],
    'http_req_duration{name:GET /api/stations,stage:vu20}': ['p(95)<300'],
    'http_req_duration{name:GET /api/stations,stage:vu100}': ['p(95)<300'],
    'http_req_duration{name:GET /api/stations,stage:vu200}': ['p(95)<300'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { token: login() };
}

// Bậc tải theo thời gian chạy (bỏ các đoạn tăng / giảm 30 giây)
function stage() {
  const t = exec.instance.currentTestRunDuration / 1000;
  if (t >= 30 && t < 330) return 'vu20';
  if (t >= 360 && t < 660) return 'vu100';
  if (t >= 690 && t < 990) return 'vu200';
  return 'ramp';
}

export default function (data) {
  const st = stage();
  const h = authHeaders(data.token);
  const r1 = http.get(`${BASE}/api/incidents?page=1&pageSize=20`, Object.assign({ tags: { name: 'GET /api/incidents', stage: st } }, h));
  check(r1, { 'incidents 200': (r) => r.status === 200 });
  const r2 = http.get(`${BASE}/api/incidents/queue`, Object.assign({ tags: { name: 'GET /api/incidents/queue', stage: st } }, h));
  check(r2, { 'queue 200': (r) => r.status === 200 });
  const r3 = http.get(`${BASE}/api/stations`, Object.assign({ tags: { name: 'GET /api/stations', stage: st } }, h));
  check(r3, { 'stations 200': (r) => r.status === 200 });
  // Operator làm mới màn hình khoảng 3 giây một lần
  sleep(3);
}
