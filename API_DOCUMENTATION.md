# 📘 TÀI LIỆU TÍCH HỢP BACKEND API — DÀNH CHO ĐỘI NGŨ FRONTEND & MOBILE

> **Dự án**: Hệ thống Điều phối và Quản lý Đội cứu hộ Khẩn cấp theo Thời gian thực (Real-time Emergency Response & Dispatch Management System)  
> **Phiên bản API**: v1.0 (Clean Architecture - .NET 9.0)  
> **Cơ sở dữ liệu**: Neon PostgreSQL Cloud  
> **Mô hình AI**: Qwen3-VL 32B Instruct (`qwen/qwen3-vl-32b-instruct`)

---

## 1. MÔI TRƯỜNG & ĐỊA CHỈ TRUY CẬP (ENVIRONMENT)

* **Base URL (HTTP)**: `http://localhost:5000`
* **Base URL (HTTPS)**: `https://localhost:7132`
* **Swagger UI (Giao diện test trực quan)**: `http://localhost:5000/swagger`
* **CORS**: Đã cấu hình `AllowAll` (Hỗ trợ gọi từ Web `http://localhost:5173`, `http://localhost:3000` hoặc Mobile Expo không bị chặn).

### Chuẩn định dạng Response chung (`ApiResponseDto<T>`)
Mọi API trả về đều tuân thủ cấu trúc JSON đồng nhất:
```json
{
  "success": true,
  "message": "Thông điệp phản hồi từ máy chủ",
  "data": { ... },
  "errors": null
}
```
* Khi thất bại (`success: false`), trường `message` chứa nguyên nhân và `errors` chứa danh sách chi tiết các lỗi kiểm tra dữ liệu (validation).

### Cơ chế Xác thực (Authentication)
* Sử dụng chuẩn **JWT Bearer Token**.
* Đối với các API yêu cầu đăng nhập, FE gửi header:
  ```http
  Authorization: Bearer <access_token>
  ```

---

## 2. TÀI KHOẢN MẪU ĐỂ TEST (TEST ACCOUNTS)

Hệ thống đã tự động seed sẵn dữ liệu mẫu lên cơ sở dữ liệu cloud Neon. FE/Mobile có thể dùng trực tiếp để đăng nhập:

| Vai trò (Role) | Email | Mật khẩu | Mục đích sử dụng |
|---|---|---|---|
| **Admin (Mới)** | `admin@gmail.com` | `123123` | Quản trị viên hệ thống (Đã cấu hình & kích hoạt) |
| **Admin** | `admin@emergencydispatch.com` | `Admin@123456` | Quản trị viên hệ thống, quản lý tài khoản/trạm |
| **Operator** | `operator@emergencydispatch.com` | `Operator@123456` | Điều phối viên (Xem hàng đợi, duyệt gợi ý AI) |
| **RescueStaff**| `staff@emergencydispatch.com` | `Staff@123456` | Đội cứu hộ (Nhận nhiệm vụ, cập nhật trạng thái) |
| **Citizen** | `citizen@emergencydispatch.com` | `Citizen@123456` | Người dân (Gửi SOS, theo dõi đội cứu hộ) |

---

## 3. DANH SÁCH CHI TIẾT CÁC API ĐÃ HOÀN THÀNH

### PHÂN HỆ 1: XÁC THỰC & TÀI KHOẢN (AUTH & USERS)

#### 1.1. Đăng nhập (`POST /api/auth/login`)
* **Mô tả**: Đăng nhập bằng Email và Mật khẩu, nhận cặp Access Token và Refresh Token.
* **Yêu cầu đăng nhập**: Không (Public)
* **Request Body**:
  ```json
  {
    "email": "admin@emergencydispatch.com",
    "password": "Admin@123456"
  }
  ```
* **Response (200 OK)**:
  ```json
  {
    "success": true,
    "message": "Đăng nhập thành công.",
    "data": {
      "accessToken": "eyJhbGciOiJIUzI1Ni...",
      "refreshToken": "by7pMrlYA5ze86...",
      "expiresAt": "2026-09-12T12:00:00Z",
      "user": {
        "id": "2d2b488a-1082-4806-9881-6e80dc6bfd3c",
        "fullName": "System Administrator",
        "email": "admin@emergencydispatch.com",
        "phoneNumber": "0901234567",
        "avatarUrl": null,
        "role": "Admin",
        "status": "Active",
        "stationId": null,
        "stationName": null
      }
    },
    "errors": null
  }
  ```

#### 1.2. Đăng ký tài khoản Người dân (`POST /api/auth/register`)
* **Mô tả**: Tạo tài khoản Citizen mới. Hệ thống tự động sinh mã OTP 6 số và gửi email xác nhận.
* **Yêu cầu đăng nhập**: Không (Public)
* **Request Body**:
  ```json
  {
    "fullName": "Nguyễn Văn A",
    "email": "nguyenvana@gmail.com",
    "password": "Password@123",
    "phoneNumber": "0912345678"
  }
  ```
