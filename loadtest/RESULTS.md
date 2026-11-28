# Kết quả load test 27/11/2026

Môi trường và cách chạy: xem [README.md](README.md). Mọi số dưới đây lấy từ `k6_output.txt` / `summary.json` / `docker_stats.csv` trong từng thư mục `results/`.

- **Máy:** laptop Ryzen 7 5800H, 16 GB.
- **API:** Docker, giới hạn 2 vCPU / 1 GB.
- **Hạ tầng:** PostGIS 16, OSRM local, k6 0.54.0. AI thật không chạy.

## Kết quả chính thức (bản backend đã sửa, chạy 17:02–19:00)

| Mã | Kịch bản | Kết quả | Ngưỡng | Đạt | Thư mục |
|---|---|---|---|---|---|
| PERF_01 | Operator đọc, 20 / 100 / 200 VU × 5 phút (3 endpoint) | 104.566 request, 0 lỗi. p95 ở 200 VU: incidents 18,3 ms, queue 41,3 ms, stations 11,2 ms. p95 ở 20 / 100 VU ≤ 74 ms | p95 < 300 ms, lỗi < 0,5 % | Đạt | `perf01_read_20261127_170214` |
| PERF_02 | 2 SOS/giây × 5 phút (một nửa có ảnh) | 601 SOS, 0 lỗi. p50 26,3 ms, p95 41,8 ms, max 62,6 ms | p95 < 1.500 ms | Đạt | `perf02_sos_20261127_171918` |
| Rate limit | 1 IP gửi 10 SOS liên tiếp | 5 × HTTP 200, 5 × HTTP 429 | 429 khi vượt 5/phút | Đạt | `ratelimit_20261127_172419` |
| PERF_04a | Spike 0 → 500 VU trong 10 s, giữ 20 s | 6.929 SOS được nhận, 1 request lỗi (0,01 %). DB tăng đúng 6.929 (10.261 → 17.190). SOS p95 2,09 s. Operator đọc p95 376 ms trong spike, 19,9 ms ở 0–30 s sau spike, 12,7 ms ở 30–60 s sau spike | Không mất báo cáo; về bình thường trong 30 s | Đạt | `perf04_spike_20261127_184256`, `db_counts.txt` |
| PERF_04b | Hàng đợi AI đầy: 800 SOS có ảnh từ 100 VU, AI giả 2 s/ảnh, 3 job song song | 800/800 được nhận và lưu (17.190 → 17.990), 0 lỗi. Hàng đợi đạt 767 mục (vượt sức chứa 500: hàng tràn hoạt động), chạy hết sau khoảng 8,5 phút. SOS p50 323 ms, **p95 4,51 s**, max 9,05 s. CPU API 200 % trong 30 giây dồn tải | p95 < 1.500 ms | **Không đạt** (CPU 2 vCPU bão hòa) | `perf04_queue_20261127_184840` |
| V.2.1.8 / 2.1.13 | SignalR GPS: 200 kết nối nhận + 50 xe gửi mỗi 3 s × 30 phút | 29.923 lần gửi, 5.984.600 lượt nhận, 0 mất, 0 trùng, 0 sai thứ tự. Độ trễ p50 14,9 ms, p95 21,8 ms, p99 24,9 ms, max 121,6 ms. 0 lần rớt kết nối. RAM API 178 → 237 MB | p95 < 1.000 ms; 200 kết nối × 30 phút | Đạt | `signalr_gps_20261127_152802` |
| V.2.1.8 | Khởi động lại container API khi 50 kết nối đang mở | API mất 3,4 s. 50/50 kết nối tự nối lại sau 1,04–1,08 s kể từ lúc API sẵn sàng | ≤ 5 s | Đạt | `reconnect_20261127T082750` |
| PERF_06 | Soak 60 phút: 50 VU + 1 SOS/10 s, DB có 9.560 sự cố chờ | 118.232 request, 0 lỗi. p95 56,5 ms, p99 98,4 ms. Không restart, không OOM. RAM TB 10 phút đầu 344 MB, 10 phút cuối 482 MB, max 547 MB | Ổn định; RAM < 350 MB | Ổn định: đạt. RAM < 350 MB: **không đạt** | `perf06_soak_20261127_174220` |
| PERF_03 | AI thật TriageVL-3B trên Kaggle Tesla T4 (28/11, 01:27–02:02 UTC): 3 request khởi động + 120 request tuần tự, ảnh 512 px | 120/120 thành công. TB 17,1 s, trung vị 16,4 s, **p95 21,7 s**, max 38,6 s. Theo nhãn: none 15,2 s, fire 18,5 s, accident 19,5 s. VRAM đỉnh 5.407 MiB (nvidia-smi, GPU 0) | ≤ 15 s / ảnh | **Không đạt** | `ai_t4_2026-11-28` |

