using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Phương tiện cứu hộ cơ động thuộc trạm
/// </summary>
public class Vehicle : BaseEntity
{
    public Guid StationId { get; set; }
    public Station? Station { get; set; }

    public string LicensePlate { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public VehicleType VehicleType { get; set; } = VehicleType.Other;
    public VehicleStatus Status { get; set; } = VehicleStatus.Available;
    public int Capacity { get; set; } = 4;
    public string? Description { get; set; }

    public double? LastKnownLatitude { get; set; }
    public double? LastKnownLongitude { get; set; }

    // Navigation properties
    public ICollection<Equipment> Equipments { get; set; } = new List<Equipment>();
    public ICollection<IncidentAssignment> IncidentAssignments { get; set; } = new List<IncidentAssignment>();
}