* **Response (200 OK)**: Trả về Access Token + Refresh Token (Tài khoản được tạo với `isEmailVerified: false`).

#### 1.3. Xác thực Email bằng mã OTP (`POST /api/auth/verify-email`)
* **Mô tả**: Người dùng nhập mã OTP 6 số nhận được trong email để kích hoạt tài khoản. Hệ thống tự động gửi **Welcome Email** hướng dẫn cập nhật hồ sơ y tế cứu hộ khẩn cấp.
* **Request Body**:
  ```json
  {
    "email": "nguyenvana@gmail.com",
    "token": "123456"
  }
  ```

#### 1.4. Gửi lại mã xác thực Email (`POST /api/auth/resend-verification`)
* **Request Body**:
  ```json
  {
    "email": "nguyenvana@gmail.com"
  }
  ```

#### 1.5. Quên mật khẩu (`POST /api/auth/forgot-password`)
* **Mô tả**: Gửi mã OTP/Token đặt lại mật khẩu về hộp thư người dùng (hiệu lực 10 phút).
* **Request Body**:
  ```json
  {
    "email": "nguyenvana@gmail.com"
  }
  ```

#### 1.6. Đặt lại mật khẩu mới (`POST /api/auth/reset-password`)
* **Mô tả**: Đặt lại mật khẩu mới bằng mã OTP nhận từ email. Toàn bộ phiên đăng nhập cũ sẽ bị thu hồi.
* **Request Body**:
  ```json
  {
    "email": "nguyenvana@gmail.com",
    "token": "654321",
    "newPassword": "NewPassword@123",
    "confirmPassword": "NewPassword@123"
  }
  ```

#### 1.7. Đổi mật khẩu (`POST /api/auth/change-password`)
* **Header**: `Authorization: Bearer <token>`
* **Mô tả**: Đổi mật khẩu tài khoản đang đăng nhập. **Hệ thống tự động gửi Email cảnh báo bảo mật thời gian thực** kèm thời gian và địa chỉ IP thực hiện.
* **Request Body**:
  ```json
  {
    "currentPassword": "Password@123",
    "newPassword": "NewPassword@456"
  }
  ```

#### 1.8. Làm mới Token (`POST /api/auth/refresh`)
* **Mô tả**: Cấp Access Token mới khi Access Token cũ hết hạn mà không bắt người dùng đăng nhập lại.
* **Request Body**:
  ```json
  {
    "accessToken": "ey...",
    "refreshToken": "by..."
  }
  ```

#### 1.9. Đăng xuất (`POST /api/auth/logout`)
* **Mô tả**: Vô hiệu hóa Refresh Token trên database Neon.
* **Request Body**:
  ```json
  {
    "refreshToken": "by..."
  }
  ```

#### 1.10. Lấy thông tin cá nhân & Hồ sơ Y tế Khẩn cấp (`GET /api/users/me`)
* **Header**: `Authorization: Bearer <token>`
* **Response (200 OK)**:
  ```json
  {
    "success": true,
    "message": "Lấy thông tin cá nhân thành công.",
    "data": {
      "id": "70d2a8b9-...",
      "fullName": "Lê Người Dân",
      "email": "citizen@emergencydispatch.com",
      "phoneNumber": "0904567890",
      "avatarUrl": null,
      "role": "Citizen",
      "status": "Active",
      "isEmailVerified": true,
      "dateOfBirth": "1995-05-15T00:00:00Z",
      "gender": 0,
      "citizenIdNumber": "079095012345",
      "address": "789 Điện Biên Phủ, Phường 22, Quận Bình Thạnh, TP. Hồ Chí Minh",
      "bloodType": 7,
      "medicalNotes": "Dị ứng thuốc kháng sinh nhóm Penicillin; Tiền sử huyết áp thấp.",
      "emergencyContactName": "Lê Thị Thân",
      "emergencyContactPhone": "0911223344",
      "emergencyContactRelationship": "Vợ",
      "fcmToken": null,
      "stationId": null,
      "stationName": null
    }
  }
  ```

#### 1.11. Cập nhật Hồ sơ Y tế Cứu hộ Khẩn cấp (`PUT /api/users/me`)
* **Header**: `Authorization: Bearer <token>`
* **Mô tả**: Cập nhật thông tin nhân thân, nhóm máu (1: A+, 2: A-, 3: B+, 4: B-, 5: AB+, 6: AB-, 7: O+, 8: O-), tiền sử bệnh án và người liên hệ khẩn cấp.
* **Request Body**:
  ```json
  {
    "fullName": "Lê Người Dân",
    "phoneNumber": "0904567890",
    "avatarUrl": "https://res.cloudinary.com/...",
    "dateOfBirth": "1995-05-15T00:00:00Z",
    "gender": 0,
    "citizenIdNumber": "079095012345",
    "address": "789 Điện Biên Phủ, Phường 22, Quận Bình Thạnh, TP. Hồ Chí Minh",
    "bloodType": 7,
    "medicalNotes": "Dị ứng thuốc kháng sinh nhóm Penicillin; Tiền sử huyết áp thấp.",
    "emergencyContactName": "Lê Thị Thân",
    "emergencyContactPhone": "0911223344",
    "emergencyContactRelationship": "Vợ"
  }
  ```

