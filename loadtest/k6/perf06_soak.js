// TC_F5_PERF_06 — chạy bền 50 người dùng ảo trong 60 phút (Operator đọc + 1 SOS mỗi 10 giây),
// RAM / CPU container lấy từ docker_stats.csv do run.sh ghi
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, login, authHeaders, postSos, vuIp } from './lib.js';

export const options = {
  scenarios: {
    operators: { executor: 'constant-vus', vus: 50, duration: __ENV.DURATION || '60m', exec: 'operator' },
    citizens: { executor: 'constant-arrival-rate', rate: 1, timeUnit: '10s', duration: __ENV.DURATION || '60m', preAllocatedVUs: 5, exec: 'citizen' },
  },
  thresholds: {
    'http_req_duration{expected_response:true}': ['p(95)<300'],
    http_req_failed: ['rate<0.005'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { token: login() };
}

export function operator(data) {
  const h = authHeaders(data.token);
  check(http.get(`${BASE}/api/incidents?page=1&pageSize=20`, Object.assign({ tags: { name: 'GET /api/incidents' } }, h)),
    { 'incidents 200': (r) => r.status === 200 });
  check(http.get(`${BASE}/api/incidents/queue`, Object.assign({ tags: { name: 'GET /api/incidents/queue' } }, h)),
    { 'queue 200': (r) => r.status === 200 });
  sleep(3);
}

export function citizen() {
  check(postSos(__ITER % 2 === 0, vuIp(__VU, 2000 + __ITER)), { 'sos 200': (r) => r.status === 200 || r.status === 201 });
}
