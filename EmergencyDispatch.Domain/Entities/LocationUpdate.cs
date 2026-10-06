namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Cập nhật vị trí GPS thời gian thực của nhân viên cứu hộ
/// </summary>
public class LocationUpdate : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }
    public double? Accuracy { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