#### 1.12. Cập nhật FCM Token (`PUT /api/users/fcm-token`)
* **Header**: `Authorization: Bearer <token>`
* **Mô tả**: Đăng ký FCM Device Token để nhận Push Notification âm thanh còi hú khẩn cấp cho Mobile App.
* **Request Body**:
  ```json
  {
    "fcmToken": "f7dK9L2xP0w:APA91bH..."
  }
  ```

---

### PHÂN HỆ 2: BÁO CÁO SỰ CỐ KHẨN CẤP & ĐIỀU PHỐI (INCIDENTS & CAD QUEUE)

#### 2.1. Gửi báo cáo sự cố SOS (`POST /api/incidents`)
* **Mô tả**: Tạo sự cố mới. Hỗ trợ **người dân chưa đăng nhập (khách vãng lai)** hoặc **đã đăng nhập**.
  * Hệ thống tự động kích hoạt mô hình Vision-Language **Qwen3-VL** để nhận diện ảnh hiện trường, gán nhãn nguy cơ (`hazardTags`) và tự động xếp hạng mức độ nguy cấp (`severity` từ 1 - 5).
  * Nếu AI lỗi mạng/timeout, hệ thống tự động Fallback về mức độ `Unclassified` (Mức 0) và đẩy lên hàng đợi của Operator để xem xét thủ công.
* **Request Body**:
  ```json
  {
    "title": "Hỏa hoạn khu dân cư",
    "description": "Thấy khói đen bốc lên nghi ngút từ tầng 2, nghi có người mắc kẹt bên trong",
    "latitude": 10.776889,
    "longitude": 106.700806,
    "locationAddress": "123 Lê Lợi, P. Bến Thành, Quận 1, TP.HCM",
    "reporterName": "Trần Văn Dân",
    "reporterPhone": "0909123456",
    "mediaUrls": [
      "https://images.unsplash.com/photo-1542382257-80dedb725088?w=800"
    ]
  }
  ```
* **Response (200 OK)**:
  ```json
  {
    "success": true,
    "message": "Báo cáo sự cố đã được tiếp nhận và phân tích rủi ro thành công.",
    "data": {
      "id": "f8a7e584-3c6d-4b82-9f12-9c3f0b2a7d4e",
      "title": "Hỏa hoạn khu dân cư",
      "description": "Thấy khói đen bốc lên nghi ngút...",
      "latitude": 10.776889,
      "longitude": 106.700806,
      "locationAddress": "123 Lê Lợi, P. Bến Thành, Quận 1, TP.HCM",
      "status": "AiProcessed",
      "severity": "Level4",
      "reporterName": "Trần Văn Dân",
      "reporterPhone": "0909123456",
      "createdAt": "2026-09-05T14:30:00Z",
      "mediaItems": [
        {
          "id": "...",
          "mediaUrl": "https://images.unsplash.com/...",
          "mediaType": "Photo"
        }
      ],
      "aiClassification": {
        "hazardTags": ["fire", "heavy_smoke", "residential_area"],
        "severityScore": 4,
        "summary": "Phát hiện đám cháy bùng phát tại khu vực nhà ở kèm khói đặc, nguy cơ cháy lan cao.",
        "confidenceScore": 0.92,
        "modelName": "qwen/qwen3-vl-32b-instruct",
        "isSuccess": true,
        "processingDurationMs": 420
      }
    }
  }
  ```

#### 2.2. Hàng đợi sự cố chờ duyệt dành cho Điều phối viên (`GET /api/incidents/queue`)
* **Mô tả**: Lấy danh sách sự cố chưa duyệt (`Pending` hoặc `AiProcessed`).
* **Thuật toán ưu tiên hiển thị (Priority Ordering)**:
  1. Các sự cố **`Unclassified` (Mức 0 - do AI không phân loại được hoặc gặp lỗi)** luôn được đẩy **lên đầu trang** để Operator thẩm tra thủ công khẩn cấp.
  2. Tiếp theo là các sự cố nguy hiểm giảm dần: **Mức 5 $\rightarrow$ Mức 4 $\rightarrow$ Mức 3 $\rightarrow$ Mức 2 $\rightarrow$ Mức 1**.
  3. Cùng mức độ thì ưu tiên sự cố gửi trước (FIFO).
* **Response (200 OK)**: Danh sách mảng các sự cố kèm đầy đủ ảnh và phân tích AI.

#### 2.3. Lấy chi tiết sự cố (`GET /api/incidents/{id}`)
* **Mô tả**: Xem thông tin chi tiết một sự cố theo GUID.

