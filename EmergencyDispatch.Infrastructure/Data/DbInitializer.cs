using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(ApplicationDbContext context)
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

        // 1. Không seed dữ liệu mẫu (trạm, xe, tài khoản demo). Chỉ đảm bảo có tài khoản Admin đầu tiên để đăng nhập DB mới.
        // Tạo khi chưa có (kể cả bản ghi đã xóa mềm); KHÔNG đặt lại mật khẩu / mở khóa / khôi phục nếu đã tồn tại,
        // để Admin vẫn đổi mật khẩu hoặc vô hiệu hóa được tài khoản này.
        const string adminEmail = "admin@gmail.com";
        if (!await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == adminEmail))
        {
            await context.Users.AddAsync(new User
            {
                Id = Guid.NewGuid(),
                FullName = "Administrator",
                Email = adminEmail,
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
