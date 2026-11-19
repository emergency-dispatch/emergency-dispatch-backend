# Chuyển trạm phụ trách: cách hoạt động và kiểm thử BE

## Phạm vi

Chuyển **trạm chịu trách nhiệm xử lý tin báo**, không phải chuyển xe hay nhân viên cứu hộ.
Hai nguyên nhân tự động: trạm không có tài khoản Operator hoạt động / quá hạn chưa tiếp nhận,
hoặc Admin xác nhận trạm ngừng hoạt động bằng `Station.IsActive = false`.
Tài khoản hoạt động không đồng nghĩa đang online: trường hợp tài khoản Active nhưng không phản hồi được phát hiện qua deadline.
Không dựa vào `OperatingHours`, số kết nối SignalR hoặc số xe rảnh để khẳng định trạm mất khả năng vận hành.

Luồng AI vẫn thử tự điều phối trước. Chỉ sau khi phân tích và lần thử tự điều phối kết thúc,
sự cố còn chờ xử lý mới có deadline tiếp nhận. Sự cố không cần phân tích AI có deadline ngay khi tạo.
Worker quét mỗi 30 giây. Khi API khởi động lại, các sự cố mới đã lưu kết quả AI nhưng chưa có deadline
được thử lại bước tự điều phối trước khi mở thời gian chờ; không chạy lại phân tích ảnh vì lý do này.

Sau khi chuyển trạm đã lưu, backend thử tự điều phối cho **đúng trạm nhận** trong worker:
- AI đạt ngưỡng `autoDispatchMinConfidence`, độ tin cậy đo từ mô hình, loại/mức độ hợp lệ và các kiểm tra an toàn còn đạt:
  tự chọn tổ/xe sẵn sàng của trạm B, phát nhiệm vụ cho staff thuộc tổ đó, không đợi Operator B phát lệnh.
- AI không đạt, Operator đã tiếp nhận/xác minh, nghi trùng hoặc trạm B không có tổ/xe phù hợp: giữ cho Operator B xử lý.
- Không gửi nhiệm vụ cho toàn bộ staff B. Không tự chọn lại xe trạm A/C trong lần thử sau chuyển trạm.
- Mỗi lần chuyển được thử một lần; trạng thái thử lưu DB để khởi động lại không mất việc.
  Sự cố đã điều phối không tạo thêm nhiệm vụ khi xử lý lại.
- Luồng này cần `autoDispatchEnabled: true` trong `PUT /api/settings/dispatch` (giữ nguyên các trường cấu hình khác).
  `station-transfer.enabled` chỉ bật/tắt việc tự chuyển trạm; AI vẫn có công tắc riêng.
- Chuyển tự động được thử tiếp trong cùng vòng quét; chuyển thủ công được worker xử lý ở lần quét kế tiếp (chu kỳ 30 giây).
- Chỉ chấp nhận `AiProcessed` hoặc `Escalated` chưa xác minh cho lần thử này, không hạ mức hay bỏ xác minh của người dùng.

Tự chuyển trạm chỉ áp dụng `Pending`, `AiProcessed`, `Verified`, `Escalated`, chưa có nhiệm vụ
`Dispatched`, `Accepted`, `EnRoute`, `OnScene` hoặc `Completed` và không phải báo cáo trùng/đang gom.
Việc tiếp nhận trạm **khác** trạng thái `Accepted` của nhiệm vụ cứu hộ.

Operator tiếp nhận qua API mới, hoặc thao tác giữ khóa, xác minh, điều phối thủ công hiện có.
Sau khi đã tiếp nhận, không chuyển vì hết deadline; vẫn có thể chuyển nếu trạm ngừng hoạt động hoặc không còn Operator,
miễn chưa có lệnh cứu hộ. Sự cố đang cứu hộ chỉ được bàn giao thủ công bởi Admin xác nhận rõ.

## Cấu hình và triển khai

