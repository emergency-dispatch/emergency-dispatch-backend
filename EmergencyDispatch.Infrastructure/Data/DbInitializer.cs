using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
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

        // 2. Seed Tài khoản mẫu (Admin, Operator, RescueStaff, Citizen)
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
                    CreatedAt = DateTime.UtcNow
                }
            };

            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();
        }
    }
}
