#!/usr/bin/env bash
# Chạy lại toàn bộ bộ load test trên DB sạch (khoảng 1 giờ 50 phút). Kết quả: loadtest/results/
#   bash loadtest/run_all.sh
set -uo pipefail
cd "$(dirname "$0")/.."
C="docker compose -f loadtest/docker-compose.yml"
psqlq() { docker exec ed-loadtest-db-1 psql -U postgres -d loadtest -tAc "$1"; }
token() { curl -s -X POST localhost:5080/api/auth/login -H "Content-Type: application/json" -H "X-Forwarded-For: 10.250.0.9" \
  -d '{"email":"admin@gmail.com","password":"123123"}' | python -c "import sys,json;print(json.load(sys.stdin)['data']['accessToken'])"; }
wait_api() { for _ in $(seq 1 60); do curl -sf -o /dev/null localhost:5080/api/localization/info && return 0; sleep 2; done; return 1; }
dup() { curl -s -X PUT -H "Authorization: Bearer $(token)" -H "Content-Type: application/json" localhost:5080/api/settings/duplicate-detection \
  -d "{\"enabled\":$1,\"radiusMeters\":200,\"windowMinutes\":15}" > /dev/null; }
log() { echo "[$(date +%T)] $*"; }

log "rebuild + reset DB"
$C down -v > /dev/null 2>&1
$C up -d --build > /dev/null 2>&1
wait_api || { log "API not ready"; exit 1; }
MSYS_NO_PATHCONV=1 docker run --rm --network ed-loadtest_default -v "$(pwd -W 2>/dev/null || pwd)/loadtest:/lt" -e INCIDENTS=600 \
  grafana/k6:0.54.0 run --quiet /lt/k6/seed.js 2>&1 | grep -E "stations created|http_req_failed"

log "PERF_01 read";   bash loadtest/run.sh perf01_read  > /dev/null 2>&1
log "PERF_02 sos";    bash loadtest/run.sh perf02_sos   > /dev/null 2>&1
log "rate limit";     bash loadtest/run.sh ratelimit    > /dev/null 2>&1

log "spike (duplicate detection off)"
dup false
B=$(psqlq 'select count(*) from "Incidents"'); bash loadtest/run.sh perf04_spike > /dev/null 2>&1; sleep 30
echo "spike: before=$B after=$(psqlq 'select count(*) from "Incidents"')" | tee loadtest/results/rerun_counts.txt

log "AI queue full (slow AI 2 s)"
node loadtest/signalr/slow_ai.mjs > loadtest/results/slow_ai.log 2>&1 &
SLOW=$!
AI_URL=http://host.docker.internal:5099 $C up -d > /dev/null 2>&1; wait_api; dup false
B=$(psqlq 'select count(*) from "Incidents"')
( for _ in $(seq 1 90); do echo "$(psqlq "select now()::time(0), count(*) filter (where \"Status\"=1), count(*) from \"Incidents\"")"; sleep 10; done ) \
  > loadtest/results/rerun_queue_depth.txt &
DEPTH=$!
bash loadtest/run.sh perf04_queue > /dev/null 2>&1
wait $DEPTH; kill $SLOW 2>/dev/null
echo "queue: before=$B after=$(psqlq 'select count(*) from "Incidents"') aiprocessing=$(psqlq 'select count(*) from "Incidents" where "Status"=1')" | tee -a loadtest/results/rerun_counts.txt

log "soak 60 min (DB now holds all spike incidents)"
$C up -d > /dev/null 2>&1; wait_api; dup true
echo "soak start: pending queue=$(psqlq 'select count(*) from "Incidents" where "Status" in (0,1,2) or ("Status"=8 and "VerifiedAt" is null)')" | tee -a loadtest/results/rerun_counts.txt
bash loadtest/run.sh perf06_soak > /dev/null 2>&1
docker inspect ed-loadtest-api-1 --format "api after soak: running={{.State.Running}} restarts={{.RestartCount}} oom={{.State.OOMKilled}}" | tee -a loadtest/results/rerun_counts.txt
log "done"
