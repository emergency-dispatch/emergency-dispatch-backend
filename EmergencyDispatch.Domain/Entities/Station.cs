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

    /// <summary>
    /// Operator phụ trách trạm (các Operator ngang cấp, mỗi người 1 trạm; tên trường giữ HeadOperatorId để tương thích FE)
    /// </summary>
    public Guid? HeadOperatorId { get; set; }
    public User? HeadOperator { get; set; }

    // Navigation properties
    public ICollection<User> StaffMembers { get; set; } = new List<User>();
    public ICollection<RescueUnit> RescueUnits { get; set; } = new List<RescueUnit>();
}
