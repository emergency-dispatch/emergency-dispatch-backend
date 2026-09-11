using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.RescueUnit;

public class RescueUnitResponseDto
{
    public Guid Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public RescueUnitType UnitType { get; set; }
    public string UnitTypeName => UnitType.ToString();
    public RescueUnitStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public double CurrentLat { get; set; }
    public double CurrentLng { get; set; }
    public DateTime? LastLocationUpdateAt { get; set; }
    public Guid StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