## Lỗi tìm ra và đã sửa (so sánh trước / sau)

| Vấn đề | Trước khi sửa | Sau khi sửa | Thư mục trước |
|---|---|---|---|
| `GET /api/incidents/queue` tải toàn bộ sự cố chờ (kèm media, JSON AI, EF tracking) | 200 VU: queue p95 1,15 s, incidents 650 ms, stations 600 ms (CPU chạm trần) | 18–41 ms | `perf01_read_20261127_143406` |
| Như trên, khi có 6.574 sự cố chờ | API **OutOfMemory** và dừng sau khoảng 30 s soak (exit 139), 99,75 % request lỗi | Soak 60 phút với 9.560 sự cố chờ, 0 lỗi | `perf06_soak_20261127_155828` |
| Hàng đợi AI `FullMode.Wait`: SOS chờ tới khi hàng đợi có chỗ | Hàng đợi đầy: SOS p95 68 s, max 69 s | p95 4,51 s | `perf04_queue_20261127_150610` |

Sửa ở backend:
- `AiAnalysisQueue`: không chặn producer, dùng hàng tràn FIFO.
- `IncidentRepository.GetPendingQueueAsync`: tối đa 200 mục, AsNoTracking, AsSplitQuery, tổng số qua header `X-Total-Count`.

## Lần chạy không dùng làm kết quả

- `perf01_read_20261127_141629`: lần đầu, chưa tách số theo endpoint / bậc tải.
- `perf04_spike_20261127_145658`: gom trùng đang bật, nên 3.260 báo cáo bị gom và hàng đợi AI không đầy. Kết quả vẫn hợp lệ cho "không mất báo cáo": 3.647/3.647 được nhận.
- `perf04_spike_20261127_172422`, `perf04_queue_20261127_172646`: script cũ dùng lại số điện thoại giữa hai kịch bản chạy liền nhau. BR-CIT-01 (1 sự cố đang mở mỗi số trong 3 phút) trả 429 đúng quy tắc. Script đã sửa (số ngẫu nhiên) và chạy lại.
- `perf02_sos_20261127_145141`, `perf04_spike_20261127_150105`, `ratelimit_20261127_152732`: bản backend trước khi sửa. Số liệu tương tự bản sau.

## Giới hạn

- Một laptop, k6 chạy cùng máy với API. Không phải hạ tầng Render free-tier (0,1 CPU, 512 MB), nên số đo không áp dụng trực tiếp cho bản demo cloud.
- AI không chạy thật. Độ trễ AI đo riêng ở TC_F5_PERF_03 (Kaggle T4).
- SignalR đo trên localhost, nên không gồm độ trễ mạng di động.
- Bài SignalR và bài reconnect chạy trên bản trước khi sửa. Hai chỗ sửa không chạm tới SignalR.

## PERF_03 — đo lại AI trên Kaggle T4 (28/11/2026)

- Notebook riêng tư `triagevl-benchmark-t4` (version 3), script `ai-service/kaggle_benchmark_t4.py`. Service `ai_service.py` bản 2026-11-23 + LoRA từ dataset `triagevl-service`, NF4, `AI_MAX_IMAGE_SIDE=512`, `AI_MAX_NEW_TOKENS=512`. Gọi thẳng `127.0.0.1:8000`, không qua ngrok.
- Ảnh: 123 ảnh chọn ngẫu nhiên (seed 490) từ dataset `5090-dataset`.
- Gói: torch 2.11.0+cu130, transformers 5.19.0, peft 0.21.2, bitsandbytes 0.50.2, accelerate 1.15.0, fastapi 0.143.1.
- Kaggle cấp máy 2 × T4. Model chỉ nạp vào GPU 0 (`Device: cuda:0`). GPU 1 không dùng.
- File: `benchmark_t4/summary.json`, `latency.csv` (từng request), `vram.csv` (nvidia-smi mỗi 0,5 s), `triagevl-benchmark-t4.log` (output notebook), `triagevl/ai_service.log`.
- Số 10,9 s / p95 13,0 s ghi trước đây (02/11) không có log nên **không dùng nữa**. Số mới chậm hơn và không đạt ngưỡng 15 s. Thời gian `client_ms` và `service_inference_ms` gần bằng nhau (chênh ~15 ms), nên thời gian nằm ở suy luận, không ở mạng.
- Mục tiêu NFR "kết quả AI tới hàng đợi Operator trong 30 s (p95)" chưa đo đầu-cuối. Chỉ riêng suy luận đã chiếm p95 21,7 s, còn khoảng 8 s cho ngrok + hàng đợi.
