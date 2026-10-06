namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Đánh giá và phản hồi của người dân về công tác cứu hộ sự cố
/// </summary>
public class CitizenFeedback : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public Guid CitizenUserId { get; set; }
    public User? CitizenUser { get; set; }

    public int Rating { get; set; } = 5;
    public string? Comment { get; set; }
}
