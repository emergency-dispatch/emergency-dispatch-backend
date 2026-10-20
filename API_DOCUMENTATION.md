# 📘 TÀI LIỆU TÍCH HỢP BACKEND API — DÀNH CHO ĐỘI NGŨ FRONTEND & MOBILE

> **Dự án**: Hệ thống Điều phối và Quản lý Đội cứu hộ Khẩn cấp theo Thời gian thực (Real-time Emergency Response & Dispatch Management System)  
> **Phiên bản API**: v1.1 (Clean Architecture - .NET 9.0)  
> **Cơ sở dữ liệu**: Neon PostgreSQL Cloud  
> **Mô hình AI**: Qwen2.5-VL-3B LoRA (Local GPU) → fallback Qwen3-VL 32B (`qwen/qwen3-vl-32b-instruct`, OpenRouter)

> ⚠️ **Quy ước link (cập nhật 21/10/2026)**: Toàn bộ API dùng **một tiền tố duy nhất `/api/...`**. Các link cũ `/api/v1/...` và `/api/RescueUnits/...` **đã bị xóa** — gọi vào sẽ trả về `404`. Bảng tra cứu nhanh toàn bộ endpoint ở [Mục 6](#6-bảng-tra-cứu-nhanh-toàn-bộ-endpoint).

---

## 1. MÔI TRƯỜNG & ĐỊA CHỈ TRUY CẬP (ENVIRONMENT)

* **Base URL (HTTP)**: `http://localhost:5000`
* **Base URL (HTTPS)**: `https://localhost:7000`
* **Swagger UI (Giao diện test trực quan)**: `http://localhost:5000/swagger`
* **Android Emulator**: dùng `http://10.0.2.2:5000` thay cho `localhost`.
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
* Enum được trả về và nhận vào dưới dạng **chuỗi** (ví dụ `"Level2"`, `"Available"`), backend cũng chấp nhận số nguyên.

### Cơ chế Xác thực (Authentication)
* Sử dụng chuẩn **JWT Bearer Token**.
* Đối với các API yêu cầu đăng nhập, FE gửi header:
  ```http
  Authorization: Bearer <access_token>
  ```
* Với SignalR, truyền token qua query string: `?access_token=<JWT_TOKEN>`.

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
* **Header**: `Authorization: Bearer <token>`
* **Mô tả**: Vô hiệu hóa Refresh Token trên database Neon.
* **Request Body**:
  ```json
  {
    "refreshToken": "by..."
  }
  ```

#### 1.10. Đăng nhập Google (`POST /api/auth/google`)
* **Mô tả**: Gửi Google ID Token lấy từ Google Sign-In, backend xác thực và trả về cặp JWT như đăng nhập thường.
* **Request Body**:
  ```json
  {
    "idToken": "eyJhbGciOiJSUzI1NiIs..."
  }
  ```

#### 1.11. Lấy thông tin cá nhân & Hồ sơ Y tế Khẩn cấp (`GET /api/users/me`)
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

#### 1.12. Cập nhật Hồ sơ Y tế Cứu hộ Khẩn cấp (`PUT /api/users/me`)
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

#### 1.13. Cập nhật FCM Token (`PUT /api/users/fcm-token`)
* **Header**: `Authorization: Bearer <token>`
* **Mô tả**: Đăng ký FCM Device Token để nhận Push Notification âm thanh còi hú khẩn cấp cho Mobile App.
* **Request Body**:
  ```json
  {
    "fcmToken": "f7dK9L2xP0w:APA91bH..."
  }
  ```

#### 1.14. Danh bạ người thân khẩn cấp ICE (`/api/users/ice-contacts`)
* **Header**: `Authorization: Bearer <token>`

| Method | Endpoint | Mô tả |
|---|---|---|
| GET | `/api/users/ice-contacts` | Danh sách người thân của tài khoản đang đăng nhập |
| POST | `/api/users/ice-contacts` | Thêm người thân |
| PUT | `/api/users/ice-contacts/{contactId}` | Cập nhật người thân |
| DELETE | `/api/users/ice-contacts/{contactId}` | Xóa người thân |

* **Request Body (POST / PUT)**:
  ```json
  {
    "name": "Lê Thị Thân",
    "phoneNumber": "0911223344",
    "relationship": "Vợ",
    "isPrimary": true
  }
  ```

---

### PHÂN HỆ 2: BÁO CÁO SỰ CỐ KHẨN CẤP & ĐIỀU PHỐI (INCIDENTS & CAD QUEUE)

#### 2.1. Gửi báo cáo sự cố SOS (`POST /api/incidents` hoặc `POST /api/incidents/sos`)
* **Mô tả**: Tạo sự cố mới. Hỗ trợ **người dân chưa đăng nhập (khách vãng lai)** hoặc **đã đăng nhập** (gửi kèm token để tự điền tên/SĐT).
  * API trả về **ngay lập tức** với trạng thái `AiProcessing` (nếu có ảnh) hoặc `Pending` (không có ảnh). AI chạy nền và kết quả được đẩy realtime qua SignalR (`ReceiveIncidentUpdate`).
  * AI gán nhãn nguy cơ (`hazardTags`) và xếp mức độ nguy cấp (`severity` từ `Level1` đến `Level3`).
  * Nếu AI lỗi mạng/timeout, hệ thống tự động Fallback về mức độ `Unclassified` (Mức 0) và đẩy lên đầu hàng đợi của Operator để xem xét thủ công.
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
    "message": "Đã tiếp nhận SOS. AI đang phân tích mức độ nguy hiểm, kết quả sẽ được cập nhật realtime.",
    "data": {
      "id": "f8a7e584-3c6d-4b82-9f12-9c3f0b2a7d4e",
      "title": "Hỏa hoạn khu dân cư",
      "description": "Thấy khói đen bốc lên nghi ngút...",
      "latitude": 10.776889,
      "longitude": 106.700806,
      "locationAddress": "123 Lê Lợi, P. Bến Thành, Quận 1, TP.HCM",
      "status": "AiProcessing",
      "severity": "Unclassified",
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
      "aiClassification": null
    }
  }
  ```
* **Sau khi AI xong** (`GET /api/incidents/{id}` hoặc sự kiện SignalR), trường `aiClassification` có dạng:
  ```json
  {
    "hazardTags": ["fire", "heavy_smoke", "residential_area"],
    "severityScore": 3,
    "summary": "Phát hiện đám cháy bùng phát tại khu vực nhà ở kèm khói đặc, nguy cơ cháy lan cao.",
    "confidenceScore": 0.92,
    "modelName": "Qwen2.5-VL-3B-LoRA (Local GPU)",
    "isSuccess": true,
    "processingDurationMs": 1420
  }
  ```

#### 2.2. Hàng đợi sự cố chờ duyệt dành cho Điều phối viên (`GET /api/incidents/queue`)
* **Mô tả**: Lấy danh sách sự cố chưa duyệt (`Pending`, `AiProcessing` hoặc `AiProcessed`).
* **Thuật toán ưu tiên hiển thị (Priority Ordering)**:
  1. Các sự cố **`Unclassified` (Mức 0 - do AI không phân loại được hoặc gặp lỗi)** luôn được đẩy **lên đầu trang** để Operator thẩm tra thủ công khẩn cấp.
  2. Tiếp theo là các sự cố nguy hiểm giảm dần: **Mức 3 $\rightarrow$ Mức 2 $\rightarrow$ Mức 1**.
  3. Cùng mức độ thì ưu tiên sự cố gửi trước (FIFO).
* **Response (200 OK)**: Danh sách mảng các sự cố kèm đầy đủ ảnh và phân tích AI.

#### 2.3. Lấy chi tiết sự cố (`GET /api/incidents/{id}`)
* **Mô tả**: Xem thông tin chi tiết một sự cố theo GUID.

#### 2.4. Danh sách sự cố có phân trang & bộ lọc (`GET /api/incidents`)
* **Query Parameters**:
  * `pageIndex`: Số trang (mặc định: 1)
  * `pageSize`: Số phần tử/trang (mặc định: 10, tối đa: 50)
  * `status`: Lọc theo trạng thái (`Pending`, `AiProcessed`, `Verified`, `Dispatched`, ...)
  * `severity`: Lọc theo mức độ (`Unclassified`, `Level1`, `Level2`, `Level3`)
  * `searchTerm`: Tìm kiếm theo từ khóa trong tiêu đề, mô tả, địa chỉ
  * `fromDate`, `toDate`: Lọc theo khoảng thời gian tạo
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
* **Request Body** (`confirmedSeverity`: `Level1` / `Level2` / `Level3`):
  ```json
  {
    "confirmedSeverity": "Level3",
    "adjustedTitle": "Cháy lớn tại số 123 Lê Lợi, Quận 1 (Đã xác minh)",
    "operatorNotes": "Đã liên hệ người dân qua SĐT 0909123456 xác nhận có 2 người bị kẹt tầng 2."
  }
  ```
* **Response (200 OK)**: Sự cố chuyển sang trạng thái `Verified`.

#### 2.6. Hủy sự cố báo sai / báo khống (`PUT /api/incidents/{id}/cancel`)
* **Quyền hạn**: Công khai (người báo tự hủy) hoặc Operator.
* **Request Body**:
  ```json
  {
    "reason": "Báo khống, đã gọi lại xác minh không có hiện trường"
  }
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

