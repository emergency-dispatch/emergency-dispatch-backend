using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Hình ảnh / video đính kèm báo cáo hoàn thành nhiệm vụ cứu hộ
/// </summary>
public class CompletionReportMedia : BaseEntity
{
    public Guid CompletionReportId { get; set; }
    public CompletionReport? CompletionReport { get; set; }

    public string MediaUrl { get; set; } = string.Empty;
    public string? PublicId { get; set; }
    public MediaType MediaType { get; set; } = MediaType.Photo;
    public long FileSizeBytes { get; set; }
}
