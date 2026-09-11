# 📋 DANH SÁCH CÁC TASK BACKEND (TIẾN ĐỘ THỰC TẾ)

## 📊 BẢNG TỔNG HỢP TIẾN ĐỘ

| STT | Tên Task | Trạng thái |
|:---:|---|:---:|
| 1 | Auth (SignIn, SignUp, Verify OTP Email, Welcome Email, Google Login, Refresh Token) | 🟢 **Đã làm** |
| 2 | Quên mật khẩu (Forgot Password qua OTP Email & Reset Password) | 🟢 **Đã làm** |
| 3 | CRUD Profile người dùng mở rộng (Nhóm máu, bệnh án, liên hệ khẩn cấp) | 🟢 **Đã làm** |
| 4 | Đổi mật khẩu kèm gửi email cảnh báo bảo mật thời gian thực có IP | 🟢 **Đã làm** |
| 5 | Upload hình ảnh/video hiện trường lên Cloudinary | 🟢 **Đã làm** |
| 6 | Tiếp nhận báo cáo sự cố (SOS) & Hàng đợi duyệt sự cố cho Operator | 🟢 **Đã làm** |
| 7 | Operator xác minh sự cố (Human-in-the-loop: duyệt, ghi đè mức độ, hủy ca) | 🟢 **Đã làm** |
| 8 | Tích hợp AI Qwen2.5-VL / Qwen3-VL (Trích xuất #hazard, chấm Severity 1-5) | 🟢 **Đã làm** |
| 9 | Quản lý Trạm cứu hộ (Station CRUD & tọa độ GPS) | 🟢 **Đã làm** |
| 10 | Quản lý Đội xe cứu hộ & Đơn vị cơ động (Rescue Units / Fleet Management) | 🟢 **Đã làm** |
| 11 | API Đóng hồ sơ sự cố (Incident Closure: lưu biên bản, thiệt hại, người đóng) | 🔴 **Chưa làm** |
| 12 | Nhật ký kiểm toán toàn bộ vòng đời sự cố (Incident Audit Trail) | 🔴 **Chưa làm** |
| 13 | WebSocket Server (SignalR Hub: Live GPS Tracking xe & Đồng bộ trạng thái tức thì) | 🔴 **Chưa làm** |
| 14 | Push Notification FCM (Còi hú âm thanh cho Staff & tiến độ cho Citizen) | 🔴 **Chưa làm** |
| 15 | Escalation Service (Background worker quét sự cố Level 4-5 sau 5 phút cảnh báo quản lý) | 🔴 **Chưa làm** |
| 16 | Thuật toán Truy vấn Không gian Địa lý (Geospatial Query / PostGIS trong bán kính $R$) | 🔴 **Chưa làm** |
| 17 | Động cơ điều phối: So sánh 2 thuật toán (Nearest-Available vs. Load-Aware) | 🔴 **Chưa làm** |
| 18 | Bộ dữ liệu kiểm chứng & Báo cáo đo lường AI (Accuracy, F1-Score, Cohen's Kappa, Latency) | 🔴 **Chưa làm** |

---

## 1. PHÂN HỆ CITIZEN & AUTHENTICATION

- [x] **Task 1.1: Đăng ký, Đăng nhập & Xác thực Email OTP** `[🟢 Đã làm]`
  - API Register tạo tài khoản Citizen, tự sinh OTP 6 số gửi qua Gmail SMTP.
  - API Verify Email bằng OTP, tự động gửi Welcome Email hướng dẫn cập nhật hồ sơ y tế.
  - API Resend OTP xác thực.
  - API Login cấp Access Token (JWT) & Refresh Token.
  - API Login Google OAuth (xác thực Google ID Token).
  - API Refresh Token & Logout thu hồi Refresh Token.

- [x] **Task 1.2: Quên mật khẩu qua Email** `[🟢 Đã làm]`
  - API Forgot Password gửi OTP reset qua email (hạn 10 phút).
  - API Reset Password xác nhận OTP và đặt mật khẩu mới, thu hồi mọi phiên cũ.

- [x] **Task 1.3: CRUD Profile Cá nhân Mở rộng (Đặc thù cứu hộ)** `[🟢 Đã làm]`
  - API `GET /api/users/me` và `PUT /api/users/me`.
  - Lưu trữ đầy đủ: CCCD/CMND, Ngày sinh, Giới tính, Địa chỉ, **Nhóm máu** (`BloodType`), **Ghi chú bệnh án/dị ứng** (`MedicalNotes`), **Người liên hệ khẩn cấp** (Tên, SĐT, Quan hệ).
  - API `PUT /api/users/fcm-token` lưu FCM Device Token.

- [x] **Task 1.4: Đổi mật khẩu kèm Email cảnh báo bảo mật** `[🟢 Đã làm]`
  - API `POST /api/auth/change-password` xác thực mật khẩu cũ.
  - Tự động lấy địa chỉ IP và gửi email cảnh báo bảo mật thời gian thực.

---

## 2. PHÂN HỆ CORE DISPATCH API & REAL-TIME SERVICES

- [x] **Task 2.1: Tiếp nhận Báo cáo Sự cố (SOS) & Upload Media** `[🟢 Đã làm]`
  - Upload ảnh/video hiện trường lên Cloudinary (`POST /api/media/upload`).
  - Tạo sự cố `POST /api/incidents` (người dân đăng nhập hoặc vãng lai).
  - Tự động kích hoạt AI phân tích hiện trường khi tạo sự cố.
  - API danh sách sự cố có phân trang, bộ lọc (`GET /api/incidents`, `GET /api/incidents/{id}`).

- [x] **Task 2.2: Hàng đợi CAD & Điều phối viên Thẩm tra (Human-in-the-loop)** `[🟢 Đã làm]`
  - API `GET /api/incidents/queue`: Hàng đợi ưu tiên sự cố `Unclassified (Level 0)` lên đầu, tiếp đến `Level 5` $\rightarrow$ `Level 1`.
  - API `PUT /api/incidents/{id}/verify`: Operator duyệt sự cố, ghi đè mức độ rủi ro của AI.
  - API `PUT /api/incidents/{id}/cancel`: Hủy sự cố báo sai / báo khống.

- [x] **Task 2.3: Quản lý Trạm Cứu hộ (Station CRUD)** `[🟢 Đã làm]`
  - Entity `Station` đã có đầy đủ tọa độ, địa chỉ, số điện thoại, trạng thái hoạt động.
  - Bộ API CRUD Trạm cứu hộ hoàn chỉnh: `GET /api/stations`, `GET /api/stations/{id}`, `POST`, `PUT`, `DELETE /api/stations`.
  - Quản lý danh sách xe và nhân viên trực thuộc trạm.

- [x] **Task 2.4: Quản lý Đội Xe Cứu hộ & Đơn vị Cơ động (Rescue Units / Fleet Management)** `[🟢 Đã làm]`
  - Đã tạo Entity `RescueUnit` & `DispatchAssignment` (kèm migration EF Core).
  - Phân loại xe: Cấp cứu 115 (`Ambulance`), Cứu hỏa (`FireTruck`), Xe thang (`LadderTruck`), Cano (`RescueBoat`), Cứu nạn đặc chủng (`HeavyRescueVehicle`).
  - Quản lý 5 trạng thái xe: `Available`, `Dispatched`, `OnScene`, `Returning`, `Maintenance`.
  - API CRUD xe: `GET /api/rescue-units`, `GET /api/rescue-units/{id}`, `GET /api/rescue-units/station/{stationId}`, `POST`, `PUT`, `DELETE`.
  - API Cập nhật tọa độ GPS trực tiếp: `PUT /api/rescue-units/{id}/location` (dành cho Mobile/GPS Tracker).
  - API Cập nhật trạng thái xe: `PUT /api/rescue-units/{id}/status`.
  - Đã seed sẵn 6 xe cứu hộ mẫu vào 2 trạm Quận 1 và Quận 7.

- [ ] **Task 2.5: API Đóng Hồ sơ Sự cố (Incident Closure)** `[🔴 Chưa làm]`
  - API `POST /api/incidents/{id}/close`: Chuyển trạng thái sang `Completed`.
  - Lưu biên bản kết thúc ca: thời gian đóng, người đóng, báo cáo hiện trường, số người được cứu/thương vong, ảnh hiện trường sau xử lý.

- [ ] **Task 2.6: Nhật ký Kiểm toán Vòng đời Sự cố (Incident Audit Trail)** `[🔴 Chưa làm]`
  - Tạo entity `IncidentAuditTrail` lưu vết bất biến mọi thay đổi trạng thái trong vòng đời sự cố.
  - Tự động ghi log: Trạng thái cũ $\rightarrow$ mới, người thực hiện (Citizen, Operator, Staff, Worker), lý do, timestamp.
  - API `GET /api/incidents/{id}/audit-trail` truy xuất toàn bộ lịch sử ca sự cố.

- [ ] **Task 2.7: WebSocket Server Real-time (SignalR DispatchHub)** `[🔴 Chưa làm]`
  - Xây dựng `DispatchHub` tại `/hubs/dispatch` có xác thực JWT Bearer.
  - Phân nhóm kết nối: `Operators`, `Incident_{id}`, `Unit_{id}`.
  - **Live GPS Tracking**: Nhận tọa độ GPS xe cứu hộ gửi lên và broadcast trực tiếp cho Citizen (theo dõi xe tới) và Operator (bản đồ trung tâm).
  - **Status Real-time Sync**: Tự động broadcast sự kiện khi có sự cố mới hoặc khi trạng thái sự cố thay đổi.

- [ ] **Task 2.8: Dịch vụ Push Notification FCM** `[🔴 Chưa làm]`
  - Tích hợp Firebase Cloud Messaging (FCM HTTP v1 / FirebaseAdmin SDK).
  - Cảnh báo âm thanh còi hú (`emergency_siren.wav`, priority `high`) cho Rescue Staff khi nhận lệnh điều phối.
  - Push thông báo tiến độ xử lý cho Citizen ("Xe đang đến", "Đã tới hiện trường", "Đã xử lý xong").

- [ ] **Task 2.9: Escalation Service (Background Worker)** `[🔴 Chưa làm]`
  - Xây dựng `BackgroundService` (.NET IHostedService) chạy định kỳ mỗi 30s - 1 phút.
  - Tự động quét tìm sự cố **Level 4 - 5** chưa được tiếp nhận/điều phối sau **5 phút**.
  - Tự động đổi trạng thái sang `Escalated`, ghi log vào Audit Trail và bắn cảnh báo khẩn cấp lên Quản lý qua SignalR/FCM.

---

## 3. PHÂN HỆ AI MODULE & DISPATCH ENGINE

- [x] **Task 3.1: Pipeline AI Phân tích Hiện trường Qwen-VL** `[🟢 Đã làm]`
  - Tích hợp mô hình Qwen2.5-VL / Qwen3-VL qua OpenRouter trong `AiClassificationService`.
  - Nhận ảnh hiện trường, chuẩn hóa JSON, trích xuất `#hazardTags`, tóm tắt tiếng Việt, chấm `severityScore` (1-5).
  - Cơ chế Resilience / Fallback về `Unclassified` khi lỗi mạng hoặc timeout.
  - API kiểm thử độc lập: `POST /api/ai/analyze`.

- [ ] **Task 3.2: Thuật toán Truy vấn Không gian Địa lý (Geospatial Query / PostGIS)** `[🔴 Chưa làm]`
  - Thuật toán tính toán khoảng cách không gian (Haversine / PostGIS `ST_DWithin`, `ST_Distance`).
  - Tìm kiếm toàn bộ trạm hoặc xe cứu hộ khả dụng (`Status == Available`) trong bán kính $R$ (km) từ hiện trường sự cố.
  - Tính khoảng cách (km) và thời gian dự kiến tiếp cận (ETA).
  - API `GET /api/dispatch/nearby-units?incidentId={id}&radiusKm=10`.

- [ ] **Task 3.3: So sánh Thuật toán Điều phối (Nearest-Available vs. Load-Aware)** `[🔴 Chưa làm]`
  - Xây dựng **Dispatch Engine** hỗ trợ 2 thuật toán phục vụ mục tiêu nghiên cứu:
    1. **Nearest-Available**: Chọn đơn vị rảnh gần nhất theo khoảng cách/ETA.
    2. **Load-Aware**: Đánh giá kết hợp khoảng cách + khối lượng công việc hiện tại của trạm/xe (tỉ lệ bận, số ca trong ngày, độ mệt mỏi).
  - API `GET /api/dispatch/recommendations?incidentId={id}`: Trả về kết quả gợi ý kèm bảng so sánh chỉ số giữa 2 thuật toán.
  - API `POST /api/dispatch/assign`: Thực hiện gán xe vào sự cố, chuyển trạng thái xe & sự cố sang `Dispatched`.

- [ ] **Task 3.4: Bộ Dữ liệu Kiểm chứng & Báo cáo Đánh giá AI** `[🔴 Chưa làm]`
  - Xây dựng bộ dữ liệu kiểm chứng chuẩn (Ground-Truth Validation Set gồm ảnh + nhãn chuyên gia).
  - Pipeline chạy benchmark và tính toán các chỉ số nghiên cứu khoa học:
    - **Accuracy** (Độ chính xác phân loại mức độ nghiêm trọng).
    - **Precision, Recall, F1-Score** (Macro-F1 & Weighted-F1).
    - **Cohen's Kappa ($\kappa$)** (Hệ số đồng thuận giữa AI và chuyên gia).
    - **Processing Latency** (Độ trễ trung bình, Min, Max, P95 tính bằng mili-giây).
  - API xuất báo cáo số liệu thực nghiệm (`POST /api/ai/benchmark/run`, `GET /api/ai/benchmark/report`) để đưa vào slide thuyết trình và luận văn tốt nghiệp.
