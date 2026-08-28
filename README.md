# Emergency Dispatch Backend

ASP.NET Core backend for the Emergency Dispatch system.

## Tech Stack
- ASP.NET Core
- Entity Framework Core
- SQL Server

## Getting Started

### Prerequisites
- .NET 8 SDK (or later)
- SQL Server
- Visual Studio 2022 / VS Code / Rider

### Run the project
```bash
dotnet restore
dotnet build
dotnet run
```

## Project Structure
```
├── Controllers/        # API Controllers
├── Models/             # Entity models
├── DTOs/               # Data Transfer Objects
├── Services/           # Business logic
├── Repositories/       # Data access layer
├── Migrations/         # EF Core migrations
└── Program.cs          # Application entry point
```

## License
This project is for educational purposes (SEP490).