#### 4.1. Phân tích ảnh bằng AI (`POST /api/ai/analyze`)
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
      "severityScore": 3,
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

#### 5.1. Gợi ý đội cứu hộ tối ưu (`GET /api/dispatch/recommend`)
* **Endpoint**: `GET /api/dispatch/recommend?incidentId={id}&algorithm=NearestAvailable&maxDistanceKm=15`
* **Quyền hạn**: `Operator`, `Admin`
* **Query Parameters**:
  * `incidentId` (bắt buộc): ID sự cố cần điều phối.
  * `algorithm` (tùy chọn): `NearestAvailable` (mặc định — gần nhất theo Haversine) hoặc `LoadAware` (cân bằng tải theo số nhiệm vụ đang xử lý).
  * `unitType` (tùy chọn): Lọc loại xe (`Ambulance`, `FireTruck`, `LadderTruck`, `RescueBoat`, `HeavyRescueVehicle`, `PolicePatrol`). Bỏ trống thì hệ thống tự suy luận theo loại sự cố.
  * `maxDistanceKm` (tùy chọn, mặc định 15): Bán kính tìm kiếm.
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
      "stationId": "a1b2c3d4-...",
      "stationName": "Trạm Cứu hộ Trung tâm Quận 1",
      "currentLat": 10.7769,
      "currentLng": 106.7009,
      "distanceKm": 1.45,
      "estimatedEtaMinutes": 4.5,
      "activeTasksCount": 0,
      "score": 40.82,
      "algorithmUsed": "NearestAvailable",
      "recommendationReason": "Khoảng cách: 1.45 km (4 phút). Trạng thái: Available."
    }
  ]
}
```

#### 5.2. Danh sách xe khả dụng quanh sự cố (`GET /api/dispatch/available-units`)
* **Endpoint**: `GET /api/dispatch/available-units?incidentId={id}&maxDistanceKm=15`
* **Quyền hạn**: `Operator`, `Admin`
* **Mô tả**: Trả về các xe khả dụng trong bán kính, sắp xếp theo khoảng cách (cùng định dạng với 5.1).

#### 5.3. Điều phối xe cứu hộ tới sự cố (`POST /api/dispatch/assign`)
* **Endpoint**: `POST /api/dispatch/assign`
* **Quyền hạn**: `Operator`, `Admin`
* **Mô tả**: Gán **một** xe cứu hộ cho sự cố (gọi nhiều lần nếu cần nhiều xe). Hệ thống chuyển sự cố sang `Dispatched`, xe sang `Dispatched`, ghi Audit Trail và phát sự kiện SignalR `ReceiveJobAssigned` tới Operator + đội cứu hộ của trạm.
* **Body Request**:
```json
{
  "incidentId": "d3b07384-d113-40e1-bbbb-b9961a9ee001",
  "rescueUnitId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "notes": "Ưu tiên cấp cứu nạn nhân bỏng tầng 2, tiếp cận từ cổng Đông."
}
```
* **Response**: `200 OK` — trả về chi tiết lượt phân công (`DispatchAssignment`) với `status: "Dispatched"`.

#### 5.4. Chi tiết phân công (`GET /api/dispatch/assignments/{id}`)
* **Quyền hạn**: Đã đăng nhập.

#### 5.5. Danh sách phân công của một sự cố (`GET /api/dispatch/assignments/incident/{incidentId}`)
* **Quyền hạn**: Công khai.

#### 5.6. Nhiệm vụ của Đội cứu hộ (`GET /api/dispatch/my-assignments`)
* **Quyền hạn**: `RescueStaff`, `Operator`, `Admin`
* **Mô tả**: Dành cho App Mobile / Web Staff. Trả về toàn bộ nhiệm vụ của các xe thuộc **trạm** của nhân viên đang đăng nhập (mới nhất trước). Tài khoản chưa gán trạm sẽ nhận danh sách rỗng.

#### 5.7. Cập nhật trạng thái nhiệm vụ (`PUT /api/dispatch/assignments/{id}/status`)
* **Quyền hạn**: `RescueStaff`, `Operator`, `Admin`
* **Mô tả**: Đội cứu hộ cập nhật tiến trình theo vòng đời:
  * `Accepted`: Đã nhận lệnh.
  * `EnRoute`: Đang trên đường tới hiện trường.
  * `OnScene`: Đã có mặt tại hiện trường.
  * `Cancelled`: Lệnh điều phối bị hủy.
  * (Hoàn tất nhiệm vụ dùng API 5.8.)
* **Body Request**:
```json
{
  "status": "OnScene",
  "notes": "Đã tiếp cận hiện trường, đang triển khai đội hình sơ cứu."
}
```
* **Response**: `200 OK` — phát SignalR `ReceiveAssignmentStatus`.

#### 5.8. Nghiệm thu & đóng nhiệm vụ (`POST /api/dispatch/assignments/{id}/resolve`)
* **Quyền hạn**: `RescueStaff`, `Operator`, `Admin`
* **Mô tả**: Đánh dấu phân công `Completed`, trả xe về `Available`, **đóng hồ sơ sự cố** (`Completed`) và ghi nhận thời gian xử lý (Telemetry RQ3).
* **Body Request** (`resolutionSummary` tối thiểu 10 ký tự):
```json
{
  "resolutionSummary": "Đã dập tắt đám cháy hoàn toàn lúc 14:45. 2 nạn nhân đã được đưa vào Bệnh viện Quận 1 an toàn.",
  "evidenceMediaUrls": ["https://res.cloudinary.com/.../after.jpg"],
  "handoverNotes": "Bàn giao hiện trường cho công an phường."
}
```
* **Response**: `200 OK`

#### 5.9. Theo dõi đường di chuyển xe cứu hộ cho Người dân (`GET /api/dispatch/tracking/incident/{incidentId}`)
* **Quyền hạn**: Công khai (người báo vãng lai cũng xem được bằng `incidentId`).
* **Mô tả**: Trả về vị trí xe hiện tại, **lộ trình di chuyển** (`routeTrajectory` — danh sách điểm tọa độ để vẽ polyline trên bản đồ), khoảng cách, ETA và các mốc thời gian. Nếu chưa có xe được phân công, `dispatchStatus = "PendingDispatch"`.
* **Gợi ý tích hợp**: Gọi API này để vẽ lộ trình ban đầu, sau đó kết nối `/hubs/tracking`, gọi `SubscribeIncident(incidentId)` và lắng nghe `ReceiveRescueUnitLocation` để di chuyển marker xe realtime.
* **Response**: `200 OK`
```json
{
  "success": true,
  "message": "Lấy thông tin lộ trình di chuyển cứu hộ thành công.",
  "data": {
    "incidentId": "d3b07384-...",
    "incidentTitle": "Hỏa hoạn khu dân cư",
    "incidentAddress": "123 Lê Lợi, Quận 1",
    "incidentLat": 10.776889,
    "incidentLng": 106.700806,
    "incidentStatus": "Dispatched",
    "incidentSeverity": "Level3",
    "rescueUnitId": "3fa85f64-...",
    "plateNumber": "51A-115.01",
    "unitTypeName": "FireTruck",
    "stationName": "Trạm Cứu hộ Trung tâm Quận 1",
    "stationPhoneNumber": "02838123456",
    "vehicleLat": 10.7812,
    "vehicleLng": 106.6951,
    "lastLocationUpdateAt": "2026-10-18T14:32:10Z",
    "routeTrajectory": [
      { "latitude": 10.7812, "longitude": 106.6951 },
      { "latitude": 10.7798, "longitude": 106.6972 },
      { "latitude": 10.776889, "longitude": 106.700806 }
    ],
    "distanceKm": 0.85,
    "estimatedArrivalMinutes": 3,
    "dispatchStatus": "EnRoute",
    "dispatchedAt": "2026-10-18T14:30:00Z",
    "acceptedAt": "2026-10-18T14:30:40Z",
    "arrivedAt": null,
    "completedAt": null,
    "notes": null
  }
}
```

---

### PHÂN HỆ 6: CẢNH BÁO ĐIỂM NÓNG GIAO THÔNG (TRAFFIC HOTSPOTS)

#### 6.1. Báo cáo điểm nghẽn giao thông (`POST /api/traffic/report`)
* **Quyền hạn**: Công khai (gửi kèm token nếu đã đăng nhập).
* **Mô tả**: Cảnh báo điểm kẹt xe, tai nạn hoặc ngập lụt cản trở xe cứu hộ. Điểm nóng tự động hết hạn sau `ttlMinutes` (10–180 phút, mặc định 45) và được worker `HotspotCleanupWorker` dọn dẹp ngầm. Phát SignalR `ReceiveTrafficHotspot`.
* **Body Request** (`congestionLevel`: `Low`, `Moderate`, `Severe`, `Blocked`):
```json
{
  "title": "Ùn tắc nghiêm trọng tại ngã tư D1 - D2",
  "description": "Va chạm giữa 2 xe tải gây chắn toàn bộ làn đường hướng vào Lô E",
  "latitude": 10.8415,
  "longitude": 106.8105,
  "locationAddress": "Ngã tư D1 - D2, Khu Công nghệ cao",
  "congestionLevel": "Severe",
  "mediaUrl": null,
  "reporterName": "Nguyễn Văn B",
  "reporterPhone": "0909000111",
  "ttlMinutes": 60
}
```
* **Response**: `200 OK` (Trả về chi tiết Hotspot và thời gian hết hạn `expiresAt`).

#### 6.2. Danh sách điểm nghẽn đang hoạt động (`GET /api/traffic/active`)
* **Quyền hạn**: Công khai (Web CAD và App Mobile).
* **Mô tả**: Trả về toàn bộ các điểm nóng còn hiệu lực để hiển thị lớp bản đồ cảnh báo.

#### 6.3. Giải tỏa điểm nghẽn (`PUT /api/traffic/{id}/resolve`)
* **Quyền hạn**: `Operator`, `Admin`, `RescueStaff`

---

### PHÂN HỆ 7: CẤU HÌNH LEO THANG SỰ CỐ TỰ ĐỘNG (ESCALATION RULE ENGINE)

#### 7.1. Lấy danh sách quy tắc leo thang (`GET /api/escalation/rules`)
* **Quyền hạn**: `Admin`, `Operator`
* Xem chi tiết một quy tắc: `GET /api/escalation/rules/{id}`.

#### 7.2. Tạo mới quy tắc leo thang (`POST /api/escalation/rules`)
* **Quyền hạn**: `Admin`
* **Mô tả**: Thiết lập quy tắc cho background worker `EscalationWorker`. Khi có sự cố mức độ nghiêm trọng `>= minSeverityLevel` không được tiếp nhận sau `timeoutMinutes` (1–60), hệ thống tự động đổi trạng thái sự cố sang `Escalated` và phát cảnh báo `ReceiveEscalationAlert` tới các vai trò trong `notifyRoles`.
* **Body Request**:
```json
{
  "description": "Leo thang khẩn cấp sự cố Cấp 3 chưa duyệt quá 5 phút",
  "minSeverityLevel": "Level3",
  "timeoutMinutes": 5,
  "notifyRoles": "[\"Admin\",\"Operator\"]",
  "expandRadiusPercent": 50,
  "isActive": true
}
```
* **Response**: `200 OK`

#### 7.3. Cập nhật quy tắc (`PUT /api/escalation/rules/{id}`)
* **Quyền hạn**: `Admin`
* **Body Request**: Tương tự `POST`, có thể đặt `isActive: false` để vô hiệu hóa quy tắc tức thời.

#### 7.4. Xóa mềm quy tắc (`DELETE /api/escalation/rules/{id}`)
* **Quyền hạn**: `Admin`

#### 7.5. Lịch sử kích hoạt leo thang (`GET /api/escalation/logs`)
* **Endpoint**: `GET /api/escalation/logs?incidentId={id}&page=1&pageSize=50`
* **Quyền hạn**: `Admin`, `Operator`
* `incidentId` tùy chọn — bỏ trống để xem toàn bộ.

#### 7.6. Chạy kiểm tra leo thang thủ công (`POST /api/escalation/trigger-check`)
* **Quyền hạn**: `Admin`
* **Mô tả**: Kích hoạt ngay một lượt quét (phục vụ demo/kiểm thử, không cần chờ chu kỳ worker).

---

### PHÂN HỆ 8: ĐA NGÔN NGỮ (BILINGUAL LOCALIZATION `vi` / `en`)

#### 8.1. Kiểm tra ngôn ngữ hiện tại của Client (`GET /api/localization/info`)
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

---

### PHÂN HỆ 9: KẾT NỐI REAL-TIME WEBSOCKET (SIGNALR HUBS)

Backend cung cấp 2 SignalR Hubs. Khi kết nối có token, server **tự động thêm kết nối vào nhóm theo vai trò**:
* `Operator`, `Admin` → nhóm `operators` (Admin thêm nhóm `admins`).
* `RescueStaff` → nhóm `rescuers` và nhóm trạm `station:{stationId}`.
* Người báo (kể cả khách không token) gọi `SubscribeIncident(incidentId)` để vào nhóm `incident:{incidentId}`.

#### 9.1. Incident Hub — Sự cố & điều phối (`/hubs/incidents`)
* **URL**: `http://localhost:5000/hubs/incidents?access_token=<JWT_TOKEN>`
* **Client gọi lên Server (Invoke)**:
  * `SubscribeIncident(incidentId)` / `UnsubscribeIncident(incidentId)`
