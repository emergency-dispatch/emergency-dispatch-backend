namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Báo cáo nghiệm thu hoàn thành nhiệm vụ của một xe cứu hộ (FR-RS-03, BR-25, BR-26)
/// </summary>
public class CompletionReport : BaseEntity
{
    public Guid AssignmentId { get; set; }
    public DispatchAssignment? Assignment { get; set; }

    /// <summary>
    /// Người nộp báo cáo (tổ trưởng / nhân viên cứu hộ)
    /// </summary>
    public Guid? SubmittedByUserId { get; set; }
    public User? SubmittedByUser { get; set; }

    /// <summary>
    /// Tóm tắt các hành động xử lý tại hiện trường (10–2000 ký tự)
    /// </summary>
    public string ActionsSummary { get; set; } = string.Empty;

    /// <summary>
    /// Số nạn nhân được cứu / sơ cứu (≥ 0)
    /// </summary>
    public int VictimsAssistedCount { get; set; }

    /// <summary>
    /// Danh sách vật tư, thiết bị đã sử dụng
    /// </summary>
    public List<string> EquipmentUsed { get; set; } = new();

    // BR-26: Bàn giao liên ngành (bệnh viện, công an, PCCC)
    public string? HandoverEntity { get; set; }
    public string? HandoverContactName { get; set; }
    public string? HandoverContactPhone { get; set; }
    public string? HandoverNotes { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompletionReportMedia> MediaItems { get; set; } = new List<CompletionReportMedia>();
}
