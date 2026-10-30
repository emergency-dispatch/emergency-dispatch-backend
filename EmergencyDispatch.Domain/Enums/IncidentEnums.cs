namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Trạng thái của sự cố khẩn cấp trong toàn bộ vòng đời điều phối
/// </summary>
public enum IncidentStatus
{
    /// <summary>
    /// Vừa được tạo, đang chờ xử lý
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Đang được AI phân tích hình ảnh/video
    /// </summary>
    AiProcessing = 1,

    /// <summary>
    /// AI đã phân tích xong, chờ Operator xác minh
    /// </summary>
    AiProcessed = 2,

    /// <summary>
    /// Operator đã xác minh sự cố (Human-in-the-loop)
    /// </summary>
    Verified = 3,

    /// <summary>
    /// Đã phân công đội cứu hộ tiếp nhận
    /// </summary>
    Dispatched = 4,

    /// <summary>
    /// Đội cứu hộ đang trên đường hoặc đang xử lý tại hiện trường
    /// </summary>
    InProgress = 5,

    /// <summary>
    /// Sự cố đã xử lý xong và hoàn tất báo cáo
    /// </summary>
    Completed = 6,

    /// <summary>
    /// Sự cố bị hủy (báo khống, trùng lặp)
    /// </summary>
    Cancelled = 7,

    /// <summary>
    /// Sự cố nghiêm trọng bị leo thang lên cấp cao hơn
    /// </summary>
    Escalated = 8
}

/// <summary>
/// Mức độ nghiêm trọng của sự cố (Chuẩn hóa dự án SEP490: 3 Cấp độ + Reject / Level 0)
/// </summary>
public enum SeverityLevel
{
    /// <summary>
    /// Mức 0: Bác bỏ / Chưa phân loại (Reject - Báo khống, diễn tập, nấu nướng, không phải sự cố khẩn cấp)
    /// </summary>
    Unclassified = 0,

    /// <summary>
    /// Mức 1: Nhẹ / Mới bùng phát (Cháy rác, chập điện nhỏ, va chạm giao thông nhẹ, xử lý nhanh tại chỗ)
    /// </summary>
    Level1 = 1,

    /// <summary>
    /// Mức 2: Trung bình / Cục bộ (Cháy 1 xe hoặc 1 phòng/căn hộ, tai nạn có người bị thương, cần 1-2 xe chữa cháy/cấp cứu khu vực)
    /// </summary>
    Level2 = 2,

    /// <summary>
    /// Mức 3: Khẩn cấp / Thảm họa cao nhất (Cháy lớn toàn tòa nhà, cháy nổ nhà xưởng, sập công trình, nhiều nạn nhân - Tự động leo thang sau 5 phút nếu chưa tiếp nhận)
    /// </summary>
    Level3 = 3
}

/// <summary>
/// Nhánh sự kiện do AI nhận diện (FR-SYS-01)
/// </summary>
public enum IncidentEventType
{
    /// <summary>
    /// Không xác định / không phải cháy nổ hay tai nạn
    /// </summary>
    None = 0,

    /// <summary>
    /// Cháy nổ
    /// </summary>
    FireExplosion = 1,

    /// <summary>
    /// Tai nạn giao thông
    /// </summary>
    TrafficAccident = 2
}

/// <summary>
/// Kênh tiếp nhận sự cố. CitizenApp = người dân gửi SOS qua ứng dụng;
/// các giá trị còn lại = Operator tự tạo sự cố khi biết tin từ bên ngoài (chưa có ai báo qua app)
/// </summary>
public enum IncidentSource
{
    CitizenApp = 0,
    PhoneCall = 1,        // Cuộc gọi tới tổng đài / số trực
    Radio = 2,            // Bộ đàm của lực lượng tuần tra, bảo vệ
    Cctv = 3,             // Camera giám sát
    FieldObservation = 4, // Nhân sự trực tiếp nhìn thấy
    Other = 5
}

/// <summary>
/// Lý do hủy sự cố
/// </summary>
public enum CancellationReason
{
    FalseReport = 0,      // Báo giả / khống
    Duplicate = 1,        // Trùng lặp sự cố khác
    TestReport = 2,       // Báo cáo thử nghiệm
    CitizenCancelled = 3, // Citizen tự hủy
    AutoExpired = 4,      // Hết hạn 24h
    Other = 5             // Lý do khác
}

/// <summary>
/// Hành động ghi vết kiểm toán sự cố (Audit Trail)
/// </summary>
public enum AuditAction
{
    Created = 0,
    AiAnalyzed = 1,
    Verified = 2,
    SeverityOverridden = 3,
    Dispatched = 4,
    AssignmentAccepted = 5,
    AssignmentRejected = 6,
    StatusChanged = 7,
    Escalated = 8,
    DeEscalated = 9,
    Cancelled = 10,
    Completed = 11,
    Closed = 12,
    ReDispatched = 13,
    PhoneRevealed = 14,
    ReporterBlocked = 15,
    TransferredStation = 16,
    OutOfServiceArea = 17,
    CreatedByOperator = 18
}
