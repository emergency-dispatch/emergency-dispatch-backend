# YOLO Emergency Classification Service

Service FastAPI (Python 3.10+) phục vụ model phân loại tai nạn/hỏa hoạn và model phát hiện xe.
Nhánh jam chỉ đưa ra bằng chứng trên ảnh tĩnh. AI không tự xác nhận ùn tắc,
không đo tốc độ, khoảng cách theo mét hoặc mật độ xe/km/làn.

## Chạy local cùng Backend / Start with the backend

From the repository root / Từ thư mục gốc repository:

```powershell
dotnet run --project EmergencyDispatch.API --launch-profile http
```

Set `YoloService:Enabled=true` and `YoloService:AutoStart=true` in your local
`EmergencyDispatch.API/appsettings.json` (or equivalent environment variables).
The backend starts Python `main.py` in the background, forwards its
logs to the backend console, and checks `/api/yolo/health`. Wait for
`YOLO ready / YOLO sẵn sàng` before testing. Model loading does not block the web API.

Chạy lệnh trên sẽ tự mở YOLO; không cần terminal Python riêng. Chờ log sẵn sàng trước khi
gửi ảnh. API vẫn chạy nếu Python lỗi; kết quả AI theo cơ chế fallback/review hiện có.
This launcher does not install Python/packages or download missing models.

Install requirements once / Cài thư viện một lần:

```powershell
python -m pip install -r YoloService/requirements.txt
```

Optional settings / Cấu hình tùy chọn:

- `YoloService:PythonExecutable`: full path to Python, e.g. the interpreter in a virtual environment.
  Without this setting, use `YoloService/.venv/Scripts/python.exe` on Windows or
  `YoloService/.venv/bin/python` on Linux if present; otherwise use `python` / `python3` from PATH.
- `YoloService:WorkingDirectory`: Python service directory, absolute or relative to the API content root;
  default is `../YoloService`.
- `YoloService:StartupTimeoutSeconds`: startup probe timeout, default 120 seconds (5-600).
- `YoloService:AutoStart=false`: connect to an independently managed YOLO service without launching Python.
  Use `YoloService__AutoStart=false` as an environment variable for Docker/deployment.

Only local HTTP addresses are supported for AutoStart. A valid YOLO already running at the
configured address is reused and is **not stopped** with the backend. An occupied unrelated
port is reported without starting a second process. On normal backend shutdown, only the
Python process launched by this backend is stopped. An abrupt OS/process termination may
require manually stopping its leftover Python process; the next run reuses a healthy one.

AutoStart is opt-in. Settings in `appsettings.json` apply to every environment unless overridden;
there is no separate Development override required. The local configuration file is ignored by Git,
so another machine must provide its own settings and API credentials. Deployments using Compose
should set `YoloService__AutoStart=false`, start YOLO as a separate container and set
`YoloService__BaseUrl=http://yolo:8001` when the service is named `yolo`.

## Khởi chạy riêng / Run independently

```powershell
cd D:\githup\emergency-dispatch-backend\YoloService
python -m pip install -r requirements.txt
python main.py
```

Swagger: `http://127.0.0.1:8001/docs`.

| Biến môi trường | Mặc định |
|---|---|
| `YOLO_ACCIDENT_FIRE_MODEL` | `YoloService/models/accident_fire_best.pt` |
| `YOLO_ACCIDENT_FIRE_FALLBACK` | Checkpoint `best.pt` Kaggle trên máy phát triển; cần cấu hình khi triển khai |
| `YOLO_JAM_MODEL` | `YoloService/models/traffic_jam.pt` |
| `YOLO_SERVICE_PORT` | `8001` |

Model tai nạn/cháy phải là classification có đúng `accident`, `fire`, `reject`.
Model jam đang dùng là detection COCO; không phải classifier được train riêng cho ùn tắc.
Không tải thêm model, huấn luyện mới hay gọi cloud trong bước phân tích hình học.

## Đánh giá jam bằng không gian / Spatial evidence

Quy tắc cũ `tổng >= 15`, `xe máy >= 12`, `ô tô + xe buýt >= 7` đã được bỏ.
Detection vẫn dùng ngưỡng phát hiện đối tượng `conf=0.3`. Sau đó:

1. Giữ các khung ô tô, xe máy, xe buýt, xe tải hợp lệ.
2. Nếu caller cung cấp `jam_roi`, chỉ chọn xe có điểm giữa cạnh đáy khung nằm trong vùng đó.
   Caller cần chọn phần đường cùng chiều và loại vùng xe đỗ. Backend không tự xác định làn,
   chiều chạy hoặc xe đang đỗ. Nếu không có ROI, dùng toàn ảnh và ghi rõ giới hạn này.
3. Tính tỷ lệ diện tích **hợp các khung xe** trong vùng xét; phần chồng lấp chỉ tính một lần.
   Đây là diện tích chiếu của bounding box, không phải diện tích mặt đường thực sự bị xe chiếm.
4. Tính khoảng hở giữa các khung, chia cho đường chéo khung trung bình. Chỉ so sánh xe có
   kích thước tương đối tương đồng để giảm ảnh hưởng phối cảnh; không hiệu chỉnh camera,
   không quy đổi sang mét và không chứng minh các xe cùng làn.
