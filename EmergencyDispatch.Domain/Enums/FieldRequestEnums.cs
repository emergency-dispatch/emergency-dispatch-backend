namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Loại yêu cầu đội cứu hộ gửi về trung tâm điều phối từ hiện trường
/// </summary>
public enum FieldRequestType
{
    /// <summary>Cần thêm xe / lực lượng chi viện (kèm loại xe, số lượng)</summary>
    Backup = 0,

    /// <summary>Vấn đề khác (bắt buộc mô tả)</summary>
    Other = 1
}

/// <summary>
/// Vòng đời yêu cầu từ hiện trường: Open → Acknowledged → Fulfilled / Declined; staff có thể tự hủy khi còn Open / Acknowledged
/// </summary>
public enum FieldRequestStatus
{
    Open = 0,
    Acknowledged = 1,
    Fulfilled = 2,
    Declined = 3,
    Cancelled = 4
}
