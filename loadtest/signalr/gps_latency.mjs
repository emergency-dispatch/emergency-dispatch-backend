// V.2.1.8 / V.2.1.13 — đo độ trễ GPS qua SignalR (/hubs/tracking) và độ bền kết nối.
// S kết nối "xe" gửi UpdateLocation mỗi INTERVAL ms; R kết nối "Operator" nhận ReceiveRescueUnitLocation.
// Độ trễ = lúc Operator nhận − lúc xe gửi (cùng một tiến trình, cùng đồng hồ).
//   node loadtest/signalr/gps_latency.mjs  (biến môi trường: BASE_URL, RECEIVERS, SENDERS, INTERVAL_MS, DURATION_S, OUT_DIR)
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const require = createRequire(import.meta.url);
const signalR = require(process.env.SIGNALR_PATH || 'D:/SEP490/emergency-dispatch-frontend/node_modules/@microsoft/signalr');

const BASE = process.env.BASE_URL || 'http://localhost:5080';
const RECEIVERS = Number(process.env.RECEIVERS || 200);
const SENDERS = Number(process.env.SENDERS || 50);
const INTERVAL_MS = Number(process.env.INTERVAL_MS || 3000);
const DURATION_S = Number(process.env.DURATION_S || 600);
const OUT_DIR = process.env.OUT_DIR || `loadtest/results/signalr_${new Date().toISOString().replace(/[-:]/g, '').slice(0, 15)}`;

async function api(pathname, opts = {}) {
  const res = await fetch(`${BASE}${pathname}`, { ...opts, headers: { 'Content-Type': 'application/json', ...(opts.headers || {}) } });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(`${pathname}: HTTP ${res.status} ${JSON.stringify(body).slice(0, 200)}`);
  return body;
}

