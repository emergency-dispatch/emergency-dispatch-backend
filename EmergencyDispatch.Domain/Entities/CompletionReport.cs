namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Báo cáo nghiệm thu (FR-RS-03, BR-25, BR-26): một thành viên đại diện tổ chụp ảnh + ký,
/// áp cho mọi xe của tổ trong sự cố. Xe chưa gán tổ thì báo cáo riêng cho xe đó (StationTeamId = null).
/// </summary>
public class CompletionReport : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    /// <summary>
    /// Tổ nộp báo cáo; null = báo cáo của một xe chưa gán tổ. Mỗi tổ tối đa 1 báo cáo / sự cố
    /// </summary>
    public Guid? StationTeamId { get; set; }
    public StationTeam? StationTeam { get; set; }

    /// <summary>
    /// Các lượt điều phối (xe) được hoàn tất bởi báo cáo này
    /// </summary>
    public ICollection<DispatchAssignment> Assignments { get; set; } = new List<DispatchAssignment>();

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

    /// <summary>
    /// Ảnh chữ ký xác nhận hoàn tất của thành viên tổ nộp báo cáo (upload qua /api/media/upload).
    /// Bắt buộc với báo cáo mới; null ở báo cáo nộp trước khi có tính năng ký
    /// </summary>
    public string? SignatureUrl { get; set; }

    /// <summary>
    /// Họ tên thành viên ký xác nhận (= người nộp báo cáo) tại thời điểm ký
    /// </summary>
    public string? SignerName { get; set; }

    public DateTime? SignedAt { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompletionReportMedia> MediaItems { get; set; } = new List<CompletionReportMedia>();
}
