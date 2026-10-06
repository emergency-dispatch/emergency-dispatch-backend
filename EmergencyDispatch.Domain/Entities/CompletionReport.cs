namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Báo cáo hoàn thành nhiệm vụ cứu hộ (kèm bằng chứng hình ảnh và mã bàn giao QR)
/// </summary>
public class CompletionReport : BaseEntity
{
    public Guid AssignmentId { get; set; }
    public IncidentAssignment? Assignment { get; set; }

    public string Notes { get; set; } = string.Empty;
    public int VictimCount { get; set; } = 0;
    public string? EquipmentUsed { get; set; }

    public string? QrCode { get; set; }
    public bool QrHandoverConfirmed { get; set; } = false;

    public Guid? QrScannedByUserId { get; set; }
    public User? QrScannedByUser { get; set; }
    public DateTime? QrScannedAt { get; set; }
    public double? QrScannedLatitude { get; set; }
    public double? QrScannedLongitude { get; set; }

    // Navigation properties
    public ICollection<CompletionReportMedia> MediaItems { get; set; } = new List<CompletionReportMedia>();
}
