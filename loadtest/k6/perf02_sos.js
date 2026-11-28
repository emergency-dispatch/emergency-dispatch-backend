// TC_F5_PERF_02 — độ trễ tạo SOS (có / không ảnh) với 2 SOS mỗi giây trong 5 phút, mỗi lần gửi một IP khác
import { check } from 'k6';
import { postSos, vuIp } from './lib.js';

export const options = {
  scenarios: {
    sos: {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.RATE || 2), timeUnit: '1s', duration: __ENV.DURATION || '5m',
      preAllocatedVUs: 20, maxVUs: 100,
    },
  },
  thresholds: {
    'http_req_duration{expected_response:true}': ['p(95)<1500'],
    http_req_failed: ['rate<0.005'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  const withPhoto = __ITER % 2 === 0;
  const res = postSos(withPhoto, vuIp(__VU, 100 + (__ITER % 200)));
  check(res, { 'sos 200': (r) => r.status === 200 || r.status === 201 });
}
