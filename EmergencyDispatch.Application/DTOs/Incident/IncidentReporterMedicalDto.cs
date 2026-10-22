namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// DTO chứa thông tin y tế khẩn cấp, đặc thù căn hộ và người liên hệ khẩn cấp của người báo cáo
/// Phục vụ kíp cấp cứu 115 và lực lượng cứu hộ 114 tiếp cận hiện trường
/// </summary>
public class IncidentReporterMedicalDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    
    // Medical ID
    public string? BloodType { get; set; }
    public string? MedicalNotes { get; set; }
    public List<string> ChronicConditions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();
    public List<string> CurrentMedications { get; set; } = new();
    public List<string> MobilityLimitations { get; set; } = new();

    // Special Info cứu nạn
    public string? PreferredLanguage { get; set; }
    public string? DependentsNote { get; set; }
    public int HouseholdMembersCount { get; set; }

    // Một người liên hệ khẩn cấp từ hồ sơ cá nhân
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
}
