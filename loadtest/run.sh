#!/usr/bin/env bash
# Chạy một kịch bản k6 và ghi kết quả + mẫu docker stats vào loadtest/results/<tên>_<thời điểm>/
#   bash loadtest/run.sh perf01_read [biến=giá_trị ...]
set -euo pipefail
cd "$(dirname "$0")/.."
name="$1"; shift
stamp=$(date +%Y%m%d_%H%M%S)
out="loadtest/results/${name}_${stamp}"
mkdir -p "$out"
envargs=()
for kv in "$@"; do envargs+=(-e "$kv"); done
# Lấy mẫu CPU / RAM của container API và DB mỗi 10 giây trong lúc chạy
( while true; do docker stats --no-stream --format "$(date -u +%FT%TZ),{{.Name}},{{.CPUPerc}},{{.MemUsage}}" ed-loadtest-api-1 ed-loadtest-db-1 >> "$out/docker_stats.csv" 2>/dev/null || true; sleep 10; done ) &
sampler=$!
trap 'kill $sampler 2>/dev/null || true' EXIT
MSYS_NO_PATHCONV=1 docker run --rm --network ed-loadtest_default -v "$(pwd -W 2>/dev/null || pwd)/loadtest:/lt" "${envargs[@]}" \
  grafana/k6:0.54.0 run --summary-export "/lt/results/${name}_${stamp}/summary.json" "/lt/k6/${name}.js" 2>&1 | tee "$out/k6_output.txt"
docker logs --since 30m ed-loadtest-api-1 > "$out/api_log_tail.txt" 2>&1 || true
echo "results -> $out"
