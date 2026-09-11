namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Trạm cứu hộ / cơ sở cứu hộ thường trực
/// </summary>
public class Station : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public double CoverageRadiusKm { get; set; } = 10;
    public string? OperatingHours { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<User> StaffMembers { get; set; } = new List<User>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}
