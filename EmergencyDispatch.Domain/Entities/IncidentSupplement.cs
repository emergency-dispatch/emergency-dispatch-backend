namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Thông tin người báo bổ sung sau khi gửi SOS (ghi chú, ảnh mới). Chỉ thêm, không sửa / xóa nội dung cũ.
/// </summary>
public class IncidentSupplement : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    /// <summary>
    /// Người bổ sung (null nếu khách vãng lai dùng mã hủy / theo dõi SOS)
    /// </summary>
    public Guid? ReporterUserId { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Ảnh / video bổ sung (URL do POST /api/media/upload trả về)
    /// </summary>
    public List<string> MediaUrls { get; set; } = new();
}
