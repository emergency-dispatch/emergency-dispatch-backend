namespace EmergencyDispatch.Application.DTOs.User;

public class IceContactDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
