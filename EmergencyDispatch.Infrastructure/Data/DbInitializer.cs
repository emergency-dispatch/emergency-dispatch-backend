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
    }
}
