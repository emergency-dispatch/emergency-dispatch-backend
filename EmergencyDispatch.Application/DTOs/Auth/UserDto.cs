using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Auth;

public class UserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; }
    public Guid? StationId { get; set; }
    public string? StationName { get; set; }
    public bool IsEmailVerified { get; set; }
    public BloodType? BloodType { get; set; }
}
