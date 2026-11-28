// AI giả chậm cho bài đầy hàng đợi (TC_F5_PERF_04): mỗi request chờ DELAY_MS rồi trả 503,
// để worker đi đường dự phòng (OpenRouter bị chặn → circuit breaker → Level 0). Không gọi AI thật.
//   DELAY_MS=2000 PORT=5099 node loadtest/signalr/slow_ai.mjs
import http from 'node:http';
const DELAY = Number(process.env.DELAY_MS || 2000);
let n = 0;
http.createServer((req, res) => {
  n += 1;
  req.resume();
  setTimeout(() => { res.writeHead(503, { 'Content-Type': 'application/json' }); res.end('{"error":"simulated slow AI"}'); }, DELAY);
}).listen(Number(process.env.PORT || 5099), () => console.log(`slow AI on :${process.env.PORT || 5099}, delay ${DELAY} ms`));
setInterval(() => console.log(`${new Date().toISOString()} requests=${n}`), 30000);