Migration mới: `AddStationOwnershipHandover`. Không có câu lệnh cập nhật hàng loạt các sự cố cũ.
Migration tiếp theo `AddStationTransferDispatchRetry` thêm dấu xử lý trong lịch sử chuyển trạm;
đánh dấu các lượt chuyển đã có trước migration để không tự chạy lại những lượt cũ.
Sự cố trước migration có `stationAssignedAt = null`, không tự được đưa vào luồng chuyển trạm.
Sự cố mới sau triển khai được lưu metadata dù tự chuyển đang tắt; khi bật, các sự cố mới này có thể được xử lý.
Chuyển thủ công hoặc tách báo cáo thành sự cố riêng khởi tạo chu kỳ trách nhiệm mới.

Chạy migration trên đúng DB thử nghiệm trước khi dùng tính năng:

```powershell
dotnet ef database update --project EmergencyDispatch.Infrastructure --startup-project EmergencyDispatch.API
```

Lệnh trên dùng connection string của môi trường đang chọn. Khi chạy API, cơ chế migration lúc khởi động của dự án vẫn áp dụng.
Không dùng DB triển khai để tạo dữ liệu test. Không cần bật YOLO/Qwen cho các ca kiểm thử không gửi ảnh.

Đăng nhập Admin, Authorize trên Swagger. Đọc và sửa cấu hình:

- `GET /api/settings/station-transfer` → `{ success, data: { values, updatedAt, updatedByName } }`.
- `PUT /api/settings/station-transfer`, body là **values**, không bọc `data`:

```json
{
  "enabled": true,
  "responseTimeoutMultiplier": 2,
  "maxAutomaticTransfers": 2
}
```

Mặc định `enabled = false`. Hệ số 1–10, số lần 1–5; không nhận NaN/Infinity.
Deadline lấy ngưỡng ngày/đêm của cấu hình escalation nhân hệ số, theo giờ Việt Nam tại lúc mở thời gian chờ.
Mặc định ban ngày 5 × 2 = 10 phút, ban đêm 2.5 × 2 = 5 phút, áp dụng mọi mức độ.
Deadline đã lưu không bị tính lại mỗi vòng quét hoặc khi Admin đổi cấu hình.

Để thử nhanh trên DB test, có thể lưu lại cấu hình escalation cũ rồi đặt cả hai ngưỡng ngày/đêm 0.5 phút,
giữ nguyên các trường hợp lệ khác của escalation và đặt hệ số chuyển trạm = 1.
Tạo sự cố mới sau đó; chờ 30–60 giây và đọc lại sự cố. Khôi phục cấu hình sau test.

## API và dữ liệu mới

`GET /api/incidents/{id}` có thêm:

- `stationAssignedAt`, `stationResponseDueAt` (UTC; null khi chưa mở thời gian chờ).
- `stationAcknowledgedAt`, `stationAcknowledgedByUserId`.
- `stationAssignmentVersion`: số phiên bản quyền phụ trách, tăng mỗi lần chuyển.
- `stationTransferChainId`, `automaticStationTransferCount`.
- `stationTransferAlertCode`, `stationTransferAlertedAt`.

Tiếp nhận bằng Operator của trạm đang phụ trách hoặc Admin:

```http
POST /api/incidents/{incidentId}/acknowledge
Authorization: Bearer <token>
Content-Type: application/json
Accept-Language: vi
```

```json
{ "expectedAssignmentVersion": 1 }
```

Dùng số phiên bản vừa đọc, không mặc định luôn là 1. Có thể gửi `{}` để tương thích client chưa lưu phiên bản.
API kiểm tra lại trạm và tài khoản trên DB. Gửi lại khi đã tiếp nhận không đổi người/thời điểm tiếp nhận đầu tiên.
Operator trạm khác → 403; phiên bản cũ hoặc khóa của Operator khác → 409; không tồn tại → 404.

Chuyển thủ công dùng route cũ:

```http
POST /api/incidents/{incidentId}/transfer
```

```json
{
  "stationId": "GUID-cua-tram-nhan",
  "reason": "Trạm hiện tại không thể tiếp tục phụ trách",
  "expectedAssignmentVersion": 1,
  "confirmActiveAssignments": false
}
```

