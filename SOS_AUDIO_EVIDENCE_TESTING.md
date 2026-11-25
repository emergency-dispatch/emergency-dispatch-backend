# SOS Audio — nội dung duy nhất description/note

## Contract hiện tại

- Web/Mobile chuyển giọng nói thành chữ vào cùng ô mô tả có thể chỉnh sửa.
- Tạo SOS: `description` <=4000 ký tự. Bổ sung: `note` <=4000. Không có transcript/transcriptionStatus riêng.
- `audioAttachments: [{ "uploadToken": "..." }]`, tối đa một audio và tổng năm attachment mỗi request.
- Upload audio <=5 MiB, >0–30 giây; lưu riêng tư và cấp link nghe 5 phút sau khi kiểm tra quyền.

## Swagger

1. Dùng DB thử riêng, áp migration mới `20261125154731_UnifyIncidentDescription`, cấu hình Cloudinary thật. Chưa chạy migration trên DB vận hành trong đợt sửa này.
2. Đăng nhập Citizen, upload ảnh và audio qua `POST /api/media/upload` (form field file). Audio trả uploadToken, durationSeconds; không trả bản chép lời.
3. Gửi `POST /api/incidents` hoặc `/sos`:

```json
{
  "description": "Tai nạn xe máy. Có hai người bị thương, cần cứu hộ.",
  "latitude": 10.84,
  "longitude": 106.80,
  "locationAddress": "Đường tại khu vực thử nghiệm",
  "mediaUrls": ["URL ảnh đã upload"],
  "audioAttachments": [{ "uploadToken": "token audio" }]
}
```

Chọn tọa độ thật trong vùng thử đã cấu hình. Ví dụ payload không phải bằng chứng đã chạy trên DB vận hành.

4. GET chi tiết: mô tả đầy đủ nằm trong description; mediaItems chứa audio và duration/MIME/publicId, không có transcript/transcriptionStatus. Kiểm tra chuỗi 4000 được nhận, 4001 bị từ chối. JSON audio còn gửi transcript phải bị từ chối thay vì bỏ qua âm thầm.
5. Preview: POST `/api/media/audio/preview-url` với uploadToken. Sau khi tạo ca: GET `/api/incidents/{id}/audio/{mediaId}/playback-url`. Người có quyền nhận data.url/expiresAt; tài khoản khác không được ký link. URL gốc unsigned bị từ chối, URL ký hết hạn bị từ chối. Receipt khác chủ/hết hạn/sửa nội dung bị từ chối.
6. Ảnh + description + audio: Qwen nhận description một lần, YOLO chỉ nhận ảnh. Audio + description rỗng: khi đến cổng tự điều phối phải dừng với audio-description-unavailable; Operator nghe/xác nhận. Không ảnh: Pending, không đưa audio vào mô hình ảnh.
7. Bổ sung qua POST `/api/incidents/{id}/supplements`:

```json
{
  "note": "Người bị thương vẫn đang chờ cứu hộ.",
  "audioAttachments": [{ "uploadToken": "token audio" }]
}
```

Guest kèm guestCancellationToken theo contract hiện có. Kiểm tra note đầy đủ được lưu, push chỉ chứa đoạn xem trước. Operator/crew nhận event đầy đủ; nhóm theo dõi rộng không nhận note/tên/ảnh/audio mà phải tải API có phân quyền. Không tự điều phối lại ca.
8. Audio supplement nghe qua `/api/incidents/{id}/supplements/{supplementId}/audio/{mediaId}/playback-url`. Người báo trùng không được đọc description/audio của người báo gốc. Bản đồ và thông báo khu vực không chứa nội dung riêng tư.

## Kiểm tra migration dữ liệu cũ

- Mô tả cũ + Transcript trong media Audio được ghép vào Description, cách nhau bằng dòng trống; phần đã có không bị thêm lần hai.
- Transcript trong JSON supplement (PascalCase/camelCase) chuyển sang Note, sau đó bỏ các key text/status khỏi audio JSON.
- Nội dung cũ tổng cộng >4000 vẫn giữ đầy đủ trong cột text; giới hạn 4000 chỉ cho request mới. Không làm hiện lại lời nói của audio đã xóa.
- Audio ID/publicId/duration và file gốc giữ nguyên. Hai cột Transcript/TranscriptionStatus được bỏ sau backfill.
- Không phát thông báo hay tự điều phối từ migration. Rollback không cắt Note về 1000 và không tách lại được phần nhập tay/lời nói đã gộp.

## Bằng chứng kiểm thử

Kết quả đợt sửa gộp nội dung được ghi sau khi chạy test. Kiểm thử camera/microphone/trình nghe trên Web/Mobile và inference Qwen/YOLO thật không nằm trong đợt này.
Lần triển khai audio trước đã có 6 kiểm tra Cloudinary thật bằng WAV một giây tự sinh (upload, thời lượng, link riêng tư/hết hạn, xóa file test); phần upload và phát audio không bị thay đổi trong đợt gộp text.


### Kết quả đợt gộp description — 25/11/2026

- Toàn bộ suite BE: **1066 Passed, 0 Failed, 2 Skipped** (1068 total).
- **15 kiểm tra PostgreSQL riêng Passed**: backfill Description/Note, chống chép trùng, PascalCase/camelCase JSON cũ, giữ dữ liệu dài hơn 4000 sau khi gộp, không làm hiện lại audio đã xóa, giữ metadata, bỏ cột cũ, rollback/reapply không cắt hay nhân đôi text.
- EF xác nhận model/snapshot đã khớp migration. Không áp dụng migration lên DB vận hành trong đợt này; PostgreSQL thử đã dừng.
- Chưa sửa UI Web/Mobile, chưa gọi Qwen/YOLO thật hay chạy lại Cloudinary trong đợt chỉ gộp text này.
