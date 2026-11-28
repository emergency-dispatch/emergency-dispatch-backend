// TC_F5_PERF_04 (đầy hàng đợi) — 800 SOS có ảnh gửi dồn dập trong khi AI giả xử lý 2 giây / ảnh (3 job song song).
// Hàng đợi AI (500) đầy → SOS phải chờ chỗ trống (FullMode Wait). Đo độ trễ SOS và kiểm tra không mất báo cáo.
import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE, login, authHeaders, sosBody, vuIp } from './lib.js';

const accepted = new Counter('sos_accepted');
const rejected = new Counter('sos_rejected');

export const options = {
  scenarios: {
    burst: { executor: 'shared-iterations', vus: 100, iterations: Number(__ENV.N || 800), maxDuration: '20m', exec: 'burst' },
    probe: { executor: 'constant-arrival-rate', rate: 1, timeUnit: '2s', duration: __ENV.PROBE || '6m', preAllocatedVUs: 3, exec: 'probe' },
  },
  thresholds: {
    'http_req_duration{name:POST /api/incidents/sos (queue full)}': ['p(95)<1500'],
    'http_req_duration{name:GET /api/incidents (probe)}': ['p(95)<300'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { token: login() };
}

export function burst() {
  const res = http.post(`${BASE}/api/incidents/sos`, sosBody(true), {
    headers: { 'Content-Type': 'application/json', 'X-Forwarded-For': vuIp(__VU, 5000 + exec.scenario.iterationInTest) },
    tags: { name: 'POST /api/incidents/sos (queue full)' },
    timeout: '600s',
  });
  if (check(res, { 'sos 200': (r) => r.status === 200 || r.status === 201 })) accepted.add(1);
  else rejected.add(1);
}

export function probe(data) {
  const res = http.get(`${BASE}/api/incidents?page=1&pageSize=20`,
    Object.assign({ tags: { name: 'GET /api/incidents (probe)' } }, authHeaders(data.token)));
  check(res, { 'probe 200': (r) => r.status === 200 });
}