Trạm nhận phải Active, chưa xóa và có Operator Active. Lý do 5–500 ký tự.
Operator chỉ chuyển sự cố của trạm mình, chưa có lệnh cứu hộ và không bị người khác giữ khóa.
Nếu đã có lệnh cứu hộ: Operator → 409; Admin cũng → 409 khi chưa gửi `confirmActiveAssignments: true`.
Admin xác nhận chỉ đổi trạm phụ trách; **giữ nguyên xe, đội, trạng thái và thời gian nhiệm vụ**.

`GET /api/incidents/{id}/station-transfers` trả lịch sử tăng theo `assignmentVersion`.
Admin xem được; Operator cần thuộc trạm hiện tại. Không có API sửa/xóa lịch sử.
Reason codes: `Manual`, `NoOperator`, `NoResponse`, `StationUnavailable`.
Lịch sử lưu tên trạm tại thời điểm chuyển, người thao tác, phiên bản, lần tiếp nhận trước và cơ sở chọn trạm.

## Chọn trạm và chống vòng lặp

Lọc trạm Active có Operator Active; bỏ trạm hiện tại và các trạm đã đi qua trong cùng chu kỳ tự động.
So sánh quãng đường bộ **trạm → sự cố** qua OSRM; không dùng bán kính trạm để loại ứng viên.
Nếu chỉ có một trạm thay thế thì chọn trực tiếp (`SingleCandidate`, không ghi khoảng cách chưa đo).
Nếu OSRM không có tuyến hợp lệ trong 2 giây, dùng tọa độ dự phòng, ghi rõ `ApproximateStraightLine`, không gọi là đường bộ.
Kết quả đường bộ có `RoadDistance` và `selectionDistanceKm` trong lịch sử.

Mặc định chỉ A → B → C, không tự quay lại A. Admin/Operator chuyển thủ công sẽ bắt đầu chu kỳ tự động mới.
Không còn trạm phù hợp: giữ trạm hiện tại, cảnh báo Admin `NoTarget`.
Hết giới hạn: giữ trạm hiện tại, cảnh báo Admin `TransferLimit`.
Không gửi lại cùng cảnh báo trong mỗi vòng quét; vẫn thử tìm trạm khi điều kiện thay đổi.

## Checklist kiểm thử Swagger

Chuẩn bị trên DB test ít nhất A/B/C Active, mỗi trạm có Operator Active; Admin và token Operator A/B/C.
Tạo SOS không ảnh bằng tài khoản Citizen hoặc Guest hợp lệ; chọn tọa độ gần A, loại/description hợp lệ theo Swagger hiện tại.
Dùng GUID trả về, không dùng mã hiển thị `INC-...` làm API ID. Không mở trang tự giữ khóa khi test không phản hồi.