* **Client lắng nghe từ Server (On)**:

| Sự kiện | Người nhận | Payload |
|---|---|---|
| `ReceiveNewIncident` | `operators` | Sự cố SOS mới (`IncidentResponseDto`) |
| `ReceiveIncidentUpdate` | `operators` | Sự cố cập nhật đầy đủ (sau khi AI xong, xác minh, điều phối, đóng) |
| `ReceiveIncidentStatus` | `incident:{id}` | Trạng thái rút gọn cho người báo (không kèm dữ liệu y tế) |
| `ReceiveJobAssigned` | `rescuers`, `station:{id}`, `operators` | Lệnh phân công mới (`DispatchAssignmentResponseDto`) |
| `ReceiveAssignmentStatus` | `operators`, `incident:{id}` | Đội cứu hộ cập nhật trạng thái nhiệm vụ |
| `ReceiveTrafficHotspot` | `operators`, `rescuers` | Điểm nghẽn mới / được giải tỏa |
| `ReceiveEscalationAlert` | `operators`, `admins`, `incident:{id}` | Cảnh báo leo thang sự cố quá hạn |

#### 9.2. Tracking Hub — Định vị GPS thời gian thực (`/hubs/tracking`)
* **URL**: `http://localhost:5000/hubs/tracking?access_token=<JWT_TOKEN>`
* **Client gọi lên Server (Invoke)**:
  * `SubscribeIncident(incidentId)` / `UnsubscribeIncident(incidentId)` — người báo theo dõi xe.
  * `UpdateLocation(message)` — **chỉ `RescueStaff` / `Admin`**, gửi định kỳ 3–5 giây:
    ```json
    {
      "rescueUnitId": "3fa85f64-...",
      "incidentId": "d3b07384-...",
      "latitude": 10.7798,
      "longitude": 106.6972,
      "headingDegrees": 90,
      "speedKmh": 42
    }
    ```
