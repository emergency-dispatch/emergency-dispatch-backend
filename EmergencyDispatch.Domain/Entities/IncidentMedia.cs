using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Tệp tin hình ảnh hoặc video đính kèm sự cố khẩn cấp
/// </summary>
public class IncidentMedia : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    /// <summary>
    /// Đường dẫn tệp tin (Cloudinary URL hoặc Cloud Storage)
    /// </summary>
    public string MediaUrl { get; set; } = string.Empty;

    /// <summary>
    /// ID định danh trên Cloudinary để phục vụ quản lý, xóa
    /// </summary>
    public string? PublicId { get; set; }

    /// <summary>
    /// Loại media (Photo, Video)
    /// </summary>
    public MediaType MediaType { get; set; } = MediaType.Photo;

    /// <summary>
    /// Kích thước tệp tin (bytes)
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// MIME type thực tế của file (vd: image/jpeg, video/mp4)
    /// </summary>
    public string? MimeType { get; set; }
}
