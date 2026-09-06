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