| Ca | Cách thực hiện | Kết quả mong đợi |
|---|---|---|
| Không phản hồi | Tạo mới, không lock/verify/ack; đợi quá deadline và tối đa thêm 30 giây | Chuyển sang trạm phù hợp; version tăng; `NoResponse` |
| Không có Operator | Ngưng tất cả tài khoản Operator của A bằng API Admin; tạo mới gần A | Chuyển khi worker quét, không cần chờ deadline; `NoOperator` |
| Trạm ngừng hoạt động | Tạo trước ở A, sau đó Admin PUT `/api/stations/{A}` với thông tin trạm đầy đủ và `isActive:false` | Sự cố chưa điều đội chuyển đi; `StationUnavailable` |
| Đã tiếp nhận | Gọi acknowledge bằng Operator A trước hạn rồi đợi | Không chuyển vì quá hạn; ack giữ nguyên |
| Tiếp nhận hai lần | Gửi acknowledge hai lần bằng actor hợp lệ | 200 hai lần, không đổi người/thời điểm tiếp nhận đầu |
| Sai trạm | Gọi acknowledge/transfer bằng Operator B khi A đang phụ trách | 403 |
| Phiên bản cũ | Sau chuyển đọc version mới, gửi version trước đó | 409; không đổi dữ liệu |
| Đã điều cứu hộ | Điều đội bằng API hiện có, rồi chờ/đặt trạm unavailable | Không chuyển tự động; nhiệm vụ giữ nguyên |
| AI tự điều phối | Dùng ảnh và cấu hình AI auto-dispatch thực tế đủ điều kiện | Vẫn tự điều đội theo luồng cũ; không phải chờ Operator tiếp nhận |
| AI cao sau chuyển B | Bật auto-dispatch, ca `AiProcessed`/`Escalated` chưa xác minh có confidence thật đạt ngưỡng, B có tổ/xe phù hợp | Staff thuộc tổ B được chọn nhận nhiệm vụ; Operator B được thông báo nhưng không cần phát lệnh |
| AI thấp sau chuyển B | Confidence dưới ngưỡng, chuyển sang B | Không có phân công tự động; Operator B xác minh và điều phối thủ công |
| B không có tổ sẵn sàng | AI cao nhưng B không có tổ/xe phù hợp; A/C vẫn có xe | Không điều xe A/C trong lần thử này; báo Operator B xử lý |
| Operator tiếp nhận trước worker | Chuyển sang B rồi Operator B acknowledge trước lần thử | Worker không tự giao đè lên thao tác của Operator |
| Khởi động lại sau chuyển | Chuyển B, dừng backend trước lần thử rồi khởi động lại | Đọc việc chờ từ DB, kiểm tra lại trạm/phiên bản/AI; không tạo nhiệm vụ trùng |
| Chuyển thủ công | Operator A chuyển sang B hợp lệ trước điều phối | Trạm B có quyền xử lý; A mất quyền; reset ack/deadline/chu kỳ |
| Bàn giao khi đang cứu hộ | Admin chuyển không xác nhận rồi gửi lại với xác nhận true | Lần đầu 409, lần sau 200; nhiệm vụ không bị hủy/giao lại |
| Không trạm thay thế | Ngưng Operator của B/C, để A quá hạn | `NoTarget`, Admin nhận một cảnh báo; sự cố không mất khỏi DB |
| Hết số lần | Cho B rồi C lần lượt không phản hồi | Đủ 2 lần thì dừng, `TransferLimit`, không A ↔ B vô hạn |
| Song ngữ | Lặp API với `Accept-Language: vi` / `en`; kiểm tra thông báo người nhận | Message và lý do hệ thống đúng ngôn ngữ; ghi chú người dùng giữ nguyên |
| Tắt tính năng | PUT settings `enabled:false`, tạo tin không tiếp nhận | Không chuyển tự động; chuyển thủ công vẫn dùng được |
| Xung đột | Gửi ack/manual transfer khi worker đang định tuyến | Chỉ trạng thái hợp lệ mới được lưu; dữ liệu đã tiếp nhận/đổi trạm không bị ghi đè |
| Báo cáo gom | Có parent + linked reports, chuyển parent | Linked reports cùng trạm mới; parent giữ nguyên ID, thời điểm, AI và ảnh |

## Handoff web/mobile

Không thay đổi mã web/mobile trong đợt này. Tiếp tục dùng `ReceiveIncidentTransferred`, `ReceiveIncidentUpdate`, `ReceiveIncidentLock`.
Sự kiện chuyển trạm thêm `isAutomatic`, `reasonCode`, `assignmentVersion`.
Trạm cũ bỏ sự cố khỏi hàng đợi của mình; trạm mới tải lại chi tiết. Không tạo sự cố mới cho cùng lần chuyển.
Client nên gửi `expectedAssignmentVersion` và khi gặp 403/409 thì tải lại quyền phụ trách thay vì retry ghi dữ liệu cũ.
Hiển thị trạng thái tiếp nhận trạm riêng với trạng thái `Accepted` của đội cứu hộ.

## Xác minh tự động

`dotnet test EmergencyDispatch.Tests --filter FullyQualifiedName~StationTransferTests`
kiểm tra quy tắc chuyển, quyền truy cập, phiên bản cũ, bảo toàn nhiệm vụ, cạnh tranh với ack/dispatch,
giới hạn/vòng lặp, sự cố cũ, lịch ngày/đêm và bản dịch. Đây là test backend, không phải xác nhận giao diện/thiết bị đã tích hợp.
