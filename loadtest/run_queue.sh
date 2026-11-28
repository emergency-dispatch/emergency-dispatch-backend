#!/usr/bin/env bash
# Chạy riêng bài spike và bài đầy hàng đợi AI (bắt đầu từ trạng thái DB hiện tại)
set -uo pipefail
cd "$(dirname "$0")/.."
C="docker compose -f loadtest/docker-compose.yml"
psqlq() { docker exec ed-loadtest-db-1 psql -U postgres -d loadtest -tAc "$1"; }
token() { curl -s -X POST localhost:5080/api/auth/login -H "Content-Type: application/json" -H "X-Forwarded-For: 10.250.0.9" \
  -d '{"email":"admin@gmail.com","password":"123123"}' | python -c "import sys,json;print(json.load(sys.stdin)['data']['accessToken'])"; }
wait_api() { for _ in $(seq 1 60); do curl -sf -o /dev/null localhost:5080/api/localization/info && return 0; sleep 2; done; return 1; }
dup() { curl -s -X PUT -H "Authorization: Bearer $(token)" -H "Content-Type: application/json" localhost:5080/api/settings/duplicate-detection \
  -d "{\"enabled\":$1,\"radiusMeters\":200,\"windowMinutes\":15}" > /dev/null; }
out=loadtest/results/rerun2_counts.txt
dup false
B=$(psqlq 'select count(*) from "Incidents"'); bash loadtest/run.sh perf04_spike > /dev/null 2>&1; sleep 30
echo "spike: before=$B after=$(psqlq 'select count(*) from "Incidents"')" | tee $out
sleep 200   # cho cửa sổ BR-CIT-01 (3 phút) trôi qua
node loadtest/signalr/slow_ai.mjs > loadtest/results/slow_ai2.log 2>&1 &
SLOW=$!
AI_URL=http://host.docker.internal:5099 $C up -d > /dev/null 2>&1; wait_api; dup false
B=$(psqlq 'select count(*) from "Incidents"')
( for _ in $(seq 1 90); do echo "$(psqlq "select now()::time(0), count(*) filter (where \"Status\"=1), count(*) from \"Incidents\"")"; sleep 10; done ) > loadtest/results/rerun2_queue_depth.txt &
DEPTH=$!
bash loadtest/run.sh perf04_queue > /dev/null 2>&1
wait $DEPTH; kill $SLOW 2>/dev/null
echo "queue: before=$B after=$(psqlq 'select count(*) from "Incidents"') aiprocessing=$(psqlq 'select count(*) from "Incidents" where "Status"=1')" | tee -a $out
$C up -d > /dev/null 2>&1; wait_api; dup true