* **Client lắng nghe từ Server (On)**:
  * `ReceiveRescueUnitLocation` (gửi tới `operators` và `incident:{incidentId}` nếu có) → payload `{ rescueUnitId, incidentId, latitude, longitude, headingDegrees, speedKmh, timestampUtc }`.

---

### PHÂN HỆ 10: QUẢN TRỊ VIÊN & THỐNG KÊ DASHBOARD (ADMIN STATS & BACK-OFFICE)

#### 10.1. Lấy toàn bộ số liệu thống kê & KPIs vận hành (`GET /api/dashboard/stats`)
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

### PHÂN HỆ 11: QUẢN LÝ TRẠM CỨU HỘ (STATIONS)

| Method | Endpoint | Quyền hạn | Mô tả |
|---|---|---|---|
| GET | `/api/stations?activeOnly=false` | Công khai | Danh sách trạm |
| GET | `/api/stations/{id}` | Công khai | Chi tiết trạm |
| POST | `/api/stations` | `Admin` | Tạo trạm |
| PUT | `/api/stations/{id}` | `Admin` | Cập nhật trạm |
| DELETE | `/api/stations/{id}` | `Admin` | Xóa mềm trạm |
| GET | `/api/stations/{id}/staff` | Công khai | Nhân viên thuộc trạm |

