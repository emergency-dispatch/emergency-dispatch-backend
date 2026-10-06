namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Trang thiết bị cứu hộ được trang bị trên phương tiện
/// </summary>
public class Equipment : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Quantity { get; set; } = 1;
    public string Condition { get; set; } = "Good";
    public DateTime? LastInspectedAt { get; set; }
}