#### 2.4. Danh sách sự cố có phân trang & bộ lọc (`GET /api/incidents`)
* **Query Parameters**:
  * `pageIndex`: Số trang (mặc định: 1)
  * `pageSize`: Số phần tử/trang (mặc định: 10, tối đa: 50)
  * `status`: Lọc theo trạng thái (`Pending`, `AiProcessed`, `Verified`, `Dispatched`, ...)
  * `severity`: Lọc theo mức độ (`Unclassified`, `Level1`, ..., `Level5`)
  * `searchTerm`: Tìm kiếm theo từ khóa trong tiêu đề, mô tả, địa chỉ
* **Response (200 OK)**:
  ```json
  {
    "success": true,
    "data": {
      "items": [ ... ],
      "pageIndex": 1,
      "pageSize": 10,
      "totalCount": 42,
      "totalPages": 5,
      "hasPreviousPage": false,
      "hasNextPage": true
    }
  }
  ```

#### 2.5. Điều phối viên xác minh sự cố (`PUT /api/incidents/{id}/verify`)
* **Mô tả**: Cơ chế **Human-in-the-loop**: Operator xác nhận hoặc ghi đè (override) mức độ nghiêm trọng mà AI đã gợi ý trước khi phát lệnh điều phối xe cứu hộ.
* **Header**: `Authorization: Bearer <token_cua_operator>`
* **Request Body**:
  ```json
  {
    "confirmedSeverity": "Level4",
    "adjustedTitle": "Cháy lớn tại số 123 Lê Lợi, Quận 1 (Đã xác minh)",
    "operatorNotes": "Đã liên hệ người dân qua SĐT 0909123456 xác nhận có 2 người bị kẹt tầng 2."
  }
  ```
* **Response (200 OK)**: Sự cố chuyển sang trạng thái `Verified`.

#### 2.6. Hủy sự cố báo sai / báo khống (`PUT /api/incidents/{id}/cancel`)
* **Header**: `Authorization: Bearer <token_cua_operator>`
* **Request Body**:
  ```json
  "Báo khống, đã gọi lại xác minh không có hiện trường"
  ```
* **Response (200 OK)**: Sự cố chuyển sang trạng thái `Cancelled`.

---

### PHÂN HỆ 3: TẢI LÊN HÌNH ẢNH / VIDEO (MEDIA UPLOAD)

#### 3.1. Upload tệp tin lên Cloudinary (`POST /api/media/upload`)
* **Mô tả**: Tải ảnh hoặc video hiện trường lên Cloudinary, trả về link trực tiếp có HTTPS.
* **Content-Type**: `multipart/form-data`
* **Form Field**: `file` (tệp tin binary)
* **Quy tắc kiểm tra (Validation Rule)**:
  * **Hình ảnh**: Tối đa **10 MB** (Định dạng: `image/jpeg`, `image/png`, `image/webp`).
  * **Video**: Tối đa **30 MB** (Định dạng: `video/mp4`, `video/quicktime`).
* **Response (200 OK)**:
  ```json
  {
    "success": true,
    "message": "Tải tệp tin lên thành công.",
    "data": {
      "url": "https://res.cloudinary.com/dignpno2i/image/upload/v1725540000/emergency_dispatch/images/sample.jpg",
      "publicId": "emergency_dispatch/images/sample",
      "mediaType": "Photo",
      "fileSizeBytes": 2048500,
      "mimeType": "image/jpeg"
    }
  }
  ```

---

### PHÂN HỆ 4: KIỂM THỬ ĐỘC LẬP MÔ HÌNH AI (AI DIRECT EVALUATION)

#### 4.1. Phân tích ảnh với Qwen3-VL (`POST /api/ai/analyze`)
* **Mô tả**: Phục vụ trang giới thiệu công nghệ (Demo Showcase) hoặc kiểm thử độc lập mà không cần tạo bản ghi sự cố.
* **Request Body**:
  ```json
  {
    "mediaUrl": "https://images.unsplash.com/photo-1542382257-80dedb725088?w=800",
    "additionalContext": "Khu vực dân cư đông đúc, gần trạm xăng"
  }
  ```
* **Response (200 OK)**:
  ```json
  {
    "success": true,
    "message": "Phân tích hiện trường bằng AI hoàn tất thành công.",
    "data": {
      "hazardTags": ["fire", "gas_station_nearby", "high_risk"],
      "severityScore": 5,
      "summary": "Phát hiện đám cháy lan rộng gần trạm xăng dầu, nguy cơ cháy nổ cực kỳ nguy hiểm.",
      "confidenceScore": 0.96,
      "modelName": "qwen/qwen3-vl-32b-instruct",
      "isSuccess": true,
      "processingDurationMs": 380
    }
  }
  ```

---


### PHÂN HỆ 5: ĐIỀU PHỐI CỨU HỘ THÔNG MINH (DISPATCH & RECOMMENDER ENGINE)