* **Request Body (POST / PUT)**:
```json
{
  "name": "Trạm Cứu hộ Trung tâm Quận 1",
  "address": "12 Nguyễn Huệ, Quận 1, TP.HCM",
  "latitude": 10.7769,
  "longitude": 106.7009,
  "phoneNumber": "02838123456",
  "isActive": true
}
```

---

### PHÂN HỆ 12: QUẢN LÝ PHƯƠNG TIỆN CỨU HỘ (RESCUE UNITS)

> Link duy nhất: **`/api/rescue-units`** (link cũ `/api/RescueUnits` đã bị xóa).

| Method | Endpoint | Quyền hạn | Mô tả |
|---|---|---|---|
| GET | `/api/rescue-units?unitType=FireTruck&availableOnly=false` | Công khai | Danh sách xe (lọc theo loại / chỉ xe rảnh) |
| GET | `/api/rescue-units/{id}` | Công khai | Chi tiết xe |
| GET | `/api/rescue-units/station/{stationId}` | Công khai | Xe theo trạm |
| POST | `/api/rescue-units` | `Admin`, `Operator` | Thêm xe |
| PUT | `/api/rescue-units/{id}` | `Admin`, `Operator` | Cập nhật xe |
| PUT | `/api/rescue-units/{id}/location` | `Admin`, `Operator`, `RescueStaff` | Cập nhật GPS (REST, thay cho SignalR nếu cần) |
| PUT | `/api/rescue-units/{id}/status` | `Admin`, `Operator`, `RescueStaff` | Cập nhật trạng thái xe |
| DELETE | `/api/rescue-units/{id}` | `Admin` | Xóa mềm xe |