5. Tìm cụm xe gần nhau. Chỉ trả `suspected` khi đủ xe, đủ mức chiếm chỗ và có cụm dồn sát.

Ngưỡng mặc định là **điểm vận hành thử nghiệm**, chưa phải tiêu chuẩn ùn tắc và chưa được
hiệu chuẩn trên tập dữ liệu đại diện:

| Biến môi trường | Mặc định | Ý nghĩa |
|---|---|---|
| `JAM_MIN_VEHICLES` | `4` | Số xe tối thiểu trong vùng |
| `JAM_MIN_BOX_OCCUPANCY` | `0.35` | Tỷ lệ hợp khung xe/vùng xét tối thiểu |
| `JAM_MIN_CLUSTER_SIZE` | `3` | Cụm gần nhau phải có ít nhất số xe này |
| `JAM_MIN_CLUSTER_RATIO` | `0.60` | Cụm lớn nhất phải chứa ít nhất tỷ lệ này của số xe |
| `JAM_MAX_NORMALIZED_GAP` | `0.25` | Khoảng hở chuẩn hóa tối đa để nối hai xe vào một cụm |
| `JAM_MAX_SCALE_RATIO` | `2.5` | Tỷ lệ đường chéo tối đa của hai khung được so sánh |

Các điều kiện phải **đồng thời** thỏa mãn. Cần đo precision/recall trên validation/test riêng
trước khi chọn ngưỡng sử dụng thực tế. Ảnh che khuất, khung xe lớn, bãi đỗ hoặc góc chụp
khác nhau vẫn có thể gây sai. Một ảnh không đủ xác nhận xe đứng yên hoặc ùn tắc kéo dài.

`jam_confidence` và `models[].confidence` của detection trả `null`, vì không có xác suất jam
được hiệu chuẩn. Điểm quy tắc nằm tại `jam_geometry.heuristic_score` với
`score_is_probability=false`; điểm 1 không mang nghĩa chắc chắn ùn tắc.

## API phân loại / Classification

`POST /api/yolo/classify` nhận URL trực tiếp hoặc base64 ảnh:

```json
{
  "image_url": "https://example.com/direct-image.jpg"
}
```

Tùy chọn `jam_roi` là đa giác lồi 3–12 điểm theo thứ tự đường biên, tọa độ `[x,y]`
chuẩn hóa từ 0 đến 1, tương ứng ảnh sau khi sửa hướng EXIF. Ví dụ sau chỉ chọn nửa dưới
ảnh, **không có nghĩa đó luôn là vùng mặt đường**:

```json
{
  "image_base64": "BASE64_ANH_DAY_DU",
  "jam_roi": [[0, 0.5], [1, 0.5], [1, 1], [0, 1]]
}
```

ROI không hợp lệ trả HTTP 422. ROI chỉ hỗ trợ detector; dùng với classifier jam trả HTTP 400.
Không gửi đường dẫn máy như `C:\\Users\\...` vào `image_url`.

Phản hồi giữ hợp đồng trạng thái:

- `traffic_jam_status`: `suspected`, `no_evidence` hoặc `unverified`.
- `traffic_jam_evidence`: giải thích phạm vi, số xe, tỷ lệ khung xe và cụm xe.
- `is_traffic_jam`: luôn `false` tại tầng AI; chỉ Operator/Admin được xác nhận.
- `needs_human_review`: bật khi nghi ngờ hoặc không thể đánh giá.
- `jam_geometry`: bằng chứng có cấu trúc, tiêu chí, ngưỡng và giới hạn phương pháp.
- `jam_vehicle_count`, `jam_counts_by_type`: số xe **trong vùng đang xét**.
- `jam_geometry.detected_vehicle_count`: tổng xe phát hiện trước khi lọc ROI.

`no_evidence` chỉ nghĩa chưa đủ dấu hiệu theo phương pháp này, không xác nhận đường thông thoáng.
Nếu dùng classifier jam chuyên biệt trong tương lai, `jam_geometry=null`; confidence khi đó
là điểm của classifier, không phải điểm quy tắc hình học.

## Tích hợp .NET / Integration

Backend C# tiếp tục gửi `image_url`, nên mặc định dùng toàn ảnh. `jam_roi` hiện được cung cấp
trực tiếp trên API Python; không tự áp một ROI cố định lên ảnh SOS chụp từ nhiều góc.
Các trường trạng thái/bằng chứng hiện có vẫn được client C# đọc bình thường.
`DualEngineAiService` giữ phản hồi YOLO đầy đủ tại `RawResponse` → `yolo.rawResponse`
(chuỗi JSON), gồm `jam_geometry`, để kiểm tra lại các chỉ số sau khi lưu kết quả.

Để bật nhánh YOLO cho một phiên backend PowerShell:

```powershell
$env:YoloService__Enabled = "true"
$env:YoloService__BaseUrl = "http://127.0.0.1:8001"
dotnet run --project EmergencyDispatch.API --launch-profile https
```

Lệnh chạy backend thực hiện ở thư mục gốc backend, trong terminal khác với Python service.
Không thay đổi quy tắc thẩm định con người hay tự bật YOLO trong cấu hình mặc định.

`GET /api/yolo/health`: `healthy` khi cả hai model được tải, `degraded` khi chỉ một model sẵn sàng,
`unhealthy` khi cả hai không sẵn sàng. `gpu_available=false` vẫn cho phép chạy CPU.