#### 5.1. Lấy danh sách đội cứu hộ gợi ý thông minh (`GET /api/dispatch/incidents/{incidentId}/recommendations`)
* **Endpoint**: `GET /api/dispatch/incidents/{incidentId}/recommendations?algorithm=NearestAvailable&maxUnits=5`
* **Quyền hạn**: `Operator`, `Admin`
* **Query Parameters**:
  * `algorithm` (tùy chọn): Thuật toán gợi ý (`NearestAvailable` [mặc định theo khoảng cách Haversine] hoặc `LoadAware` [cân bằng tải dựa trên số lượng sự cố đang xử lý]).
  * `maxUnits` (tùy chọn, mặc định 5): Số lượng đội cứu hộ gợi ý tối đa.
* **Mô tả**: Hệ thống tự động lọc các đơn vị cứu hộ có chủng loại phù hợp (Ambulance, FireTruck, PoliceVehicle, RescueBoat) theo mức độ nghiêm trọng và loại sự cố, tính toán khoảng cách địa lý thời gian thực, thời gian di chuyển ước tính (ETA) và điểm số phù hợp (MatchScore từ 0 đến 100).
* **Response**: `200 OK`
```json
{
  "success": true,
  "message": "Lấy danh sách gợi ý thành công",
  "data": [
    {
      "rescueUnitId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "plateNumber": "51A-115.01",
      "unitType": "Ambulance",
      "status": "Available",
      "distanceKm": 1.45,
      "estimatedArrivalMinutes": 4.5,
      "matchScore": 95.0,
      "recommendationReason": "Đội cứu thương gần nhất (1.45 km, ETA ~4.5 phút)",
      "currentActiveAssignments": 0,
      "stationName": "Trạm Cứu hộ Trung tâm Quận 1"
    }
  ]
}
```

#### 5.2. Điều phối đội cứu hộ tới sự cố (`POST /api/dispatch/assignments`)
* **Endpoint**: `POST /api/dispatch/assignments`
* **Quyền hạn**: `Operator`, `Admin`
* **Mô tả**: Điều phối viên phê duyệt gửi một hoặc nhiều đội cứu hộ tới hiện trường. Hệ thống chuyển trạng thái sự cố sang `Dispatched`, tạo lệnh điều phối và phát sự kiện Realtime qua SignalR tới cả Web CAD và App Mobile cứu hộ.
* **Body Request**:
```json
{
  "incidentId": "d3b07384-d113-40e1-bbbb-b9961a9ee001",
  "rescueUnitIds": [
    "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  ],
  "dispatcherNotes": "Ưu tiên cấp cứu nạn nhân bỏng tầng 2, tiếp cận từ cổng Đông."
}
```
* **Response**: `200 OK`
```json
{
  "success": true,
  "message": "Đã điều phối 1 đội cứu hộ thành công.",
  "data": [
    {
      "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "incidentId": "d3b07384-d113-40e1-bbbb-b9961a9ee001",
      "rescueUnitId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "status": "Assigned",
      "dispatchedAt": "2026-10-18T14:30:00Z"
    }
  ]
}
```

#### 5.3. Xem danh sách nhiệm vụ của Đội cứu hộ (`GET /api/dispatch/assignments/my-missions`)
* **Endpoint**: `GET /api/dispatch/assignments/my-missions`
* **Quyền hạn**: `RescueStaff`, `Admin`
* **Mô tả**: Dành cho App Mobile Đội cứu hộ. Trả về toàn bộ nhiệm vụ đang được phân công cho xe/trạm trực thuộc nhân viên cứu hộ đang đăng nhập.
* **Response**: `200 OK`

#### 5.4. Cập nhật trạng thái nhiệm vụ cứu hộ (`PUT /api/dispatch/assignments/{assignmentId}/status`)
* **Endpoint**: `PUT /api/dispatch/assignments/{assignmentId}/status`
* **Quyền hạn**: `RescueStaff`, `Operator`, `Admin`
* **Mô tả**: Đội cứu hộ cập nhật tiến trình di chuyển theo vòng đời chuẩn:
  * `EnRoute`: Đang trên đường tới hiện trường.
  * `OnScene`: Đã có mặt tại hiện trường sự cố.
  * `Completed`: Đã hoàn tất nhiệm vụ cứu nạn.
  * `Cancelled`: Lệnh điều phối bị hủy.
* **Body Request**:
```json
{
  "status": "OnScene",
  "notes": "Đã tiếp cận hiện trường, đang triển khai đội hình sơ cứu."
}
```
* **Response**: `200 OK`

#### 5.5. Hoàn tất xử lý sự cố (`POST /api/dispatch/incidents/{incidentId}/resolve`)
* **Endpoint**: `POST /api/dispatch/incidents/{incidentId}/resolve`
* **Quyền hạn**: `Operator`, `Admin`
* **Mô tả**: Đóng sự cố khẩn cấp, giải phóng toàn bộ các phương tiện cứu hộ về trạng thái `Available` (Sẵn sàng) và ghi nhận thời gian phản ứng tổng thể.
* **Body Request**:
```json
{
  "resolutionSummary": "Đã dập tắt đám cháy hoàn toàn lúc 14:45. 2 nạn nhân đã được đưa vào Bệnh viện Quận 1 an toàn."
}
```
* **Response**: `200 OK`

