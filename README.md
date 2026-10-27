# Emergency Dispatch Backend

Backend API cho hệ thống điều phối cấp cứu — ASP.NET Core 9.0 + Clean Architecture.

## Tech Stack

| Thành phần | Công nghệ |
|---|---|
| Framework | ASP.NET Core Web API (.NET 9.0) |
| Database | PostgreSQL + EF Core (Npgsql) |
| Auth | JWT Bearer (Access + Refresh Token) |
| Realtime | SignalR |
| API Docs | Swagger (Swashbuckle) |
| Image Storage | Cloudinary |
| Email | Gmail SMTP |

## Kiến trúc (Clean Architecture)

```
EmergencyDispatch.sln
├── EmergencyDispatch.Domain            # Entities, Enums, Repository Interfaces
├── EmergencyDispatch.Application       # DTOs, Service Interfaces, Business Logic
├── EmergencyDispatch.Infrastructure    # DbContext, Repositories, External Services
└── EmergencyDispatch.API               # Controllers, Hubs, Program.cs (DI + Middleware)
```

## Bắt đầu

### Yêu cầu
- .NET 9 SDK
- PostgreSQL
- Visual Studio 2022 / VS Code / Rider

### Chạy project
```bash
dotnet restore
dotnet build
dotnet run --project EmergencyDispatch.API
```

### Cấu hình bắt buộc trong `appsettings.json` (file không commit, mỗi người tự giữ)
```json
"JwtSettings": {
  "Key": "<chuỗi bí mật ≥ 32 ký tự>",
  "Issuer": "EmergencyDispatchAPI",
  "Audience": "EmergencyDispatchClient",
  "ExpireMinutes": 60
},
"Cors": {
  "AllowedOrigins": [ "https://<domain-web-khi-deploy>" ]
}
```
- Thiếu `JwtSettings:Key` hoặc key ngắn hơn 32 byte → API **không khởi động** (không còn key mặc định trong code).
- `ExpireHours` cũ vẫn đọc được, nhưng nên đổi sang `ExpireMinutes`.
- Bỏ trống `Cors:AllowedOrigins` khi chạy local: môi trường Development tự cho phép localhost và IP mạng LAN. Khi deploy **bắt buộc** điền domain web, nếu không trình duyệt sẽ bị chặn.

### Migration
```bash
dotnet ef migrations add <TenMigration> --project EmergencyDispatch.Infrastructure --startup-project EmergencyDispatch.API
dotnet ef database update --project EmergencyDispatch.Infrastructure --startup-project EmergencyDispatch.API
```

## Chỉ đường đường bộ (OSRM)

Backend dùng OSRM (API `/route` và `/table`) để vẽ tuyến đường trên live map, tính quãng đường, ETA và xếp hạng gợi ý xe theo đường bộ thật.
Địa chỉ máy chủ cấu hình ở `appsettings.json`:

```json
"Routing": { "OsrmBaseUrl": "https://router.project-osrm.org", "TimeoutSeconds": 5 }
```

Mặc định dùng máy chủ demo công cộng của OSRM: miễn phí nhưng có giới hạn lượt gọi và không đảm bảo ổn định. Khi demo hoặc bảo vệ, nên **tự host OSRM bằng Docker** (API giống hệt, chỉ cần đổi URL):

```bash
mkdir osrm && cd osrm
# 1. Bản đồ Việt Nam từ OpenStreetMap (~250 MB)
curl -L -o vietnam-latest.osm.pbf https://download.geofabrik.de/asia/vietnam-latest.osm.pbf
# 2. Tiền xử lý cho xe ô tô (chạy 1 lần, ~10–20 phút, cần ~4 GB RAM)
docker run -t -v "${PWD}:/data" osrm/osrm-backend osrm-extract -p /opt/car.lua /data/vietnam-latest.osm.pbf
docker run -t -v "${PWD}:/data" osrm/osrm-backend osrm-partition /data/vietnam-latest.osrm
docker run -t -v "${PWD}:/data" osrm/osrm-backend osrm-customize /data/vietnam-latest.osrm
# 3. Chạy máy chủ ở cổng 5001
docker run -d --name osrm -p 5001:5000 -v "${PWD}:/data" osrm/osrm-backend osrm-routed --algorithm mld /data/vietnam-latest.osrm
```

Sau đó đặt `"OsrmBaseUrl": "http://localhost:5001"` rồi khởi động lại backend.
Nếu máy chủ chỉ đường lỗi, backend tự chuyển sang ước lượng theo chim bay và trả `isApproximateRoute: true` / `isApproximate: true` (FE vẽ nét đứt).

## API Documentation cho Frontend & Mobile

Chi tiết toàn bộ tài liệu tích hợp API cho đội ngũ Frontend & Mobile được tổng hợp tại:
👉 **[API_DOCUMENTATION.md](file:///d:/SEP490/emergency-dispatch-backend/API_DOCUMENTATION.md)**

* **Swagger UI**: `http://localhost:5000/swagger`
* **Cơ sở dữ liệu**: Neon PostgreSQL Cloud (Đã tự động migrate và seed sẵn tài khoản mẫu).
* **Tài khoản test nhanh**:
  * Admin: `admin@emergencydispatch.com` / `Admin@123456`
  * Operator: `operator@emergencydispatch.com` / `Operator@123456`
  * Cứu hộ: `staff@emergencydispatch.com` / `Staff@123456`
  * Người dân: `citizen@emergencydispatch.com` / `Citizen@123456`

## License
Đồ án SEP490 — FPT University.
