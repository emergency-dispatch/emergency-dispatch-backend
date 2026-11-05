# Deploy backend lên Render (dùng cho demo)

Backend chạy bằng Docker (`Dockerfile` ở thư mục gốc), cấu hình bằng `render.yaml`. Database vẫn là Neon, AI TriageVL vẫn chạy trên Kaggle + ngrok.

## 1. Trước khi deploy lần đầu

1. **Sao lưu Neon.** API tự chạy migration lúc khởi động. Lần đầu sẽ áp các migration mới lên Neon (hotspot, field request, gom báo cáo, `DataProtectionKeys`…).
2. Push code (có `Dockerfile`, `.dockerignore`, `render.yaml`) lên GitHub. `appsettings.json` **không** có trong git, nên mọi cấu hình đặt ở Render.
3. Kaggle: đặt secret `NGROK_DOMAIN` và `AI_SERVICE_API_KEY` để URL và khóa AI không đổi giữa các lần chạy.

## 2. Tạo service

Render Dashboard → **New → Blueprint** → chọn repo backend → Render đọc `render.yaml` và hỏi giá trị các biến secret:

| Biến | Lấy từ |
|---|---|
| `ConnectionStrings__DefaultConnection` | Connection string Neon (dạng `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require`) |
| `JwtSettings__Key` | Khóa JWT (≥ 32 ký tự). Giữ giống bản local nếu muốn token cũ còn dùng được |
| `Cors__AllowedOrigins__0` | Domain web FE, ví dụ `https://<tên>.vercel.app` (không có `/` cuối). Thêm domain khác bằng `Cors__AllowedOrigins__1`… |
| `Google__ClientId`, `Google__ClientSecret` | Google Cloud Console. Thêm domain web vào Authorized JavaScript origins |
| `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret` | Cloudinary |
| `EmailSettings__SenderEmail`, `EmailSettings__SenderPassword` | Gmail + App Password |
| `EmailSettings__BrevoApiKey` | **Bắt buộc trên gói free.** Render free chặn cổng SMTP 25/465/587 nên Gmail SMTP không gửi được. Tạo tài khoản Brevo (300 email/ngày miễn phí) → SMTP & API → API Keys; xác minh `SenderEmail` ở Senders. Có biến này thì backend gửi qua HTTPS, không dùng SMTP |
| `AiService__LocalServiceUrl` | `https://<ngrok-domain>/api/analyze-incident` |
| `AiService__ApiKey` | Trùng `AI_SERVICE_API_KEY` trên Kaggle |
| `OpenRouter__ApiKey` | OpenRouter (dự phòng khi Kaggle tắt) |

Các biến không phải secret đã có giá trị sẵn trong `render.yaml`. Đáng chú ý:
- `ForwardedHeaders__Enabled=true`: lấy IP thật của người dùng sau proxy Render (rate limit SOS, audit).
- `Swagger__Enabled=true`: mở `/swagger` để demo. Đặt `false` nếu không cần.
- `YoloService__Enabled=false`: YOLO không chạy trên gói free. AI chỉ dùng TriageVL → OpenRouter → Level 0.

Push FCM (tùy chọn): Render → service → **Environment → Secret Files** → thêm `firebase.json` (service-account JSON), rồi đặt `Firebase__CredentialPath=/etc/secrets/firebase.json`.

Bấm **Apply**. Lần build đầu mất vài phút. Xong sẽ có URL `https://emergency-dispatch-api.onrender.com` (tên có thể khác).

## 3. Kiểm tra sau deploy

- `GET https://<api>/api/localization/info` → 200.
- `https://<api>/swagger` mở được (nếu bật).
- Log Render có dòng "Database migration và seed dữ liệu hoàn tất thành công."
- Đăng nhập từ web FE không bị lỗi CORS.

## 4. Mỗi lần demo

1. **Đánh thức API trước 2–3 phút**: mở `https://<api>/api/localization/info`. Gói free ngủ sau khoảng 15 phút không có request; lần đầu thức dậy mất 30–60 s.
2. Bật notebook Kaggle (nếu muốn dùng TriageVL), chờ in "sẵn sàng".
3. Trong lúc demo, có web mở là API không ngủ. Nếu buổi demo dài và có lúc không thao tác, có thể đặt UptimeRobot / cron-job.org gọi URL trên mỗi 10 phút.

Lưu ý khi API ngủ: worker leo thang, hàng đợi AI và SignalR dừng. Sự cố gửi trước lúc ngủ vẫn nằm trong DB; worker chạy tiếp khi API thức dậy. Ảnh đang chờ AI lúc ngủ được worker "stuck AI" xử lý lại.

## 5. Cập nhật code

`autoDeploy: false` → push code không tự deploy. Muốn deploy: Render → service → **Manual Deploy → Deploy latest commit**. Đổi biến môi trường (ví dụ URL ngrok): sửa ở **Environment** → **Save changes**, Render tự khởi động lại, không cần build.

## 6. Giới hạn của cách deploy này

- Chỉ chạy **1 instance** (hàng đợi AI trong RAM, SignalR chưa có Redis backplane).
- Không có YOLO → không có kiểm tra nhanh ảnh của báo cáo gom và không có đối chiếu YOLO–Qwen về ùn tắc.
- Khóa ký mã hủy SOS của khách lưu trong bảng `DataProtectionKeys` của Neon, nên khởi động lại vẫn dùng được mã cũ.
- Thử Docker local: `docker build -t ed-api .` rồi chạy với các biến môi trường như trên, port 8080.
