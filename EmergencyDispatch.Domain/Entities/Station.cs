namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Trạm cứu hộ / cơ sở cứu hộ
/// </summary>
public class Station : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<User> StaffMembers { get; set; } = new List<User>();
}
