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
* **CORS**: Chỉ origin trong `Cors:AllowedOrigins` (appsettings) được gọi API. Chưa cấu hình + môi trường Development → cho phép `localhost` / `127.0.0.1` (mọi cổng) và IP mạng LAN (`192.168.x.x`, `10.x.x.x`, `172.16–31.x.x`). App mobile native không gửi `Origin` nên không bị ảnh hưởng.
* **Access token** sống **60 phút** (`JwtSettings:ExpireMinutes`). Hết hạn → API trả 401, client gọi `POST /api/auth/refresh` (refresh token sống 7 ngày). Lưu ý: `expiresAt` trong response đăng nhập là hạn của **refresh token**, không phải access token.

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
      "id": "70d2a8b9-2095-4fa5-9e16-6b51ec92703a",
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
      "stationName": null,
      "chronicConditions": [
        "Hen suyễn"
      ],
      "allergies": [
        "Penicillin"
      ],
      "currentMedications": [
        "Thuốc theo chỉ định"
      ],
      "mobilityLimitations": [
        "Sử dụng xe lăn"
      ],
      "preferredLanguage": "Tiếng Việt",
      "dependentsNote": "Có trẻ nhỏ và người cao tuổi sống cùng",
      "householdMembersCount": 4
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
    "emergencyContactRelationship": "Vợ",
    "chronicConditions": [
      "Hen suyễn"
    ],
    "allergies": [
      "Penicillin"
    ],
    "currentMedications": [
      "Thuốc theo chỉ định"
    ],
    "mobilityLimitations": [
      "Sử dụng xe lăn"
    ],
    "preferredLanguage": "Tiếng Việt",
    "dependentsNote": "Có trẻ nhỏ và người cao tuổi sống cùng",
    "householdMembersCount": 4
  }
  ```

* **Trường mở rộng** (GET trả về, PUT cập nhật):

  | Trường | Kiểu dữ liệu | Ý nghĩa / giới hạn |
  |---|---|---|
  | `chronicConditions` | `string[]` | Bệnh nền |
  | `allergies` | `string[]` | Dị ứng |
  | `currentMedications` | `string[]` | Thuốc đang sử dụng |
  | `mobilityLimitations` | `string[]` | Hạn chế thể chất / khuyết tật |
  | `preferredLanguage` | `string / null` | Ngôn ngữ ưu tiên, tối đa 50 ký tự |
  | `dependentsNote` | `string / null` | Người phụ thuộc, tối đa 1.000 ký tự |
  | `householdMembersCount` | `integer / null` | Số thành viên gia đình, từ 0 đến 100 |

* **Quy tắc cập nhật**:
  * `fullName` bắt buộc, tối đa 100 ký tự.
  * Bốn mảng Medical ID: bỏ trường hoặc gửi `null` thì giữ nguyên; gửi `[]` thì xóa danh sách.
  * `preferredLanguage`, `dependentsNote`, `householdMembersCount`: bỏ trường thì giữ nguyên; gửi `null` thì xóa.
  * Các quy tắc giữ nguyên trên chỉ áp dụng cho những trường được liệt kê. Với trường hồ sơ cũ như số điện thoại, ngày sinh, địa chỉ, nhóm máu, ghi chú y tế và ba trường `emergencyContact...`, bỏ trường trong PUT có thể xóa dữ liệu đang lưu; client cần gửi lại giá trị muốn giữ.

#### 1.13. Cập nhật FCM Token (`PUT /api/users/fcm-token`)
* **Header**: `Authorization: Bearer <token>`
* **Mô tả**: Đăng ký FCM Device Token để nhận Push Notification âm thanh còi hú khẩn cấp cho Mobile App.
* **Request Body**:
  ```json
  {
    "fcmToken": "f7dK9L2xP0w:APA91bH..."
  }
  ```

#### 1.14. Một người liên hệ khẩn cấp
* Hồ sơ chỉ lưu một người liên hệ qua `emergencyContactName`, `emergencyContactPhone`, `emergencyContactRelationship`.
* Đọc/cập nhật bằng `GET/PUT /api/users/me`, dùng JWT của tài khoản. PUT vẫn cần `fullName` và các giá trị hồ sơ muốn giữ.
* Không có API quản lý danh sách nhiều người thân hoặc cấu hình gửi tin nhắn tự động.

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
      "aiClassification": null,
      "reportedByUserId": null,
      "guestCancellationToken": "<mã hủy Guest do BE sinh khi tạo sự cố>"
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

* **Mã hủy Guest SOS**:
  * Khi tạo SOS chưa đăng nhập, response có `data.guestCancellationToken` tương ứng `data.id`, hiệu lực **24 giờ**.
  * Mã là bí mật, không phải JWT. Theo quy tắc main hiện tại, endpoint hủy bắt buộc đăng nhập; mã này chưa cấp quyền tự hủy cho Guest.
  * Chỉ trả mã trong response tạo Guest SOS; không trả qua GET chi tiết, danh sách, hàng đợi hoặc sự kiện realtime. SOS tạo khi đã đăng nhập không có mã này.
  * Guest SOS cần Operator/Admin hỗ trợ hủy; không có API lấy lại mã.
  * Token dùng ASP.NET Core Data Protection; khi triển khai cần giữ key ring qua các lần khởi động và chia sẻ key ring giữa các API instance để xác minh token trong thời hạn.
* **Hồ sơ trong response**: SOS gắn tài khoản khi tìm được hồ sơ có `reporterMedicalProfile` gồm Medical ID, Special Info và ba trường `emergencyContactName`, `emergencyContactPhone`, `emergencyContactRelationship`. Guest không có hồ sơ tài khoản liên kết. Số liên hệ khẩn cấp được che theo cùng quy tắc bảo vệ số điện thoại của response.

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
* **Quyền hạn theo main hiện tại (BR-08)**: bắt buộc JWT, chỉ nhận vai trò Citizen, Operator hoặc Admin.
  * Citizen chỉ tự hủy SOS của mình trước khi có xe được điều động. Chặn tự hủy khi trạng thái là `Dispatched`/`InProgress` hoặc có phân công đang hoạt động.
  * Operator/Admin được hủy theo quyền điều phối, nhưng không được thao tác khi Operator khác đang giữ khóa mềm còn hiệu lực.
  * Guest chưa đăng nhập không được gọi endpoint này, kể cả có `guestCancellationToken`; cần Operator/Admin hỗ trợ.
* **Request Body**:
  ```json
  {
    "reason": "Đã xác minh báo nhầm sự cố"
  }
  ```
* `reason` không bắt buộc, tối đa 500 ký tự; bỏ trống dùng lý do mặc định theo ngôn ngữ request. Trường `cancellationToken` vẫn còn trong DTO để tương thích nhưng không cấp quyền hủy Guest trong luồng hiện tại.
* **200 OK**: `success:true`, `data.status: "Cancelled"`. Operator/Admin có thể thu hồi các phân công đang hoạt động theo logic xử lý và phát cập nhật realtime.
* **400 Bad Request**: không tìm thấy sự cố; đã `Completed`/`Cancelled`; người dân không sở hữu sự cố hoặc đội cứu hộ đã được điều động.
* **401 Unauthorized**: chưa đăng nhập; **403 Forbidden**: JWT không có vai trò được phép; **409 Conflict**: Operator/Admin bị chặn do khóa mềm của Operator khác, `errorCode: "CONFLICT"`.

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

#### 8.2. Song ngữ cho Medical ID, Special Info và hủy SOS
* Gửi `Accept-Language: vi` hoặc `Accept-Language: en`; cũng hỗ trợ `vi-VN`, `en-US`. Mặc định là tiếng Việt. Header được ưu tiên trước query `?culture=`.
* Dùng cùng `ILocalizationService` / `LocalizationHelper` và `LocalizationCatalog` của dự án. Không cần endpoint riêng cho từng ngôn ngữ.
* Thông báo thành công/thất bại của hồ sơ, tạo SOS và hủy SOS dùng ngôn ngữ request; thông báo động có ID hoặc số xe cũng được dịch theo mẫu.
* Lỗi DataAnnotations của các DTO hồ sơ/hủy SOS được dịch trong `ValidationProblemDetails.errors`; giữ nguyên HTTP 400, tên field và cấu trúc lỗi mặc định. Lỗi FluentValidation của tạo SOS nằm trong `ApiResponseDto.errors`.
* Tên field JSON, ID, enum như `Cancelled`, `Level3` giữ nguyên ở cả hai ngôn ngữ. Nội dung người dùng nhập như bệnh nền, dị ứng, ngôn ngữ ưu tiên, ghi chú và quan hệ không tự dịch.
* `preferredLanguage` là dữ liệu hồ sơ, không thay thế `Accept-Language` để chọn ngôn ngữ API.
* Lý do hủy mặc định và ghi chú hệ thống khi hủy được tạo theo ngôn ngữ request, lưu nguyên văn; đổi header khi xem lại không dịch lại nội dung đã lưu.
* Test Swagger: chạy lại cùng request với header `vi` và `en`; kiểm tra `message`/lỗi validation thay đổi ngôn ngữ, trong khi giá trị hồ sơ và tên field giữ nguyên. Với test hủy SOS, dùng Citizen sở hữu sự cố hoặc Operator/Admin và tạo hai sự cố riêng cho hai ngôn ngữ; Guest chưa đăng nhập phải nhận 401.

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
| Users (cá nhân) | `GET/PUT /api/users/me` · `PUT /api/users/fcm-token` |
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

---

## 7. ENDPOINT MỚI / THAY ĐỔI THEO SRS (đợt 21/10/2026)

> Chi tiết body/response và việc FE/Mobile cần đổi: `FE_MOBILE_HANDOFF.md` mục 6 và 7. Mọi lỗi nghiệp vụ có thêm `errorCode`: `CONFLICT` (409), `FORBIDDEN` (403), `NOT_FOUND` (404), `TOO_MANY_REQUESTS` (429).

| Phân hệ | Endpoint | Quyền | Ghi chú |
|---|---|---|---|
| Incidents | `GET /api/incidents`, `GET /api/incidents/queue` | Operator, Admin | Trước đây công khai |
| Incidents | `GET /api/incidents/{id}` | Đăng nhập | Citizen chỉ xem sự cố của mình; số điện thoại bị che trừ người báo |
| Incidents | `PUT /api/incidents/{id}/verify` | Operator, Admin | Ghi đè mức độ AI bắt buộc `operatorNotes` ≥ 5 ký tự |
| Incidents | `PUT /api/incidents/{id}/cancel` | Citizen (của mình, trước khi điều xe), Operator, Admin | Bỏ công khai |
| Incidents | `PUT /api/incidents/{id}/lock`, `DELETE /api/incidents/{id}/lock` | Operator, Admin | Khóa mềm 2 phút (BR-15) |
| Incidents | `POST /api/incidents/{id}/reveal-phone` | Operator, Admin, RescueStaff đang làm nhiệm vụ | Ghi Audit Trail |
| Incidents | `POST /api/incidents/{id}/block-reporter` | Operator, Admin | Chặn số báo khống (BR-05) |
| Incidents | `GET /api/incidents/my-reports` | Citizen | FR-CIT-05 |
| Incidents | `GET /api/incidents/{id}/audit-trail` | Operator, Admin | |
| Audit | `GET /api/audit-trail` | Admin | Lọc theo incident/actor/action/thời gian |
| Dispatch | `POST /api/dispatch/assign` | Operator, Admin | Nhận `rescueUnitIds[]`, trả mảng; giới hạn số xe theo mức độ; 409 nếu xe bị giao trước |
| Dispatch | `POST /api/dispatch/assignments/{id}/reject` | RescueStaff, Operator, Admin | Lý do bắt buộc (BR-22) |
| Dispatch | `PUT /api/dispatch/assignments/{id}/status` | RescueStaff (xe của trạm mình), Operator, Admin | Thứ tự bắt buộc Accepted → EnRoute → OnScene |
| Dispatch | `POST /api/dispatch/assignments/{id}/resolve` | RescueStaff, Operator, Admin | Bắt buộc ảnh + `victimsAssistedCount`; sự cố chỉ đóng khi mọi xe xong; xe CoolDown 5 phút |
| Dispatch | `GET /api/dispatch/tracking/incident/{id}`, `GET /api/dispatch/assignments/incident/{id}` | Người báo, Operator, Admin, RescueStaff của trạm được giao | Bỏ công khai; tracking có `units[]` |
| Dispatch | `GET /api/dispatch/recommend` | Operator, Admin | Load-Aware theo BR-11, bán kính tự mở rộng (BR-12); `maxDistanceKm` tùy chọn |
| Stations | `GET/POST /api/stations/{id}/teams`, `PUT/DELETE /api/stations/{id}/teams/{teamId}` | Xem: Admin, Operator, RescueStaff · Sửa: Admin, Operator | Tổ làm việc |
| Stations | `GET /api/stations/{id}/staff` | Admin, Operator, RescueStaff | Trước đây công khai |
| Users | `POST /api/users/{id}/lock`, `POST /api/users/{id}/unlock` | Admin | Operator chỉ quản lý được RescueStaff |
| Blocked numbers | `GET /api/blocked-numbers`, `POST /api/blocked-numbers/{id}/unblock`, `POST /api/blocked-numbers/{id}/extend` | Xem: Admin, Operator · Sửa: Admin | |
| Settings | `GET/PUT /api/settings/escalation`, `GET/PUT /api/settings/dispatch`, `GET /api/settings/defaults` | Xem: Admin, Operator · Sửa: Admin | |
| Notifications | `GET /api/notifications`, `GET /api/notifications/unread-count`, `PUT /api/notifications/{id}/read`, `PUT /api/notifications/read-all` | Đăng nhập | Realtime `ReceiveNotification` |
| Benchmark | `GET /api/benchmark/telemetry-summary` | Admin, Operator | Số liệu RQ2/RQ3 |

**SignalR:** `/hubs/incidents` và `/hubs/tracking` bắt buộc token; `SubscribeIncident` kiểm tra quyền; `UpdateLocation` chỉ cho xe thuộc trạm của nhân viên. Thêm sự kiện `ReceiveNotification`.

**Enum mới:** `RescueUnitStatus.CoolDown`, `RescueUnitStatus.OutOfService`, `DispatchAssignmentStatus.Rejected`, `IncidentEventType` (`None`, `FireExplosion`, `TrafficAccident`), `CrewRole`, `BlockDuration`, `BlockedNumberStatus`, `AuditAction.PhoneRevealed`, `AuditAction.ReporterBlocked`.

**Giới hạn:** SOS 5 lần/phút/IP và 1 sự cố đang hoạt động / 3 phút / tài khoản hoặc SĐT; mô tả SOS ≤ 200 ký tự; đăng nhập 10 lần/phút/IP; sai mật khẩu 5 lần khóa 15 phút.



---

## 8. ADMIN: QUYỀN XEM ĐỘI XE, NHẬT KÝ QUẢN TRỊ, KHÓA TỨC THÌ, BÁO CÁO, TRƯỞNG TRẠM (đợt 22/10/2026)

### 8.1. Quyền xem đội xe / trạm

| Endpoint | Trước | Nay |
|---|---|---|
| `GET /api/rescue-units` | Công khai | **Admin, Operator** |
| `GET /api/rescue-units/{id}` | Công khai | Admin, Operator; RescueStaff chỉ xe trạm mình (403) |
| `GET /api/rescue-units/station/{stationId}` | Công khai | Admin, Operator; RescueStaff chỉ trạm mình (403) |
| `PUT /api/rescue-units/{id}/location`, `/status` | RescueStaff sửa được xe mọi trạm | RescueStaff chỉ xe trạm mình (403); lỗi nghiệp vụ trả 400 thay vì 500 |
| `GET /api/stations`, `/api/stations/{id}` | Công khai, lộ xe + email/SĐT nhân sự | Bắt buộc đăng nhập. Ai không quản lý trạm đó (Citizen, nhân viên trạm khác) nhận `rescueUnits: []`, `staffMembers: []`, `activeUnitsCount/staffCount = 0` |
| `GET /api/stations/{id}/staff`, `/teams`, `GET /api/users/station/{stationId}` | RescueStaff xem mọi trạm | RescueStaff chỉ trạm mình (403) |

Citizen chỉ thấy xe được phân công cho sự cố của mình qua `GET /api/dispatch/tracking/incident/{id}` (không đổi). Thêm/sửa xe: Admin, Operator; xóa xe: Admin.

### 8.2. Operator phụ trách trạm

`POST/PUT /api/stations` nhận thêm `headOperatorId` (tùy chọn). Phải là tài khoản **Operator đang hoạt động**, nếu không thì trả 400. `StationResponseDto` có thêm `headOperatorId`, `headOperatorName`, `headOperatorPhone`. Xóa tài khoản Operator thì các trạm do người đó làm trưởng sẽ bỏ trống Operator phụ trách trạm.

### 8.3. Cấu hình leo thang (Admin chỉnh qua API)

- **Ngưỡng thời gian và bán kính:** `GET/PUT /api/settings/escalation` (Admin sửa, Operator xem):
  ```json
  { "dayThresholdMinutes": 5, "nightThresholdMinutes": 2.5, "nightStart": "22:00", "nightEnd": "06:00", "radiusMultiplier": 2, "maxRadiusKm": 30 }
  ```
- **Quy tắc:** `/api/escalation/rules` chỉ còn `minSeverityLevel` (1–3, Level 0 luôn được quét), `notifyRoles`, `isActive`, `description`.
  - `notifyRoles` là mảng JSON gồm `Admin`, `Operator`, `RescueStaff`. Giá trị khác trả 400.
  - Trường này **nay có tác dụng thật**: trước đây hệ thống luôn báo cho Operator và Admin.
- **Bỏ khỏi quy tắc:** `timeoutMinutes`, `expandRadiusPercent` (trước đây không có tác dụng). Response thêm các trường chỉ đọc `effectiveDayThresholdMinutes`, `effectiveNightThresholdMinutes`, `effectiveNightStart`, `effectiveNightEnd`, `effectiveRadiusMultiplier`, `effectiveMaxRadiusKm`.

### 8.4. Nhật ký thao tác quản trị (BR-AD-06)

`GET /api/audit-trail/system?entityType=&entityId=&actorId=&action=&from=&to=&pageIndex=&pageSize=` (Admin)

Mỗi dòng: `{ id, actorId, actorName, actorRole, action, entityType, entityId, entityName, description, oldValueJson, newValueJson, ipAddress, timestamp }`.

| `entityType` | Thao tác được ghi |
|---|---|
| `Station` | Created, Updated, Deleted |
| `RescueUnit` | Created, Updated, StatusChanged, Deleted |
| `User` | Created, Updated, Deleted, Locked, Unlocked, StationAssigned |
| `StationTeam` | Created, Updated, Deleted |
| `Settings` | SettingsUpdated (`entityName` = `escalation` / `dispatch`) |
| `EscalationRule` | Created, Updated, Deleted |
| `BlockedNumber` | Unblocked, BlockExtended |
| `Report` | Exported |

Nhật ký thao tác trên sự cố vẫn ở `GET /api/audit-trail` như trước.

### 8.5. Khóa tài khoản có hiệu lực ngay

- Mỗi request có token, backend so token với trạng thái tài khoản (bộ nhớ đệm 60 giây, bị xóa ngay khi Admin thay đổi). Token bị từ chối (**401**) khi tài khoản bị **khóa / xóa / tạm ngưng**, **đổi role**, hoặc **đổi trạm**. Header `X-Auth-Error` (đã URL-encode) chứa lý do, ví dụ "Tài khoản đã bị khóa.".
- SignalR gửi sự kiện **`ForceLogout`** `{ reason }` tới người bị khóa / xóa / đổi quyền / đổi trạm. Mọi lời gọi hub từ kết nối cũ đều bị từ chối và kết nối bị đóng.
- Đổi trạm hoặc đổi role thì client chỉ cần `POST /api/auth/refresh` để lấy token mới. Riêng khóa tài khoản thì refresh token cũng đã bị thu hồi.

### 8.6. Lịch sử sự cố đã đóng & xuất báo cáo (Admin)

| Endpoint | Mô tả |
|---|---|
| `GET /api/reports/incidents` | Lịch sử sự cố `Completed` / `Cancelled`, phân trang (tối đa 100 / trang) |
| `GET /api/reports/incidents/summary` | Tổng, hoàn thành, hủy, số lượng theo mức độ, thời gian phản ứng / xử lý trung bình |
| `GET /api/reports/incidents/export?format=Csv\|Pdf` | Tải file (tối đa 5000 dòng). CSV UTF-8 có BOM (mở thẳng bằng Excel), PDF khổ A4 ngang. Tên file trong `Content-Disposition` |

Bộ lọc chung (query): `from`, `to` (UTC, theo thời điểm **báo cáo**), `stationId` (trạm có xe tham gia), `status` = `All|Completed|Cancelled`, `severity`, `search` (tiêu đề / địa chỉ), `pageIndex`, `pageSize`. Nếu `from > to` thì trả 400.

Mỗi dòng: `{ id, title, locationAddress, severity, aiSeverityScore, status, cancellationReason, resolutionSummary, reportedAt, verifiedAt, firstDispatchedAt, firstArrivedAt, closedAt, responseMinutes, resolutionMinutes, verifiedByName, closedByName, stations[], units[] }`.

**CORS** đã cho phép client đọc header `X-Auth-Error` và `Content-Disposition`.


---

## 9. VẬN HÀNH THEO TRẠM: MỖI OPERATOR PHỤ TRÁCH 1 TRẠM (đợt 22/10/2026)

Migration `Station_Scoped_Operations` đã áp lên Neon.

### 9.1. Mô hình
- Một khu vực phục vụ (VD: KCNC) có nhiều trạm. **Mỗi trạm 1 Operator phụ trách** (`Station.headOperatorId`, unique). Mỗi Operator chỉ phụ trách 1 trạm.
- Gán Operator phụ trách trạm qua `PUT /api/stations/{id}` (`headOperatorId`) sẽ tự đặt `user.stationId` của Operator. Operator cũ bị gỡ trạm.
  - Gán một Operator đang phụ trách trạm khác thì trả **400**.
  - Token của cả 2 Operator bị hủy (401). Client gọi `refresh` để lấy claim `StationId` mới.
- `POST/PUT /api/users` (Admin) với `role = Operator`, `stationId = X` thì tự thành Operator phụ trách trạm X. Trạm X đã có Operator thì trả 400.

### 9.2. Phạm vi của Operator (Admin không giới hạn)

| Chức năng | Operator |
|---|---|
| Hàng đợi `GET /api/incidents/queue`, danh sách `GET /api/incidents` | Chỉ sự cố có `responsibleStationId` = trạm mình (hoặc chưa xác định trạm) |
| `lock`, `verify`, `cancel`, `reveal-phone`, `block-reporter`, `audit-trail`, `recommend`, `assign`, `transfer` | Chỉ sự cố trạm mình; trạm khác trả **403** "Sự cố này thuộc trạm khác phụ trách." |
| `GET /api/incidents/{id}` | Sự cố trạm mình, hoặc sự cố có xe trạm mình đang hỗ trợ |
| Tài khoản | Chỉ tạo/sửa `RescueStaff`; nhân viên **luôn thuộc trạm mình** (bỏ qua `stationId` client gửi). `GET /api/users` chỉ trả nhân sự trạm mình |
| `assign-station-team` | Chỉ kéo nhân viên **chưa có trạm** về trạm mình, hoặc sắp xếp trong trạm mình |
| Tổ `/api/stations/{id}/teams`, nhân sự `/api/stations/{id}/staff`, `/api/users/station/{id}` | Chỉ trạm mình |
| Xe | Thêm / sửa / đổi trạng thái **xe trạm mình**. Chuyển xe sang trạm khác và xóa xe chỉ Admin. **Xem** vị trí, trạng thái xe mọi trạm (live map, điều hỗ trợ) |
| `GET /api/stations/{id}` trạm khác | Chỉ thông tin công khai (không có xe, nhân sự) |

### 9.3. Trạm phụ trách sự cố
- Khi SOS được tạo: trạm đang hoạt động **gần nhất có bán kính phủ chứa vị trí**. Không trạm nào phủ tới thì lấy trạm gần nhất.
- `IncidentResponseDto` thêm `responsibleStationId`, `responsibleStationName`, `isOutOfServiceArea`, `distanceOutsideAreaKm`.
- **Chuyển trạm:** `POST /api/incidents/{id}/transfer` `{ "stationId": "...", "reason": "≥ 5 ký tự" }` (Operator trạm đang phụ trách / Admin). Khóa mềm được nhả. Ghi audit `TransferredStation`. Operator trạm nhận được báo realtime và có thông báo.
- **Điều xe xuyên trạm (Load-Aware):** `recommend` vẫn trả xe mọi trạm, mỗi xe có `isSupportUnit`. Khi `assign` xe trạm khác: Operator trạm có xe nhận `ReceiveSupportDispatched` và thông báo "XE TRẠM BẠN ĐƯỢC ĐIỀU HỖ TRỢ". Staff trạm đó nhận lệnh như bình thường.
- **Leo thang:**
  - Cấp 1 (quá ngưỡng): báo Operator trạm phụ trách + Admin (`ReceiveEscalationAlert`, `tier: 1`).
  - **Cấp 2 (quá gấp đôi ngưỡng, BR-17):** báo **mọi Operator** + Admin (`tier: 2`).

### 9.4. Staff
- `GET /api/users/my-team` thêm `stationOperator { id, fullName, phoneNumber, email, avatarUrl }`.
- Staff chỉ xem sự cố / tracking / `SubscribeIncident` khi **xe trạm mình đang làm nhiệm vụ** của sự cố đó, hoặc vừa hoàn tất ≤ 30 phút. Trước đây staff mọi trạm xem được chi tiết mọi sự cố.
- `GET /api/dispatch/tracking/incident/{id}`: dùng chung cho Citizen (sự cố của mình) và Staff (được phân công). Trả tuyến đường bộ của từng xe.

### 9.5. Khu vực phục vụ

| Endpoint | Quyền |
|---|---|
| `GET /api/settings/service-area` | **Công khai** (app Citizen cảnh báo trước khi gửi) |
| `PUT /api/settings/service-area` | Admin |

```json
{ "enabled": true, "name": "Khu Công nghệ cao", "centerLat": 10.8411, "centerLng": 106.8098, "radiusKm": 10 }
```

SOS ngoài bán kính **vẫn được tiếp nhận** và giao trạm gần nhất, kèm `isOutOfServiceArea = true`, `distanceOutsideAreaKm`. Backend gửi realtime `ReceiveOutOfAreaAlert` và thông báo khẩn tới Operator trạm đó + Admin. Mặc định `enabled = false` cho tới khi Admin cấu hình.

### 9.6. Chỉ đường đường bộ (OSRM)
- `recommend`: thêm `roadDistanceKm`, `roadDurationMinutes`, `isApproximate`. ETA = thời gian đường bộ + 2 phút xuất xe. Cost Load-Aware dùng quãng đường đường bộ.
- `tracking`: `routeTrajectory` là tuyến đường bộ thật (hàng trăm điểm), `distanceKm` / `estimatedArrivalMinutes` theo đường bộ. Thêm `isApproximateRoute` (true = máy chủ chỉ đường lỗi, chỉ có đoạn thẳng 2 điểm → **vẽ nét đứt**). Đã **bỏ đường cong giả**.
- Cấu hình `Routing:OsrmBaseUrl`. Hướng dẫn tự host Docker xem `README.md`.

### 9.7. SignalR — nhóm và sự kiện
Nhóm tự gán khi kết nối `/hubs/incidents`:
- `operators`: mọi Operator + Admin.
- `operators:station:{id}`: Operator của trạm.
- `admins`: Admin.
- `station:{id}`: nhân viên trạm.

| Sự kiện | Người nhận |
|---|---|
| `ReceiveNewIncident`, `ReceiveIncidentUpdate` | Operator trạm phụ trách + Admin (sự cố chưa có trạm → mọi Operator) |
| `ReceiveJobAssigned` | **Chỉ staff trạm có xe** (trước đây mọi staff mọi trạm) + Operator trạm phụ trách / trạm có xe + Admin |
| `ReceiveAssignmentStatus` | Như trên + nhóm `incident:{id}` (bản rút gọn) |
| `ReceiveEscalationAlert` | Cấp 1: trạm phụ trách + Admin; cấp 2: mọi Operator |
| **`ReceiveOutOfAreaAlert`** | Operator trạm phụ trách + Admin |
| **`ReceiveSupportDispatched`** | Operator trạm có xe được điều hỗ trợ |
| **`ReceiveIncidentTransferred`** | Operator trạm cũ, trạm mới, Admin (trạm mới nhận thêm `ReceiveNewIncident`) |
| **`ReceiveRescueUnitStatus`** | Mọi Operator / Admin + staff trạm có xe. Phát khi giao xe, đổi tiến độ, hoàn tất (CoolDown), **hết CoolDown**, đổi tay |
| `ReceiveNotification`, `ForceLogout` | Từng người dùng |


---

## 10. SỬA THEO GAP ANALYSIS CỦA FE (đợt 23/10/2026)

Migration `Incident_Cancellation_EventType` đã áp lên Neon.

### 10.1. Hủy sự cố — `PUT /api/incidents/{id}/cancel`
Body: `{ "reason": "…", "reasonCode": "FalseReport|Duplicate|TestReport|Other" }`. `reasonCode` chỉ dành cho Operator / Admin; Citizen tự hủy luôn là `CitizenCancelled`.
- **Không còn ghi đè** `verifiedAt`, `verifiedByUserId`, `operatorNotes`.
- `IncidentResponseDto` thêm `cancellationReason`, `cancelledAt`, `cancelledByUserId`, `cancelledByName`, `cancellationNote`.
- Ghi `IncidentStatusHistory` + `IncidentAuditTrail` (`Cancelled`). Nhả khóa mềm.
- Chỉ thu hồi các lượt **đang hoạt động**: lượt đã từ chối / hoàn tất giữ nguyên, không đụng tới xe của chúng.
- Xe được thu hồi phát `ReceiveRescueUnitStatus`.
- Dữ liệu cũ: migration khôi phục `verifiedAt` / `verifiedByUserId` / `operatorNotes` thật từ lịch sử trạng thái (lần xác minh gốc), và tách thông tin hủy sang các cột mới.

### 10.2. Xác minh — `PUT /api/incidents/{id}/verify`
```json
{ "confirmedSeverity": "Level3", "confirmedEventType": "FireExplosion", "operatorNotes": "≥ 5 ký tự khi cần", "adjustedTitle": null }
```
- `confirmedEventType` (`FireExplosion` / `TrafficAccident`) **bắt buộc khi loại sự cố hiện tại là `None`** (AI không xác định hoặc không có ảnh) → nếu thiếu trả 400.
- `operatorNotes` ≥ 5 ký tự bắt buộc khi: ghi đè mức độ AI, **hoặc sự cố đang `Unclassified`** (kể cả SOS không ảnh).
- Sửa loại sự cố so với AI được ghi audit `SeverityOverridden`.
- `IncidentResponseDto.eventType` = loại đã xác nhận. Gợi ý xe dùng loại này để khớp chủng loại xe (BR-13).

### 10.3. Lọc nhiều trạng thái — `GET /api/incidents`
`?statuses=Verified,Dispatched,InProgress,Escalated&pageSize=200`. `pageSize` tối đa 200 (các danh sách khác vẫn 50). Trạng thái sai trả 400. `status` cũ vẫn dùng được.

### 10.4. `GET /api/dispatch/assignments/active` (Operator, Admin)
Mọi lượt `Dispatched` / `Accepted` / `EnRoute` / `OnScene` kèm sự cố, xe, trạm.
- Operator: sự cố trạm mình **hoặc** xe trạm mình (xe đi hỗ trợ).
- Admin: toàn bộ.

### 10.5. `GET /api/rescue-units`
Thêm `stationId`, `status` (ngoài `unitType`, `availableOnly`). Admin chuyển xe sang trạm khác thì **tọa độ xe = tọa độ trạm mới**. Migration đã đưa các xe rảnh nằm sai trạm về đúng trạm.

### 10.6. Realtime khóa mềm — `ReceiveIncidentLock`
Payload `{ incidentId, lockedByOperatorId, lockedByOperatorName, lockExpiresAt, changedAt }`; `lockedByOperatorId = null` → đã nhả. Gửi tới Operator trạm phụ trách + Admin khi khóa / gia hạn / nhả / chuyển trạm / hủy.

### 10.7. Hạ thang
Sự cố `Escalated` được giao xe hoặc có đơn vị nhận lệnh thì:
- Ghi audit **`DeEscalated`**.
- Đóng các lượt leo thang (`EscalationLog.resolvedAt`, `resolutionTimeMs`).
- Phát `ReceiveIncidentUpdate`.

### 10.8. Tự hủy sự cố quá hạn
Worker quét mỗi giờ: sự cố `Pending` / `AiProcessing` / `AiProcessed` **chưa xác minh** quá `AutoExpireHours` (mặc định 24, trong `PUT /api/settings/dispatch`, khoảng 1–168) → `Cancelled`, `cancellationReason = AutoExpired`, kèm audit. Sự cố đã xác minh / đã điều xe không bao giờ bị tự hủy.

### 10.9. Dữ liệu thực nghiệm từng sự cố — `GET /api/benchmark/incidents?from&to&format=Json|Csv`
Mỗi dòng gồm các nhóm trường sau:
- Mốc thời gian: `createdAt`, `aiProcessedAt`, `verifiedAt`, `firstDispatchAt`, `firstAcceptedAt`, `firstArrivedAt`, `closedAt`.
- AI: `aiModelLatencyMs`, `aiSeverity`, `aiEventType`.
- Xác nhận và điều phối: `confirmedSeverity`, `confirmedEventType`, `wasAiOverridden`, `algorithm`, `unitsDispatched`, `cancellationReason`.
- Độ trễ suy ra: `tAiMs`, `tOperatorMs`, `tDispatchMs`, `tResponseMs`, `tResolutionMs`.

Dùng cho RQ2 / RQ3. CSV có giờ dạng ISO-8601 UTC.


---

## 11. BỔ SUNG REALTIME (đợt 23/10/2026)

| Sự kiện | Hub | Phát khi | Người nhận | Payload |
|---|---|---|---|---|
| `ReceiveRescueUnitLocation` | `/hubs/tracking` | Gửi GPS qua hub `UpdateLocation` **hoặc** `PUT /api/rescue-units/{id}/location` (trước đây REST không phát) | Mọi Operator / Admin, nhân viên trạm có xe, người theo dõi sự cố xe đang xử lý | `{ rescueUnitId, plateNumber, stationId, incidentId, latitude, longitude, headingDegrees, speedKmh, timestampUtc }` |
| `ReceiveRescueUnitStatus` | `/hubs/incidents` | Thêm: khi **tạo xe** và **xóa xe** | Mọi Operator / Admin, nhân viên trạm | Thêm `unitType`, `latitude`, `longitude`, `isDeleted` |
| `ReceiveIncidentUpdate` | `/hubs/incidents` | Thêm: khi worker **leo thang** sự cố (`status = Escalated`) | Operator trạm phụ trách + Admin | `IncidentResponseDto` (rút gọn) |
| **`ReceiveTeamChanged`** | `/hubs/incidents` | Tạo / sửa / xóa tổ; gán / gỡ nhân viên khỏi trạm hoặc tổ | Nhân viên trạm, Operator trạm, Admin | `{ stationId, teamId, teamName, action: Created\|Updated\|Deleted\|MembersChanged, staffId, changedAt }` |
| **`ReceiveSettingsChanged`** | `/hubs/incidents` | `PUT /api/settings/escalation`, `/dispatch`, `/service-area` | Mọi Operator / Admin | `{ key: escalation\|dispatch\|serviceArea, updatedAt, updatedByName }` |
| **`ReceiveNotificationRead`** | `/hubs/incidents` | `PUT /api/notifications/{id}/read`, `/read-all` | Chính người dùng (mọi thiết bị) | `{ notificationIds[], all, unreadCount }` |

Về `incidentId` của vị trí xe: **server tự gắn** từ lượt phân công đang chạy của xe; giá trị `incidentId` client gửi lên hub bị bỏ qua. Vì vậy Citizen / Staff đã `SubscribeIncident` trên `/hubs/tracking` luôn nhận được vị trí xe của sự cố, và client không thể đẩy vị trí vào sự cố khác.

`PUT /api/rescue-units/{id}/location` nhận thêm `headingDegrees` (0–360) và `speedKmh` (0–300), đều tùy chọn.

---

## Thay đổi phạm vi hồ sơ khẩn cấp (22/10/2026)
* Đã bỏ danh sách nhiều người thân và cấu hình tin nhắn tự động; giữ một người liên hệ trong hồ sơ, Medical ID, Special Info và cơ chế song ngữ.
* Migration `Remove_Multiple_Emergency_Contacts_And_Sms` gỡ bảng/cột của chức năng đã bỏ. Trước khi gỡ, tài khoản chưa có cả ba trường liên hệ cũ sẽ được điền từ một liên hệ còn hiệu lực, ưu tiên primary.
* Các migration cũ giữ nguyên để bảo toàn lịch sử schema. Migration mới chưa được áp dụng trong lần chỉnh sửa này.
* Khi triển khai, sao lưu dữ liệu trước khi áp dụng: danh sách liên hệ phụ và cấu hình tin nhắn sẽ bị loại khỏi database. Down migration chỉ khôi phục cấu trúc, không khôi phục đầy đủ dữ liệu đã bỏ.
* API hiện tự chạy migration lúc khởi động; cần kiểm tra bản sao lưu và migration trước khi khởi động với database đang có dữ liệu.
