# Kiểm tra thông báo khu vực sự cố cho Citizen

## Quy tắc cần giữ

Citizen nhận thông báo khi tài khoản Active, chưa xóa; không cần online. Phát sau Operator/Admin xác minh, hoặc AI điều phối thành công và có lịch sử RescueStaff nhận nhiệm vụ. Nội dung chỉ là “Có sự cố tại [khu vực báo cáo]”, song ngữ vi/en. Mỗi sự cố gốc một thông báo. Không tự động công bố chỉ vì điểm tin cậy AI cao.

## Chuẩn bị môi trường thử

1. Dùng cơ sở dữ liệu thử riêng và tài khoản Admin, Operator, RescueStaff, ít nhất hai Citizen Active, một Citizen bị khóa. Không bật trên DB vận hành để thử chức năng phát toàn bộ người dân.
2. Áp dụng migration bằng cấu hình connection string **của DB thử**:

   ```powershell
   dotnet ef database update --project EmergencyDispatch.Infrastructure --startup-project EmergencyDispatch.API
   ```

3. Chạy API như thường. Worker đã được đăng ký sẵn. Swagger: đăng nhập Admin, Authorize bằng JWT; `GET /api/settings/citizen-incident-alerts` mặc định trả `values.enabled = false`.
4. `PUT /api/settings/citizen-incident-alerts` với `{ "enabled": true }`. Kiểm tra `values.enabledFromUtc` là thời điểm server vừa ghi. Không nhập mốc thời gian cũ để phát lại lịch sử.
5. Chỉ khi thử push thật: cấu hình Firebase theo hướng dẫn hiện có, đăng ký token thiết bị thử qua `PUT /api/users/fcm-token`, bật quyền thông báo trên thiết bị. Nếu không có token, chỉ đánh giá hộp thư/API, không ghi nhận push đã chạy.

## Các bước kiểm thử chính trên Swagger

| Tình huống | Cách làm | Kết quả cần thấy |
| --- | --- | --- |
| AI có độ tin cậy cao nhưng chưa giao việc | Tạo báo cáo thử, giữ chưa điều phối | Không có thông báo Citizen mới |
| AI đã giao, staff chưa nhận | Chờ AI tự giao thành công, không thao tác nhận | Chưa có thông báo Citizen |
| Staff nhận nhiệm vụ do AI giao | RescueStaff gọi `PUT /api/dispatch/assignments/{id}/status` với trạng thái `Accepted`, theo schema Swagger hiện tại | Sau lượt quét worker, mỗi Citizen Active có một notification loại 6 |
| Operator nhận thay staff trong nhánh AI | Dùng Operator cập nhật Accepted nhưng chưa xác minh sự cố | Không đủ bằng chứng staff nhận; chưa phát |
| Operator xác nhận sự cố | Operator gọi `PUT /api/incidents/{id}/verify` với payload hợp lệ theo Swagger | Có thông báo, không cần chờ AI hoặc staff nhận |
| Xác nhận hai lần/hai nhánh | Sau khi AI/staff đã kích hoạt, Operator xác minh cùng sự cố | Số thông báo của mỗi Citizen vẫn là một |
| Bản tin trùng | Liên kết bản tin thử vào sự cố gốc theo API trùng lặp hiện có | Không tạo đợt phát riêng cho bản tin trùng |
| Citizen không Active/đã xóa | Có tài khoản bị khóa/xóa trước khi xác nhận | Không tạo hộp thư mới cho các tài khoản này |
| Sự cố không còn hợp lệ | Hủy/đánh dấu giả trước khi worker ghi nhận | Không phát thông báo mới |
| Ngôn ngữ | Đọc bằng `Accept-Language: vi` rồi `en`; đặt UiLanguage cho push | Chỉ câu thông báo và khu vực, đúng ngôn ngữ |
| Phân quyền | Citizen/RescueStaff thử PUT cấu hình; không JWT đọc thông báo; Citizen khác đoán ID không thuộc họ | 403; 401; 404 tương ứng |
| Tắt/bật | Admin tắt; xác minh một sự cố; bật lại rồi xác minh một sự cố mới | Không phát bù sự cố xác minh trong lúc tắt; sự cố mới được phát |

Đối chiếu chính xác route xác minh với Swagger của build hiện tại trước khi gửi; không gọi API nhận nhiệm vụ hoặc xác minh trên sự cố thật để tạo dữ liệu thử.

Đăng nhập từng Citizen:

1. `GET /api/notifications`: tìm loại `CitizenIncidentAlert` (giá trị enum 6), kiểm tra `incidentId = null`, đọc `dataJson.publicAlertId`.
2. `GET /api/citizen-incident-alerts`: tổng/danh sách phải tương ứng thông báo của tài khoản này.
3. `GET /api/citizen-incident-alerts/{publicAlertId}`: chỉ `id`, `area`, `message`, `createdAt`. Không có tên, điện thoại, ảnh, thông tin y tế hoặc nội dung mô tả riêng.
4. Trên client đã tích hợp: popup xuất hiện qua `ReceiveNotification`/FCM, bấm mở chi tiết thông báo khu vực; không mở hồ sơ sự cố của người khác.

Worker quét khoảng 10 giây/lượt, xử lý tối đa 100 sự cố và 100 lượt giao nhận mỗi chu kỳ. Với nhiều tài khoản hoặc nhà cung cấp chậm, cần chờ hàng đợi xử lý hết; không có cam kết tất cả thiết bị nhận trong 10 giây.

## Kiểm tra độ bền bằng môi trường thử

- Thiếu FCM token: hộp thư vẫn lưu, realtime vẫn được thử; không đánh dấu FCM thành công.
- Nhà cung cấp lỗi tạm: cùng `NotificationId` được thử lại, tối đa 5 lượt; khoảng chờ 10, 20, 40, 80 giây sau các lần thất bại tương ứng. Lượt thứ 5 dừng thử. Không tạo thêm notification.
- Token không hợp lệ: dừng retry và chỉ xóa token nếu nó vẫn là token đã lỗi; không xóa token mới của thiết bị.
- Khởi động lại worker: lease hết sau 2 phút để lượt đang xử lý được nhận lại; không cần tạo lại sự cố. Provider có thể nhận push trùng sau crash; client khử trùng theo `notificationId`.
- Recipient bị khóa/xóa hoặc sự cố bị hủy trước lúc gửi: dừng lượt giao nhận, không gửi mới; các bản ghi hộp thư đã lưu vẫn tồn tại.
- Tắt cấu hình dừng tạo/gửi mới, không rút lại thông báo đã phát. Bật lại có thể tiếp tục hàng đợi cũ nếu còn hợp lệ.

## Kiểm thử tự động

```powershell
dotnet test EmergencyDispatch.Tests/EmergencyDispatch.Tests.csproj --filter "FullyQualifiedName~CitizenIncidentAlert"
dotnet test EmergencyDispatch.Tests/EmergencyDispatch.Tests.csproj
```

Unit tests dùng sender/realtime giả lập để kiểm tra logic, không gửi thông báo tới tài khoản thật. Kết quả PostgreSQL riêng xác nhận migration, truy vấn và lease; kiểm thử hiển thị FCM trên điện thoại phải được thực hiện riêng và lưu bằng chứng thiết bị.
