# Kiểm thử SOS Audio Evidence (BE)

## Phạm vi

Web/Mobile ghi âm và chuyển lời nói thành chữ. BE nhận audio + text, lưu vào ca và cung cấp phát lại có phân quyền. Các bước dưới đây là hướng dẫn chạy thật; test dùng Cloudinary SDK mock không chứng minh upload/phát trên thiết bị đã thành công.

## Chuẩn bị

- DB thử riêng, cấu hình Cloudinary thật, tài khoản Citizen/Operator/Staff và một Citizen khác. Không dùng DB vận hành để chạy kịch bản tạo SOS.
- Áp dụng migration `20261125141313_AddIncidentAudioEvidence` bằng quy trình migrate hiện có.
- Chuẩn bị ảnh hợp lệ; file M4A/MP3/WebM/OGG/WAV audio-only <=5 MiB, <=30 giây; một file >30 giây và một file video giả MIME audio.
- Swagger `/swagger`, đăng nhập Citizen và Authorize. Không cần dịch vụ STT/API key ở BE.

## Upload và preview

1. `POST /api/media/upload`, chọn file audio thật. Mong đợi Audio, durationSeconds thực tế, uploadToken, status AwaitingClientTranscript. URL gốc không mở công khai được.
2. `POST /api/media/audio/preview-url`: `{ "uploadToken": "token từ bước 1" }`. Mở data.url, nghe đúng file; expiresAt khoảng 5 phút. Hết hạn gọi lại để lấy link mới.
3. Quá 5 MiB, quá 30 giây, file hỏng hoặc video có MIME audio: HTTP 400. Không có Cloudinary thật: lỗi rõ ràng, không trả URL ảnh giả.

## Tạo SOS kèm ảnh và âm thanh

Upload ảnh bằng API cũ, sau đó gửi `POST /api/incidents` hoặc `/api/incidents/sos`:

```json
{
  "description": "Tai nạn có người bị thương",
  "latitude": 10.84,
  "longitude": 106.80,
  "locationAddress": "Đường tại khu vực thử nghiệm",
  "mediaUrls": ["URL ảnh do BE upload trả về"],
  "audioAttachments": [
    { "uploadToken": "token audio", "transcript": "Có hai người bị thương, một người đang nằm trên đường." }
  ]
}
```

- Chọn tọa độ thuộc vùng thử đã cấu hình để không bị geofencing/cooldown chặn. Giá trị trong ví dụ là dữ liệu nhập mẫu, không phải bằng chứng chạy test.
- GET chi tiết bằng tài khoản có quyền: audio là Audio, có durationSeconds/transcript; ảnh vẫn Photo. Audio không được đưa vào job phân tích ảnh; Qwen nhận ngữ cảnh transcript cùng ảnh.
- `GET /api/incidents/{id}/audio/{mediaId}/playback-url`: owner/Operator/Staff có quyền lấy được link; Citizen khác bị từ chối, không được ký URL trước khi kiểm tra quyền. Thiếu claim UserId hợp lệ: 401. Bản ghi không thuộc ca: 404.
- Sửa token, token hết hạn hoặc token của tài khoản khác: bị từ chối. Transcript >4000, >1 audio hoặc tổng >5 file: bị từ chối. Description vẫn tối đa 200; không dùng description để chứa toàn bộ bản chép lời dài.

## Hai nhánh điều phối

- Ảnh + audio + transcript hợp lệ: vẫn qua các quy tắc AI/ensemble/confidence hiện có; audio không làm giảm yêu cầu an toàn đó.
- Ảnh + audio không transcript (null/rỗng): lưu được audio; khi đến cổng tự điều phối phải dừng với audio-transcript-unavailable để Operator nghe và xác nhận.
- Chỉ audio, không ảnh: Pending/Unclassified; không gọi YOLO/VLM cho file âm thanh. Operator mở nghe rồi xử lý thủ công.
- Bỏ audio hoàn toàn: luồng SOS ảnh/ghi chú hiện có hoạt động như cũ.

## Bổ sung sau khi đã gửi SOS

`POST /api/incidents/{id}/supplements`:

```json
{
  "note": "Cập nhật hiện trường",
  "audioAttachments": [{ "uploadToken": "token audio", "transcript": "Người bị thương vẫn đang chờ cứu hộ." }]
}
```

- Guest phải kèm guestCancellationToken theo contract bổ sung hiện có. Người khác không được thêm; ca đã đóng không được thêm.
- `GET /api/incidents/{id}/supplements` giữ audio metadata và text; route nghe: `/api/incidents/{id}/supplements/{supplementId}/audio/{mediaId}/playback-url`.
- Operator/crew được phân công nhận ReceiveIncidentSupplement. Nhóm theo dõi rộng không được nhận audio/transcript trong broadcast. Không phát audio qua notification khu vực hoặc bản đồ công khai; không tự phát lại/hủy lệnh điều phối.
- Restart rồi đọc lại ca/supplement: audio, transcript, duration còn nguyên. Media Photo/Video cũ không thay đổi enum hay dữ liệu.

## Kết quả thực thi

Ghi riêng build, ngày chạy, DB thử, thiết bị/trình duyệt, request/response đã che token và kết quả nghe thật. Không ghi nhận upload Cloudinary hoặc phát âm thanh trên điện thoại là Passed chỉ dựa vào test mock.


### Đã kiểm tra ngày 25/11/2026

- Toàn bộ suite BE: **1063 Passed, 0 Failed, 2 Skipped** (1065 total). Có test hồi quy cho codec video rỗng của SDK, URL Cloudinary đã ký, receipt hết hạn/khác người, transcript quá dài, quyền nghe, SignalR và gửi lại SOS theo idempotency key.
- **11 kiểm tra PostgreSQL thật trên DB cục bộ riêng**: áp migration vào schema trước feature, giữ nguyên ảnh/video và note cũ, lưu/đọc lại enum Audio=2, duration/MIME/size/transcript, JSONB supplement và không nhân đôi sau restart. Môi trường thử không có PostGIS nên chỉ bỏ bootstrap extension trong probe; không kiểm thử thao tác không gian. PostgreSQL thử đã dừng.
- **6 kiểm tra Cloudinary thật** bằng WAV một giây tự sinh: upload và đọc duration; tạo URL nghe 5 phút; URL gốc không ký bị từ chối; URL ký hợp lệ tải được dữ liệu; URL hết hạn bị từ chối; file test được xóa. Không gửi bản ghi hay ảnh người dùng và không ghi khóa/URL ký vào báo cáo.
- `dotnet ef migrations has-pending-model-changes`: không có model change chưa được migration bao phủ.
- Giới hạn: chưa kiểm thử thu camera/microphone hoặc phát audio trên điện thoại/trình duyệt. Không chạy inference Qwen/YOLO thật trong đợt này; phần truyền transcript vào job AI và cổng tự điều phối được kiểm tra bằng automated tests. Chưa chạy deploy hay lệnh migrate lên DB vận hành.
