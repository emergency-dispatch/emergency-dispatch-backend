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

### Migration
```bash
dotnet ef migrations add <TenMigration> --project EmergencyDispatch.Infrastructure --startup-project EmergencyDispatch.API
dotnet ef database update --project EmergencyDispatch.Infrastructure --startup-project EmergencyDispatch.API
```

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
