using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(ApplicationDbContext context, bool seedDemoAdmin = false)
    {
        // 0. Đảm bảo schema đồng bộ cho các cột và bảng mới (phòng trường hợp migration chưa apply trên cloud database)
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""Stations"" ADD COLUMN IF NOT EXISTS ""CoverageRadiusKm"" double precision NOT NULL DEFAULT 10;
                ALTER TABLE ""Stations"" ADD COLUMN IF NOT EXISTS ""Email"" text;
                ALTER TABLE ""Stations"" ADD COLUMN IF NOT EXISTS ""OperatingHours"" text;

                CREATE TABLE IF NOT EXISTS ""EscalationRules"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""MinSeverityLevel"" integer NOT NULL,
                    ""TimeoutMinutes"" integer NOT NULL,
                    ""NotifyRoles"" text NOT NULL,
                    ""ExpandRadiusPercent"" integer NOT NULL,
                    ""IsActive"" boolean NOT NULL,
                    ""Description"" text,
                    CONSTRAINT ""PK_EscalationRules"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""EscalationLogs"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""IncidentId"" uuid NOT NULL,
                    ""EscalationRuleId"" uuid NOT NULL,
                    ""EscalationLevel"" integer NOT NULL,
                    ""NotifiedRoles"" text,
                    ""NotifiedUserIds"" text,
                    ""ActionTaken"" text,
                    ""TriggerTime"" timestamp with time zone NOT NULL,
                    ""ResolvedAt"" timestamp with time zone,
                    ""ResolutionTimeMs"" bigint,
                    CONSTRAINT ""PK_EscalationLogs"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""CitizenFeedbacks"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""IncidentId"" uuid NOT NULL,
                    ""CitizenUserId"" uuid NOT NULL,
                    ""Rating"" integer NOT NULL,
                    ""Comment"" text,
                    CONSTRAINT ""PK_CitizenFeedbacks"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""IncidentStatusHistories"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""IncidentId"" uuid NOT NULL,
                    ""OldStatus"" integer NOT NULL,
                    ""NewStatus"" integer NOT NULL,
                    ""ChangedByUserId"" uuid,
                    ""ChangedByRole"" character varying(50),
                    ""Notes"" text,
                    ""ChangedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_IncidentStatusHistories"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""IncidentAuditTrails"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""IncidentId"" uuid NOT NULL,
                    ""Action"" character varying(100) NOT NULL,
                    ""ActorId"" uuid,
                    ""ActorRole"" character varying(50),
                    ""Details"" text,
                    ""PayloadJson"" text,
                    ""IpAddress"" character varying(45),
                    ""UserAgent"" character varying(500),
                    CONSTRAINT ""PK_IncidentAuditTrails"" PRIMARY KEY (""Id"")
                );

                ALTER TABLE IF EXISTS ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""Action"" integer NOT NULL DEFAULT 0;
                ALTER TABLE IF EXISTS ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""OldStateJson"" text;
                ALTER TABLE IF EXISTS ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""NewStateJson"" text;
                ALTER TABLE IF EXISTS ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""Description"" text;
                ALTER TABLE IF EXISTS ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""Timestamp"" timestamp with time zone NOT NULL DEFAULT now();
            ");
        }
        catch { }

        // 0.1. Đồng bộ bảng IncidentAuditTrails cũ (schema legacy có cột ToStatus/FromStatus NOT NULL)
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""ActorId"" uuid;
                ALTER TABLE ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""ActorRole"" character varying(50);
                ALTER TABLE ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""IpAddress"" character varying(50);
                ALTER TABLE ""IncidentAuditTrails"" ADD COLUMN IF NOT EXISTS ""UserAgent"" character varying(500);

                DO $$
                DECLARE col record;
                BEGIN
                    FOR col IN
                        SELECT column_name FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'IncidentAuditTrails'
                          AND is_nullable = 'NO'
                          AND column_name NOT IN ('Id','IncidentId','Action','Timestamp','CreatedAt','IsDeleted')
                    LOOP
                        EXECUTE format('ALTER TABLE ""IncidentAuditTrails"" ALTER COLUMN %I DROP NOT NULL', col.column_name);
                    END LOOP;
                END $$;
            ");
        }
        catch { }

        // 1. Seed Trạm cứu hộ mẫu
        if (!await context.Stations.AnyAsync())
        {
            var stations = new List<Station>
            {
                new()
                {
                    Name = "Trạm Cứu hộ Trung tâm Quận 1",
                    Address = "123 Lê Lợi, Phường Bến Thành, Quận 1, TP. Hồ Chí Minh",
                    Latitude = 10.776889,
                    Longitude = 106.700806,
                    PhoneNumber = "02838221115",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    Name = "Trạm Cứu hộ Khu vực Quận 7",
                    Address = "456 Nguyễn Lương Bằng, Phường Tân Phú, Quận 7, TP. Hồ Chí Minh",
                    Latitude = 10.732842,
                    Longitude = 106.719688,
                    PhoneNumber = "02838731115",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            await context.Stations.AddRangeAsync(stations);
            await context.SaveChangesAsync();
        }

        // 2. Seed Phương tiện cứu hộ mẫu (RescueUnits)
        if (!await context.RescueUnits.AnyAsync())
        {
            var stations = await context.Stations.ToListAsync();
            var q1Station = stations.FirstOrDefault(s => s.Name.Contains("Quận 1")) ?? stations.First();
            var q7Station = stations.FirstOrDefault(s => s.Name.Contains("Quận 7")) ?? stations.Last();

            var units = new List<RescueUnit>
            {
                // Trạm Quận 1
                new()
                {
                    PlateNumber = "51A-115.01",
                    UnitType = RescueUnitType.Ambulance,
                    Status = RescueUnitStatus.Available,
                    CurrentLat = q1Station.Latitude,
                    CurrentLng = q1Station.Longitude,
                    LastLocationUpdateAt = DateTime.UtcNow,
                    StationId = q1Station.Id,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    PlateNumber = "51A-001.14",
                    UnitType = RescueUnitType.FireTruck,
                    Status = RescueUnitStatus.Available,
                    CurrentLat = q1Station.Latitude,
                    CurrentLng = q1Station.Longitude,
                    LastLocationUpdateAt = DateTime.UtcNow,
                    StationId = q1Station.Id,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    PlateNumber = "51A-002.14",
                    UnitType = RescueUnitType.LadderTruck,
                    Status = RescueUnitStatus.Available,
                    CurrentLat = q1Station.Latitude,
                    CurrentLng = q1Station.Longitude,
                    LastLocationUpdateAt = DateTime.UtcNow,
                    StationId = q1Station.Id,
                    CreatedAt = DateTime.UtcNow
                },
                // Trạm Quận 7
                new()
                {
                    PlateNumber = "51B-115.02",
                    UnitType = RescueUnitType.Ambulance,
                    Status = RescueUnitStatus.Available,
                    CurrentLat = q7Station.Latitude,
                    CurrentLng = q7Station.Longitude,
                    LastLocationUpdateAt = DateTime.UtcNow,
                    StationId = q7Station.Id,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    PlateNumber = "51B-003.14",
                    UnitType = RescueUnitType.FireTruck,
                    Status = RescueUnitStatus.Available,
                    CurrentLat = q7Station.Latitude,
                    CurrentLng = q7Station.Longitude,
                    LastLocationUpdateAt = DateTime.UtcNow,
                    StationId = q7Station.Id,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    PlateNumber = "51B-005.99",
                    UnitType = RescueUnitType.HeavyRescueVehicle,
                    Status = RescueUnitStatus.Available,
                    CurrentLat = q7Station.Latitude,
                    CurrentLng = q7Station.Longitude,
                    LastLocationUpdateAt = DateTime.UtcNow,
                    StationId = q7Station.Id,
                    CreatedAt = DateTime.UtcNow
                }
            };

            await context.RescueUnits.AddRangeAsync(units);
            await context.SaveChangesAsync();
        }

        // 3. Seed Tài khoản mẫu (Admin, Operator, RescueStaff, Citizen)
        if (!await context.Users.AnyAsync())
        {
            var defaultStation = await context.Stations.FirstOrDefaultAsync();

            var users = new List<User>
            {
                new()
                {
                    FullName = "System Administrator",
                    Email = "admin@emergencydispatch.com",
                    PhoneNumber = "0901234567",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123456"),
                    Role = UserRole.Admin,
                    Status = UserStatus.Active,
                    IsEmailVerified = true,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    FullName = "Nguyễn Văn Điều Phối",
                    Email = "operator@emergencydispatch.com",
                    PhoneNumber = "0902345678",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Operator@123456"),
                    Role = UserRole.Operator,
                    Status = UserStatus.Active,
                    IsEmailVerified = true,
                    StationId = defaultStation?.Id,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    FullName = "Trần Cứu Hộ",
                    Email = "staff@emergencydispatch.com",
                    PhoneNumber = "0903456789",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Staff@123456"),
                    Role = UserRole.RescueStaff,
                    Status = UserStatus.Active,
                    IsEmailVerified = true,
                    BloodType = BloodType.B_Positive,
                    StationId = defaultStation?.Id,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    FullName = "Lê Người Dân",
                    Email = "citizen@emergencydispatch.com",
                    PhoneNumber = "0904567890",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Citizen@123456"),
                    Role = UserRole.Citizen,
                    Status = UserStatus.Active,
                    IsEmailVerified = true,
                    BloodType = BloodType.O_Positive,
                    Address = "789 Điện Biên Phủ, Phường 22, Quận Bình Thạnh, TP. Hồ Chí Minh",
                    CitizenIdNumber = "079095012345",
                    MedicalNotes = "Dị ứng thuốc kháng sinh nhóm Penicillin; Tiền sử huyết áp thấp.",
                    EmergencyContactName = "Lê Thị Thân",
                    EmergencyContactPhone = "0911223344",
                    EmergencyContactRelationship = "Vợ",
                    CreatedAt = DateTime.UtcNow
                }
            };

            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();
        }

        // 4. Tài khoản Admin demo cho FE test: chỉ khi chạy Development và chỉ tạo khi chưa có.
        // KHÔNG đặt lại mật khẩu / mở khóa / khôi phục nếu đã tồn tại — trước đây mỗi lần khởi động đều reset
        // về mật khẩu 123123 nên Admin không thể vô hiệu hóa tài khoản này (backdoor).
        if (seedDemoAdmin)
        {
            const string demoAdminEmail = "admin@gmail.com";
            if (!await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == demoAdminEmail))
            {
                await context.Users.AddAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Administrator",
                    Email = demoAdminEmail,
                    PhoneNumber = "0987654321",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("123123"),
                    Role = UserRole.Admin,
                    Status = UserStatus.Active,
                    IsEmailVerified = true,
                    CreatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
        }
    }
}