---

### PHÂN HỆ 6: CẢNH BÁO ĐIỂM NÓNG GIAO THÔNG (TRAFFIC HOTSPOTS)

#### 6.1. Báo cáo điểm nóng ùn tắc / sự cố giao thông (`POST /api/traffic/hotspots`)
* **Endpoint**: `POST /api/traffic/hotspots`
* **Quyền hạn**: `Operator`, `RescueStaff`, `Admin`
* **Mô tả**: Cảnh báo điểm kẹt xe, tai nạn hoặc ngập lụt cản trở xe cứu hộ. Điểm nóng có cơ chế tự động hết hạn (TTL) và được worker `HotspotCleanupWorker` dọn dẹp ngầm.
* **Body Request**:
```json
{
  "title": "Ùn tắc nghiêm trọng tại ngã tư D1 - D2",
  "description": "Va chạm giữa 2 xe tải gây chắn toàn bộ làn đường hướng vào Lô E",
  "latitude": 10.8415,
  "longitude": 106.8105,
  "radiusMeters": 350.0,
  "severity": "High",
  "durationMinutes": 60
}
```
* **Response**: `200 OK` (Trả về chi tiết Hotspot ID và thời gian hết hạn `expiresAt`).

#### 6.2. Lấy danh sách điểm nóng giao thông đang hoạt động (`GET /api/traffic/hotspots/active`)
* **Endpoint**: `GET /api/traffic/hotspots/active`
* **Quyền hạn**: Công khai (Có thể gọi từ cả Web CAD và App Mobile)
* **Mô tả**: Trả về toàn bộ các điểm nóng còn hiệu lực để hiển thị lớp bản đồ nhiệt (Heatmap) hoặc vẽ bán kính đỏ cảnh báo lái xe cứu hộ tránh tuyến đường nghẽn.
* **Response**: `200 OK`

---

### PHÂN HỆ 7: CẤU HÌNH LEO THANG SỰ CỐ TỰ ĐỘNG (ESCALATION RULE ENGINE)

#### 7.1. Lấy danh sách quy tắc leo thang tự động (`GET /api/escalation/rules`)
* **Endpoint**: `GET /api/escalation/rules`
* **Quyền hạn**: `Admin`, `Operator`
* **Mô tả**: Xem toàn bộ các quy tắc leo thang sự cố đang được cấu hình trên hệ thống.
* **Response**: `200 OK`

#### 7.2. Tạo mới quy tắc leo thang tự động (`POST /api/escalation/rules`)
* **Endpoint**: `POST /api/escalation/rules`
* **Quyền hạn**: `Admin`
* **Mô tả**: Thiết lập quy tắc cho background worker `EscalationWorker`. Khi có sự cố mức độ nghiêm trọng `>= minSeverityLevel` không được tiếp nhận sau `timeoutMinutes`, hệ thống tự động đổi trạng thái sự cố sang `Escalated` và phát chuông cảnh báo tới các vai trò trong `notifyRoles`.
* **Body Request**:
```json
{
  "description": "Leo thang khẩn cấp sự cố Cấp 3 chưa duyệt quá 5 phút",
  "minSeverityLevel": "Level3",
  "timeoutMinutes": 5,
  "notifyRoles": "["Admin","Operator"]",
  "expandRadiusPercent": 50,
  "isActive": true
}
```
* **Response**: `200 OK`

#### 7.3. Cập nhật quy tắc leo thang (`PUT /api/escalation/rules/{id}`)
* **Endpoint**: `PUT /api/escalation/rules/{id}`
* **Quyền hạn**: `Admin`
* **Body Request**: Tương tự `POST`, có thể bật/tắt `isActive: false` để vô hiệu hóa quy tắc tức thời.

#### 7.4. Xóa mềm quy tắc leo thang (`DELETE /api/escalation/rules/{id}`)
* **Endpoint**: `DELETE /api/escalation/rules/{id}`
* **Quyền hạn**: `Admin`
* **Response**: `200 OK`

#### 7.5. Xem lịch sử kích hoạt leo thang (`GET /api/escalation/logs`)
* **Endpoint**: `GET /api/escalation/logs?pageIndex=1&pageSize=20`
* **Quyền hạn**: `Admin`, `Operator`
* **Mô tả**: Xem nhật ký chi tiết các sự cố đã bị worker kích hoạt leo thang khẩn cấp kèm thời gian và hành động xử lý.
* **Response**: `200 OK`

---

### PHÂN HỆ 8: ĐA NGÔN NGỮ (BILINGUAL LOCALIZATION `vi` / `en`)

