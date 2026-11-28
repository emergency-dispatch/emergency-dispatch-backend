# Load test (k6 + SignalR) — môi trường local cô lập

Kết quả đo ngày 27/11/2026: [RESULTS.md](RESULTS.md).

Đây là minh chứng cho mục Test Report (TC_F5_PERF_01/02/04/06, V.2.1.8, V.2.1.11–2.1.13). Mọi kết quả nằm trong `loadtest/results/`.

## Môi trường

- Backend: image Docker build từ `Dockerfile` (giống bản deploy Render), `ASPNETCORE_ENVIRONMENT=Production`, giới hạn **2 vCPU / 1 GB RAM**.
- DB: `postgis/postgis:16-3.4` trong cùng compose. Không dùng Neon thật.
- OSRM: container `osrm` local (cổng 5001), dùng để chọn trạm theo đường bộ.
- AI chính và AI dự phòng trỏ vào cổng đóng (`127.0.0.1:9`). Worker AI đi đường lỗi (circuit breaker → Level 0), nên **không gọi Kaggle / OpenRouter** và không tốn chi phí.
- Cloudinary không cấu hình (chế độ mock). URL ảnh `images.unsplash.com` được chấp nhận, không upload thật.
- Rate limit SOS 5 / phút / IP **giữ nguyên**. k6 gửi `X-Forwarded-For` khác nhau cho từng người dùng ảo, như nhiều điện thoại đứng sau proxy Render (`ForwardedHeaders__Enabled=true`). Kịch bản `ratelimit.js` kiểm tra riêng một IP.
- k6 chạy bằng image `grafana/k6:0.54.0`, cùng mạng Docker với API.

## Chạy

```bash
docker compose -f loadtest/docker-compose.yml up -d --build      # API http://localhost:5080
MSYS_NO_PATHCONV=1 docker run --rm --network ed-loadtest_default -v "$(pwd -W)/loadtest:/lt" -e INCIDENTS=600 grafana/k6:0.54.0 run /lt/k6/seed.js

bash loadtest/run.sh perf01_read     # 20 / 100 / 200 VU, mỗi bậc 5 phút
bash loadtest/run.sh perf02_sos      # 2 SOS/giây trong 5 phút (nửa có ảnh)
bash loadtest/run.sh perf04_spike    # 0 → 500 VU trong 10 giây + đo phục hồi
bash loadtest/run.sh ratelimit       # 1 IP gửi 10 SOS → 5 × 200, 5 × 429
bash loadtest/run.sh perf06_soak     # 50 VU trong 60 phút
# Hàng đợi AI đầy: chạy AI giả chậm 2 s rồi trỏ API vào đó
node loadtest/signalr/slow_ai.mjs &  AI_URL=http://host.docker.internal:5099 docker compose -f loadtest/docker-compose.yml up -d
bash loadtest/run.sh perf04_queue    # 800 SOS có ảnh từ 100 VU
node loadtest/signalr/reconnect.mjs  # restart API khi 50 kết nối SignalR đang mở

bash loadtest/run_all.sh             # cả bộ trên DB sạch (~1 giờ 50 phút); run_queue.sh chỉ chạy spike + hàng đợi AI
RECEIVERS=200 SENDERS=50 DURATION_S=1800 node loadtest/signalr/gps_latency.mjs   # GPS qua SignalR, 30 phút

docker compose -f loadtest/docker-compose.yml down -v            # dọn dẹp
```

Mỗi lần `run.sh` tạo `results/<kịch bản>_<thời điểm>/` gồm:
- `k6_output.txt`: bản in đầy đủ của k6;
- `summary.json`: số liệu máy đọc được;
- `docker_stats.csv`: CPU / RAM của API và DB mỗi 10 giây;
- `api_log_tail.txt`: log API.

## Kiểm tra không mất báo cáo sau spike

```sql
-- docker exec ed-loadtest-db-1 psql -U postgres -d loadtest
select "Status", count(*) from "Incidents" group by 1;   -- AiProcessing phải về 0 khi hàng đợi chạy hết
```

Số sự cố tạo trong lúc spike phải bằng `sos_accepted` trong k6.

## Giới hạn (ghi kèm khi báo cáo)

- Chạy trên một laptop (Ryzen 7 5800H, 16 GB). k6 và API dùng chung máy, không phải hạ tầng Render free-tier (0,1 CPU).
- AI không chạy thật. Độ trễ AI đo riêng ở TC_F5_PERF_03 (Kaggle T4).
- SignalR đo trên localhost, nên không gồm độ trễ mạng di động.
