// V.2.1.12 — cùng một IP gửi 10 SOS liên tiếp: 5 lần đầu được nhận, các lần sau phải nhận HTTP 429
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { postSos } from './lib.js';

const ok = new Counter('sos_200');
const limited = new Counter('sos_429');

export const options = { vus: 1, iterations: 10 };

export default function () {
  const res = postSos(false, '10.254.254.254', 'POST /api/incidents/sos (single IP)');
  if (res.status === 429) limited.add(1);
  else if (res.status === 200 || res.status === 201) ok.add(1);
  console.log(`request ${__ITER + 1}: HTTP ${res.status}`);
  check(res, { 'first 5 accepted, rest 429': (r) => (__ITER < 5 ? r.status === 200 || r.status === 201 : r.status === 429) });
}