#### 8.1. Kiểm tra ngôn ngữ hiện tại của Client (`GET /api/localization/info`)
* **Endpoint**: `GET /api/localization/info`
* **Quyền hạn**: Công khai
* **Cơ chế xác định ngôn ngữ của Backend**:
  1. Header HTTP: `Accept-Language: en` (hoặc `vi`)
  2. Query string: `?culture=en` (hoặc `?culture=vi`)
  3. Mặc định: Tự động fallback về Tiếng Việt (`vi`).
* **Response**: `200 OK`
```json
{
  "success": true,
  "message": "Current culture information retrieved successfully",
  "data": {
    "currentCulture": "en",
    "supportedCultures": [
      { "code": "vi", "name": "Tiếng Việt (Vietnamese)" },
      { "code": "en", "name": "English (US)" }
    ]
  }
}
```

#### 8.2. Lấy toàn bộ từ điển thông báo chuẩn hai ngôn ngữ (`GET /api/localization/catalog`)
* **Endpoint**: `GET /api/localization/catalog`
* **Quyền hạn**: Công khai
* **Mô tả**: Trả về toàn bộ danh mục mã thông báo và lỗi đa ngôn ngữ của hệ thống để Frontend và Mobile đồng bộ giao diện.

---

### PHÂN HỆ 9: KẾT NỐI REAL-TIME WEBSOCKET (SIGNALR HUBS)

Backend cung cấp 2 SignalR Hubs chuyên dụng đáp ứng tiêu chuẩn độ trễ $1–3	ext{ giây}$:

#### 9.1. Tracking Hub — Định vị GPS thời gian thực (`/hubs/tracking`)
* **URL**: `http://localhost:5000/hubs/tracking?access_token=<JWT_TOKEN>`
* **Mục đích**: Đồng bộ vị trí xe cứu hộ và nhân sự hiện trường lên bản đồ số Dispatcher CAD.
* **Client phát dữ liệu lên Server (Invoke)**:
  * Phương thức: `SendLocationUpdate(double latitude, double longitude, double? speed, double? heading)`
* **Client lắng nghe từ Server (On)**:
  * Sự kiện: `ReceiveLocationUpdate` $ightarrow$ Nhận payload `{ rescueUnitId, latitude, longitude, speed, heading, updatedAt }`.
  * Sự kiện: `ReceiveRescueUnitStatus` $ightarrow$ Nhận payload `{ rescueUnitId, status, updatedAt }`.

#### 9.2. Incident Hub — Hàng đợi sự cố khẩn cấp (`/hubs/incidents`)
* **URL**: `http://localhost:5000/hubs/incidents?access_token=<JWT_TOKEN>`
* **Mục đích**: Broadcast tức thời khi có sự cố SOS mới, sự cố được duyệt, hoặc lệnh điều phối.
* **Client lắng nghe từ Server (On)**:
  * `NewIncident`: Sự cố SOS mới phát sinh (kèm chuông cảnh báo đỏ trên bàn CAD).
  * `IncidentVerified`: Sự cố đã được Operator thẩm định.
  * `IncidentStatusUpdated`: Cập nhật trạng thái sự cố.
  * `IncidentEscalated`: Cảnh báo khẩn cấp sự cố bị leo thang do quá 5 phút chưa nhận.
  * `DispatchAssigned`: Thông báo nhiệm vụ mới tới điện thoại của đội cứu hộ được chọn.

## 4. BẢNG TRA CỨU ENUMS (DÀNH CHO TYPESCRIPT / INTERFACES)

FE copy các enum này vào mã nguồn Frontend / Mobile để khớp 100% kiểu dữ liệu:

```typescript
// Mức độ nghiêm trọng của sự cố
export enum SeverityLevel {
  Unclassified = "Unclassified", // 0: AI lỗi hoặc chưa phân loại (Cần Operator xem xét khẩn)
  Level1 = "Level1",             // 1: Rất thấp
  Level2 = "Level2",             // 2: Thấp
  Level3 = "Level3",             // 3: Trung bình
  Level4 = "Level4",             // 4: Cao (Hỏa hoạn lớn, tai nạn nghiêm trọng)
  Level5 = "Level5",             // 5: Cực kỳ khẩn cấp / Thảm họa
}

// Trạng thái sự cố
export enum IncidentStatus {
  Pending = "Pending",           // Vừa gửi, đang chờ xử lý
  AiProcessing = "AiProcessing", // Đang được AI phân tích
  AiProcessed = "AiProcessed",   // AI đã phân tích xong, chờ Operator duyệt
  Verified = "Verified",         // Operator đã xác minh (Sẵn sàng điều phối)
  Dispatched = "Dispatched",     // Đã phân công đội cứu hộ
  InProgress = "InProgress",     // Đội đang đến hoặc xử lý tại hiện trường
  Completed = "Completed",       // Đã giải quyết xong
  Cancelled = "Cancelled",       // Hủy sự cố (báo sai/trùng)
  Escalated = "Escalated",       // Bị leo thang lên cấp cao hơn
}

// Vai trò người dùng
export enum UserRole {
  Citizen = "Citizen",           // Người dân
  Operator = "Operator",         // Điều phối viên
  RescueStaff = "RescueStaff",   // Nhân viên đội cứu hộ
  Admin = "Admin",               // Quản trị viên
}

// Loại phương tiện truyền thông
export enum MediaType {
  Photo = "Photo",
  Video = "Video",
}
```

