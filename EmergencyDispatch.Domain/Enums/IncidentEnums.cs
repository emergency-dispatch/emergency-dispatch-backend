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
/// Mức độ nghiêm trọng của sự cố (AI chấm điểm hoặc Operator điều chỉnh)
/// </summary>
public enum SeverityLevel
{
    /// <summary>
    /// Mức 0: Chưa phân loại (Dành cho trường hợp AI lỗi/timeout hoặc cần đánh giá thủ công)
    /// </summary>
    Unclassified = 0,

    /// <summary>
    /// Mức 1: Rất thấp (Không nguy hiểm tính mạng, sự cố nhỏ)
    /// </summary>
    Level1 = 1,

    /// <summary>
    /// Mức 2: Thấp (Cần hỗ trợ thông thường)
    /// </summary>
    Level2 = 2,

    /// <summary>
    /// Mức 3: Trung bình (Nguy cơ lan rộng hoặc có người bị thương nhẹ)
    /// </summary>
    Level3 = 3,

    /// <summary>
    /// Mức 4: Cao (Nguy hiểm tính mạng, đám cháy lớn, tai nạn nghiêm trọng)
    /// </summary>
    Level4 = 4,

    /// <summary>
    /// Mức 5: Cực độ khẩn cấp (Thảm họa, thương vong hàng loạt, sập công trình)
    /// </summary>
    Level5 = 5
}
