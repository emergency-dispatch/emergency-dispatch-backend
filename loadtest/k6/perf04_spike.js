// TC_F5_PERF_04 / V.2.1.12 — spike 0 → 500 người dùng ảo trong 10 giây gửi SOS có ảnh (đổ đầy hàng đợi AI),
// sau đó đo API đọc có trở lại bình thường trong 30 giây hay không. Kiểm tra mất báo cáo bằng SQL sau khi chạy (README).
import http from 'k6/http';
import exec from 'k6/execution';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE, login, authHeaders, postSos, vuIp } from './lib.js';

const accepted = new Counter('sos_accepted');
const rejected = new Counter('sos_rejected');

export const options = {
  scenarios: {
    spike: {
      executor: 'ramping-vus', startVUs: 0,
      stages: [{ duration: '10s', target: 500 }, { duration: '20s', target: 500 }, { duration: '5s', target: 0 }],
      gracefulRampDown: '30s', exec: 'spike',
    },
    // Operator làm mới danh sách mỗi giây: trong spike và 60 giây sau spike
    probe: { executor: 'constant-arrival-rate', rate: 1, timeUnit: '1s', duration: '100s', preAllocatedVUs: 5, exec: 'probe' },
  },
  // Ngưỡng theo giai đoạn để k6 in riêng số liệu từng giai đoạn trong summary
  thresholds: {
    'http_req_duration{name:POST /api/incidents/sos (spike)}': ['p(95)<5000'],
    'http_req_duration{phase:during}': ['max>=0'],
    'http_req_duration{phase:after_0_30s}': ['max>=0'],
    'http_req_duration{phase:after_30_60s}': ['p(95)<300'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { token: login() };
}

export function spike() {
  const res = postSos(true, vuIp(__VU, 300 + __ITER), 'POST /api/incidents/sos (spike)');
  if (check(res, { 'sos 200': (r) => r.status === 200 || r.status === 201 })) accepted.add(1);
  else rejected.add(1);
  sleep(1);
}

function phase() {
  // probe gửi 1 request / giây nên số thứ tự lần lặp ≈ số giây kể từ lúc bắt đầu; spike kết thúc ở giây 35
  const t = exec.scenario.iterationInTest;
  if (t < 40) return 'during';
  if (t < 70) return 'after_0_30s';
  return 'after_30_60s';
}

export function probe(data) {
  const res = http.get(`${BASE}/api/incidents?page=1&pageSize=20`,
    Object.assign({ tags: { name: 'GET /api/incidents (probe)', phase: phase() } }, authHeaders(data.token)));
  check(res, { 'probe 200': (r) => r.status === 200 });
}