---

### PHÂN HỆ 10: QUẢN TRỊ VIÊN & THỐNG KÊ DASHBOARD (ADMIN STATS & BACK-OFFICE)

#### 10.1. Lấy toàn bộ số liệu thống kê & KPIs vận hành (`GET /api/dashboard/stats`)
* **Endpoint**: `GET /api/dashboard/stats`
* **Quyền hạn**: `Admin`, `Operator`
* **Mô tả**: Trả về bức tranh toàn cảnh của hệ thống phục vụ Dashboard điều hành trung tâm:
  - **Sự cố**: Tổng số, đang kích hoạt, chờ xử lý, đang điều phối, đã giải quyết, hủy, leo thang.
  - **Mức độ nghiêm trọng**: Đếm số lượng sự cố Cấp 3 (Thảm họa/Khẩn cấp), Cấp 2 (Trung bình), Cấp 1 (Nhẹ).
  - **Phương tiện cứu hộ**: Tổng số xe, đang sẵn sàng (Available), đang làm nhiệm vụ (OnMission), đang bảo dưỡng (Maintenance).
  - **Hạ tầng trạm**: Tổng số trạm, số trạm đang hoạt động (Active).
  - **Nhân sự**: Tổng tài khoản, phân bổ Điều phối viên (Operator), Cứu hộ (RescueStaff), Người dân (Citizen), Quản trị viên (Admin).
  - **Chỉ số hiệu năng (KPIs)**: Thời gian phản hồi trung bình (phút), Tỷ lệ giải quyết hoàn tất sự cố (%).
  - **Sự cố gần đây**: Top các sự cố mới nhất kèm trạng thái và vị trí.
* **Response**: `200 OK`
```json
{
  "success": true,
  "message": "Lấy số liệu thống kê quản trị thành công.",
  "data": {
    "totalIncidents": 15,
    "activeIncidents": 12,
    "pendingIncidents": 6,
    "dispatchedIncidents": 1,
    "resolvedIncidents": 0,
    "cancelledIncidents": 3,
    "escalatedIncidents": 0,
    "criticalLevel3Incidents": 0,
    "moderateLevel2Incidents": 2,
    "minorLevel1Incidents": 2,
    "totalRescueUnits": 6,
    "availableUnits": 5,
    "onMissionUnits": 1,
    "maintenanceUnits": 0,
    "totalStations": 2,
    "activeStations": 2,
    "totalUsers": 11,
    "totalCitizens": 7,
    "totalOperators": 1,
    "totalRescueStaff": 1,
    "totalAdmins": 2,
    "averageResponseTimeMinutes": 0.0,
    "incidentResolutionRatePercent": 0.0,
    "recentIncidents": [
      {
        "id": "2df43391-ba46-4259-945b-1c4d6f3afc2b",
        "title": "Báo động SOS Khẩn cấp",
        "severity": "Unclassified",
        "status": "AiProcessed",
        "locationAddress": "Phường Long Trường, Thành phố Hồ Chí Minh",
        "createdAt": "2026-09-29T08:42:24.806676Z"
      }
    ]
  }
}
```

---

## 5. CODE MẪU TÍCH HỢP AXIOS (CLIENT CODE EXAMPLE)

### 5.1. Khởi tạo Axios Instance có gắn Token tự động
```typescript
import axios from 'axios';

const api = axios.create({
  baseURL: 'http://localhost:5000/api', // Hoặc đổi sang IP máy chủ / Production
  headers: {
    'Content-Type': 'application/json',
  },
});

// Interceptor: Tự động đính kèm Token nếu đã đăng nhập
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

export default api;
```

### 5.2. Ví dụ: Luồng gửi SOS kèm ảnh từ Giao diện Người dân
```typescript
async function submitSosReport(file: File, description: string, lat: number, lng: number, address: string) {
  try {
    let uploadedMediaUrl = '';

    // Bước 1: Upload ảnh nếu có
    if (file) {
      const formData = new FormData();
      formData.append('file', file);

      const uploadRes = await api.post('/media/upload', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      uploadedMediaUrl = uploadRes.data.data.url;
    }

    // Bước 2: Gửi báo cáo SOS tới Backend
    const incidentRes = await api.post('/incidents', {
      title: 'Báo cáo sự cố khẩn cấp',
      description: description,
      latitude: lat,
      longitude: lng,
      locationAddress: address,
      mediaUrls: uploadedMediaUrl ? [uploadedMediaUrl] : [],
    });

    console.log('Sự cố đã tạo:', incidentRes.data.data);
    console.log('Mức độ AI chấm:', incidentRes.data.data.severity);
    return incidentRes.data.data;
  } catch (error) {
    console.error('Lỗi khi gửi SOS:', error);
    throw error;
  }
}
```