function connect(token) {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${BASE}/hubs/tracking`, { accessTokenFactory: () => token, transport: signalR.HttpTransportType.WebSockets, skipNegotiation: true })
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000]) // giống web (incidentHubService.ts)
    .configureLogging(signalR.LogLevel.None)
    .build();
}

function pct(sorted, p) {
  if (!sorted.length) return null;
  return sorted[Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)];
}

const stats = { sent: 0, sendErrors: 0, delivered: 0, duplicates: 0, outOfOrder: 0, reconnecting: 0, reconnected: 0, closed: 0, ackMs: [] };
const latencies = [];
const perMinute = new Map();
const sendTimes = new Map(); // `${unitId}:${seq}` -> thời điểm gửi (ms)
const lastSeq = []; // receiver index -> Map(unitId -> seq)

const BASE_LAT = 10.80;
let stopping = false;

async function main() {
  mkdirSync(OUT_DIR, { recursive: true });
  const login = await api('/api/auth/login', { method: 'POST', body: JSON.stringify({ email: process.env.ADMIN_EMAIL || 'admin@gmail.com', password: process.env.ADMIN_PASSWORD || '123123' }), headers: { 'X-Forwarded-For': '10.250.0.2' } });
  const token = login.data.accessToken;
  const units = await api('/api/rescue-units?page=1&pageSize=200', { headers: { Authorization: `Bearer ${token}` } });
  const list = (units.data.items || units.data).filter((u) => String(u.plateNumber).startsWith('LT-')).slice(0, SENDERS);
  if (list.length < SENDERS) throw new Error(`need ${SENDERS} load-test units, found ${list.length} (run k6/seed.js)`);

  const receivers = [];
  for (let i = 0; i < RECEIVERS; i++) {
    const c = connect(token);
    lastSeq[i] = new Map();
    c.on('ReceiveRescueUnitLocation', (loc) => {
      const now = performance.now();
      const seq = Math.round((loc.latitude - BASE_LAT) * 1e6);
      const t0 = sendTimes.get(`${loc.rescueUnitId}:${seq}`);
      if (t0 === undefined) return; // tin của lượt khác / vị trí khởi tạo
      const prev = lastSeq[i].get(loc.rescueUnitId);
      if (prev !== undefined && seq === prev) { stats.duplicates++; return; }
      if (prev !== undefined && seq < prev) stats.outOfOrder++;
      lastSeq[i].set(loc.rescueUnitId, seq);
      const ms = now - t0;
      latencies.push(ms);
      stats.delivered++;
      const minute = Math.floor((Date.now() - startedAt) / 60000);
      if (!perMinute.has(minute)) perMinute.set(minute, []);
      perMinute.get(minute).push(ms);
    });
    c.onreconnecting(() => { stats.reconnecting++; });
    c.onreconnected(() => { stats.reconnected++; });
    c.onclose(() => { if (!stopping) stats.closed++; });
    receivers.push(c);
  }
  // Mở kết nối theo từng đợt 20 để không dồn handshake
  for (let i = 0; i < receivers.length; i += 20) await Promise.all(receivers.slice(i, i + 20).map((c) => c.start()));

  const senders = [];
  for (const u of list) {
    const c = connect(token);
    await c.start();
    senders.push({ c, unit: u, seq: 0 });
  }
  console.log(`connected: ${receivers.length} receivers + ${senders.length} senders; running ${DURATION_S}s, interval ${INTERVAL_MS} ms`);

  var startedAt = Date.now();
  const endAt = startedAt + DURATION_S * 1000;
  const timers = senders.map((s, idx) => setTimeout(function tick() {
    if (Date.now() >= endAt) return;
    s.seq += 1;
    const lat = BASE_LAT + s.seq * 1e-6;
    sendTimes.set(`${s.unit.id}:${s.seq}`, performance.now());
    const t0 = performance.now();
    stats.sent++;
    s.c.invoke('UpdateLocation', { rescueUnitId: s.unit.id, latitude: lat, longitude: 106.80, headingDegrees: 90, speedKmh: 30 })
      .then(() => stats.ackMs.push(performance.now() - t0))
      .catch((e) => { stats.sendErrors++; if (stats.sendErrors <= 5) console.error('send error', e.message); });
    s.timer = setTimeout(tick, INTERVAL_MS);
  }, Math.floor((idx * INTERVAL_MS) / senders.length)));

  const progress = setInterval(() => {
    const mem = process.memoryUsage().rss / 1048576;
    console.log(`${Math.round((Date.now() - startedAt) / 1000)}s sent=${stats.sent} delivered=${stats.delivered} reconnecting=${stats.reconnecting} closed=${stats.closed} rss=${mem.toFixed(0)}MB`);
  }, 60000);

  await new Promise((r) => setTimeout(r, DURATION_S * 1000 + 5000)); // chờ tin cuối tới nơi
  clearInterval(progress);
  timers.forEach(clearTimeout);
  senders.forEach((s) => clearTimeout(s.timer));
  stopping = true;
  const open = receivers.filter((c) => c.state === signalR.HubConnectionState.Connected).length;
  await Promise.all([...receivers, ...senders.map((s) => s.c)].map((c) => c.stop().catch(() => {})));

  latencies.sort((a, b) => a - b);
  stats.ackMs.sort((a, b) => a - b);
  const expected = (stats.sent - stats.sendErrors) * RECEIVERS;
  const summary = {
    startedAtUtc: new Date(startedAt).toISOString(),
    config: { base: BASE, receivers: RECEIVERS, senders: SENDERS, intervalMs: INTERVAL_MS, durationS: DURATION_S },
    messages: { sent: stats.sent, sendErrors: stats.sendErrors, expectedDeliveries: expected, delivered: stats.delivered,
      lost: expected - stats.delivered, duplicates: stats.duplicates, outOfOrder: stats.outOfOrder },
    latencyMs: { avg: latencies.reduce((a, b) => a + b, 0) / (latencies.length || 1), p50: pct(latencies, 50), p95: pct(latencies, 95),
      p99: pct(latencies, 99), max: latencies[latencies.length - 1] ?? null, over1000: latencies.filter((x) => x > 1000).length,
      over3000: latencies.filter((x) => x > 3000).length },
    invokeAckMs: { p50: pct(stats.ackMs, 50), p95: pct(stats.ackMs, 95), max: stats.ackMs[stats.ackMs.length - 1] ?? null },
    connections: { receiversOpenAtEnd: open, reconnecting: stats.reconnecting, reconnected: stats.reconnected, closed: stats.closed },
  };
  writeFileSync(path.join(OUT_DIR, 'summary.json'), JSON.stringify(summary, null, 2));
  const rows = ['minute,deliveries,p50_ms,p95_ms,max_ms'];
  for (const [m, arr] of [...perMinute.entries()].sort((a, b) => a[0] - b[0])) {
    arr.sort((a, b) => a - b);
    rows.push([m, arr.length, pct(arr, 50).toFixed(1), pct(arr, 95).toFixed(1), arr[arr.length - 1].toFixed(1)].join(','));
  }
  writeFileSync(path.join(OUT_DIR, 'per_minute.csv'), rows.join('\n') + '\n');
  console.log(JSON.stringify(summary, null, 2));
  console.log(`results -> ${OUT_DIR}`);
  process.exit(0);
}

main().catch((e) => { console.error(e); process.exit(1); });