* **Body tạo xe (POST)**:
```json
{
  "plateNumber": "51A-115.89",
  "unitType": "Ambulance",
  "stationId": "a1b2c3d4-...",
  "initialLat": 10.7769,
  "initialLng": 106.7009
}
```
* **Body cập nhật GPS**: `{ "latitude": 10.7798, "longitude": 106.6972 }`
* **Body cập nhật trạng thái** (`Available`, `Dispatched`, `OnScene`, `Returning`, `Maintenance`): `{ "status": "Maintenance" }`

---

### PHÂN HỆ 13: QUẢN LÝ TÀI KHOẢN, TRẠM & TỔ LÀM VIỆC (USER ADMINISTRATION)

| Method | Endpoint | Quyền hạn | Mô tả |
|---|---|---|---|
| GET | `/api/users?pageIndex=1&pageSize=10&search=&role=&status=` | `Admin`, `Operator` | Danh sách tài khoản (phân trang, lọc) |
| GET | `/api/users/{id}` | `Admin`, `Operator` | Chi tiết tài khoản |
| POST | `/api/users` | `Admin`, `Operator` | Tạo tài khoản kèm vai trò |
| PUT | `/api/users/{id}` | `Admin`, `Operator` | Cập nhật thông tin / vai trò / trạng thái |
| DELETE | `/api/users/{id}` | `Admin` | Xóa mềm tài khoản |
| PUT | `/api/users/{id}/assign-station-team` | `Admin`, `Operator` | Gán nhân viên vào trạm và Tổ làm việc |
| GET | `/api/users/station/{stationId}` | `Admin`, `Operator`, `RescueStaff` | Nhân viên của trạm kèm Tổ |
| GET | `/api/users/my-team` | `RescueStaff`, `Operator`, `Admin` | Tổ làm việc, đồng đội và xe của trạm mình |

