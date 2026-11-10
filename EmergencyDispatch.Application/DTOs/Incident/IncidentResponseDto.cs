using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// DTO trả về thông tin đầy đủ của một sự cố khẩn cấp
/// </summary>
public class IncidentResponseDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? GuestCancellationToken { get; set; }

    /// <summary>Loại sự cố người báo tự chọn (nếu có)</summary>
    public Domain.Enums.IncidentEventType? ReportedEventType { get; set; }

    /// <summary>
    /// Loại sự cố chi tiết người báo chọn (mã danh mục IncidentTypes)
    /// </summary>
    public string? ReportedIncidentTypeCode { get; set; }

    /// <summary>
    /// Loại sự cố chi tiết hiện tại (mã danh mục IncidentTypes); eventType là nhóm của loại này
    /// </summary>
    public string? IncidentTypeCode { get; set; }

    /// <summary>
    /// AI tự động điều phối: None / Dispatched (hệ thống đã tự xác minh và điều xe) / Skipped (đủ tin cậy nhưng chuyển Operator)
    /// </summary>
    public Domain.Enums.AutoDispatchState AutoDispatchState { get; set; }

    /// <summary>
    /// Lý do / mô tả kết quả tự điều phối (song ngữ theo Accept-Language)
    /// </summary>
    public string? AutoDispatchNote { get; set; }

    /// <summary>
    /// Nghi trùng với sự cố đã được điều xe (AI tự điều phối phát hiện) → Operator kiểm tra, gộp hoặc điều thêm
    /// </summary>
    public Guid? SuspectedDuplicateOfIncidentId { get; set; }

    /// <summary>(Sự cố gốc) Số báo cáo khác đang gom vào sự cố này: hàng đợi hiện "+N người cùng báo"</summary>
    public int LinkedReportCount { get; set; }

    /// <summary>(Báo cáo được gom) Sự cố gốc</summary>
    public Guid? LinkedToIncidentId { get; set; }

    /// <summary>(Chỉ trả cho người báo khi SOS vừa được gom) Tóm tắt sự cố gốc</summary>
    public LinkedToIncidentDto? LinkedTo { get; set; }

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string LocationAddress { get; set; } = string.Empty;
    public IncidentStatus Status { get; set; }
    public SeverityLevel Severity { get; set; }

    public Guid? ReportedByUserId { get; set; }
    public string? ReporterName { get; set; }
    public string? ReporterPhone { get; set; }

    public Guid? VerifiedByUserId { get; set; }
    public string? VerifiedByUserName { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? OperatorNotes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Khóa mềm 2 phút (FR-OP-02, BR-15): Operator đang xử lý sự cố; null khi chưa ai khóa hoặc khóa đã hết hạn
    /// </summary>
    public Guid? LockedByOperatorId { get; set; }
    public string? LockedByOperatorName { get; set; }
    public DateTime? LockExpiresAt { get; set; }

    /// <summary>
    /// Trạm phụ trách theo vị trí (chỉ Operator trạm này và Admin xử lý); null = mọi Operator
    /// </summary>
    public Guid? ResponsibleStationId { get; set; }
    public string? ResponsibleStationName { get; set; }

    /// <summary>
    /// Vị trí nằm ngoài khu vực phục vụ đã cấu hình (vẫn được tiếp nhận)
    /// </summary>
    public bool IsOutOfServiceArea { get; set; }
    public double? DistanceOutsideAreaKm { get; set; }

    /// <summary>
    /// Loại sự cố đã xác nhận (AI đề xuất hoặc Operator sửa khi verify)
    /// </summary>
    public IncidentEventType EventType { get; set; }

    /// <summary>
    /// CitizenApp = SOS qua ứng dụng; giá trị khác = Operator tự tạo (PhoneCall, Radio, Cctv, FieldObservation, Other)
    /// </summary>
    public IncidentSource Source { get; set; }

    // Thông tin hủy (tách khỏi VerifiedAt / OperatorNotes)
    public CancellationReason? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancellationNote { get; set; }

    public List<IncidentMediaDto> MediaItems { get; set; } = new();
    public AiClassificationResultDto? AiClassification { get; set; }

    /// <summary>
    /// Hồ sơ y tế, thông tin cứu hộ căn hộ và người liên hệ khẩn cấp của người báo (nếu có tài khoản)
    /// </summary>
    public IncidentReporterMedicalDto? ReporterMedicalProfile { get; set; }

}
