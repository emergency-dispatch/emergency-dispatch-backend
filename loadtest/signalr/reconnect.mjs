// V.2.1.8 Connection Resilience — khởi động lại container API giữa phiên và đo thời gian các kết nối SignalR
// tự nối lại (withAutomaticReconnect giống web) tính từ lúc API sẵn sàng trở lại.
//   node loadtest/signalr/reconnect.mjs   (RECEIVERS, OUT_DIR)
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const require = createRequire(import.meta.url);
const signalR = require(process.env.SIGNALR_PATH || 'D:/SEP490/emergency-dispatch-frontend/node_modules/@microsoft/signalr');
const BASE = process.env.BASE_URL || 'http://localhost:5080';
const RECEIVERS = Number(process.env.RECEIVERS || 50);
const OUT_DIR = process.env.OUT_DIR || `loadtest/results/reconnect_${new Date().toISOString().replace(/[-:]/g, '').slice(0, 15)}`;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function login() {
  const res = await fetch(`${BASE}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Forwarded-For': '10.250.0.3' },
    body: JSON.stringify({ email: 'admin@gmail.com', password: '123123' }) });
  return (await res.json()).data.accessToken;
}

async function main() {
  mkdirSync(OUT_DIR, { recursive: true });
  const token = await login();
  const conns = [];
  const reconnectedAt = [];
  let closedForGood = 0;
  for (let i = 0; i < RECEIVERS; i++) {
    const c = new signalR.HubConnectionBuilder()
      .withUrl(`${BASE}/hubs/tracking`, { accessTokenFactory: () => token, transport: signalR.HttpTransportType.WebSockets, skipNegotiation: true })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.None).build();
    c.onreconnected(() => reconnectedAt.push(Date.now()));
    c.onclose(() => { closedForGood++; });
    await c.start();
    conns.push(c);
  }
  console.log(`${conns.length} connections open; restarting API container`);
  const tRestart = Date.now();
  execSync('docker restart ed-loadtest-api-1', { stdio: 'ignore' });
  // API sẵn sàng = endpoint công khai trả 200
  let tReady = null;
  while (!tReady) {
    try { const r = await fetch(`${BASE}/api/localization/info`); if (r.ok) tReady = Date.now(); } catch { /* chưa lên */ }
    if (!tReady) await sleep(200);
  }
  const deadline = Date.now() + 30000;
  while (reconnectedAt.length + closedForGood < RECEIVERS && Date.now() < deadline) await sleep(200);
  const delays = reconnectedAt.map((t) => (t - tReady) / 1000).sort((a, b) => a - b);
  const summary = {
    receivers: RECEIVERS,
    apiDowntimeS: (tReady - tRestart) / 1000,
    reconnected: reconnectedAt.length,
    gaveUp: closedForGood,
    reconnectAfterReadyS: { min: delays[0] ?? null, median: delays[Math.floor(delays.length / 2)] ?? null, max: delays[delays.length - 1] ?? null },
    within5s: delays.filter((d) => d <= 5).length,
  };
  writeFileSync(path.join(OUT_DIR, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify(summary, null, 2));
  await Promise.all(conns.map((c) => c.stop().catch(() => {})));
  process.exit(0);
}

main().catch((e) => { console.error(e); process.exit(1); });