* **Body tạo tài khoản (POST)** (`role`: `Citizen`, `Operator`, `RescueStaff`, `Admin`):
```json
{
  "fullName": "Phạm Văn Cứu",
  "email": "staff02@emergencydispatch.com",
  "password": "Staff@123456",
  "phoneNumber": "0907000222",
  "role": "RescueStaff",
  "stationId": "a1b2c3d4-..."
}
```
* **Body gán trạm & tổ**:
```json
{
  "stationId": "a1b2c3d4-...",
  "teamName": "Tổ phản ứng nhanh số 1"
}
```

---

## 4. BẢNG TRA CỨU ENUMS (DÀNH CHO TYPESCRIPT / INTERFACES)

FE copy các enum này vào mã nguồn Frontend / Mobile để khớp 100% kiểu dữ liệu:

```typescript
// Mức độ nghiêm trọng của sự cố (chuẩn 3 cấp + Reject)
export enum SeverityLevel {
  Unclassified = "Unclassified", // 0: Bác bỏ / AI lỗi / chưa phân loại (Cần Operator xem xét khẩn)
  Level1 = "Level1",             // 1: Nhẹ (cháy rác, va chạm nhẹ)
  Level2 = "Level2",             // 2: Trung bình (cháy 1 xe/1 phòng, tai nạn có người bị thương)
  Level3 = "Level3",             // 3: Khẩn cấp cao nhất (cháy lớn, nhiều nạn nhân) — tự động leo thang
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

// Loại xe cứu hộ
export enum RescueUnitType {
  Ambulance = "Ambulance",
  FireTruck = "FireTruck",
  LadderTruck = "LadderTruck",
  RescueBoat = "RescueBoat",
  HeavyRescueVehicle = "HeavyRescueVehicle",
  PolicePatrol = "PolicePatrol",
}

// Trạng thái xe cứu hộ
export enum RescueUnitStatus {
  Available = "Available",
  Dispatched = "Dispatched",
  OnScene = "OnScene",
  Returning = "Returning",
  Maintenance = "Maintenance",
}

// Trạng thái lượt phân công
export enum DispatchAssignmentStatus {
  Dispatched = "Dispatched",
  Accepted = "Accepted",
  EnRoute = "EnRoute",
  OnScene = "OnScene",
  Completed = "Completed",
  Cancelled = "Cancelled",
}

// Thuật toán gợi ý điều phối
export enum DispatchAlgorithm {
  NearestAvailable = "NearestAvailable",
  LoadAware = "LoadAware",
}

// Mức độ ùn tắc
export enum TrafficCongestionLevel {
  Low = "Low",
  Moderate = "Moderate",
  Severe = "Severe",
  Blocked = "Blocked",
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

    // Kết quả AI sẽ đến sau qua SignalR / gọi lại GET /incidents/{id}
    console.log('Sự cố đã tạo:', incidentRes.data.data);
    return incidentRes.data.data;
  } catch (error) {
    console.error('Lỗi khi gửi SOS:', error);
    throw error;
  }
}
```

