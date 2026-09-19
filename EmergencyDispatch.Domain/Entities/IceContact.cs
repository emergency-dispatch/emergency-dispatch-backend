namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Người liên hệ khẩn cấp (In Case of Emergency - ICE)
/// </summary>
public class IceContact : BaseEntity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;

    // Navigation Property
    public User? User { get; set; }
}
