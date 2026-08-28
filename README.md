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

## License
Đồ án SEP490 — FPT University.