---

## 6. BẢNG TRA CỨU NHANH TOÀN BỘ ENDPOINT

| Phân hệ | Endpoint |
|---|---|
| Auth | `POST /api/auth/register` · `verify-email` · `resend-verification` · `forgot-password` · `reset-password` · `login` · `google` · `refresh` · `logout` · `change-password` |
| Users (cá nhân) | `GET/PUT /api/users/me` · `PUT /api/users/fcm-token` · `GET/POST /api/users/ice-contacts` · `PUT/DELETE /api/users/ice-contacts/{contactId}` |
| Users (quản trị) | `GET/POST /api/users` · `GET/PUT/DELETE /api/users/{id}` · `PUT /api/users/{id}/assign-station-team` · `GET /api/users/station/{stationId}` · `GET /api/users/my-team` |
| Incidents | `POST /api/incidents` (= `/api/incidents/sos`) · `GET /api/incidents` · `GET /api/incidents/queue` · `GET /api/incidents/{id}` · `PUT /api/incidents/{id}/verify` · `PUT /api/incidents/{id}/cancel` |
| Media | `POST /api/media/upload` |
| AI | `POST /api/ai/analyze` |
| Dispatch | `GET /api/dispatch/recommend` · `GET /api/dispatch/available-units` · `POST /api/dispatch/assign` · `GET /api/dispatch/assignments/{id}` · `GET /api/dispatch/assignments/incident/{incidentId}` · `GET /api/dispatch/tracking/incident/{incidentId}` · `GET /api/dispatch/my-assignments` · `PUT /api/dispatch/assignments/{id}/status` · `POST /api/dispatch/assignments/{id}/resolve` |
| Traffic | `POST /api/traffic/report` · `GET /api/traffic/active` · `PUT /api/traffic/{id}/resolve` |
| Escalation | `GET/POST /api/escalation/rules` · `GET/PUT/DELETE /api/escalation/rules/{id}` · `GET /api/escalation/logs` · `POST /api/escalation/trigger-check` |
| Stations | `GET/POST /api/stations` · `GET/PUT/DELETE /api/stations/{id}` · `GET /api/stations/{id}/staff` |
| Rescue Units | `GET/POST /api/rescue-units` · `GET/PUT/DELETE /api/rescue-units/{id}` · `GET /api/rescue-units/station/{stationId}` · `PUT /api/rescue-units/{id}/location` · `PUT /api/rescue-units/{id}/status` |
| Dashboard | `GET /api/dashboard/stats` |
| Localization | `GET /api/localization/info` |
| SignalR | `/hubs/incidents` · `/hubs/tracking` |
