using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// DTO gửi báo cáo sự cố khẩn cấp (SOS Report) từ người dân hoặc hệ thống
/// </summary>
public class CreateIncidentDto
{
    /// <summary>
    /// Tiêu đề sự cố (ví dụ: "Tai nạn giao thông", "Hỏa hoạn nhà cao tầng")
    /// </summary>
    [MaxLength(250, ErrorMessage = "Tiêu đề không được vượt quá 250 ký tự")]
    public string? Title { get; set; }

    /// <summary>
    /// Mô tả chi tiết sự việc
    /// </summary>
    [MaxLength(2000, ErrorMessage = "Mô tả không được vượt quá 2000 ký tự")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Vĩ độ GPS hiện trường (-90 đến 90)
    /// </summary>
    [Required(ErrorMessage = "Vĩ độ (Latitude) là bắt buộc")]
    [Range(-90.0, 90.0, ErrorMessage = "Vĩ độ không hợp lệ")]
    public double Latitude { get; set; }

    /// <summary>
    /// Kinh độ GPS hiện trường (-180 đến 180)
    /// </summary>
    [Required(ErrorMessage = "Kinh độ (Longitude) là bắt buộc")]
    [Range(-180.0, 180.0, ErrorMessage = "Kinh độ không hợp lệ")]
    public double Longitude { get; set; }

    /// <summary>
    /// Địa chỉ bằng chữ của hiện trường
    /// </summary>
    [Required(ErrorMessage = "Địa chỉ hiện trường là bắt buộc")]
    [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự")]
    public string LocationAddress { get; set; } = string.Empty;

    /// <summary>
    /// Họ và tên người báo cáo (nếu không đăng nhập)
    /// </summary>
    [MaxLength(100, ErrorMessage = "Tên người báo cáo không được vượt quá 100 ký tự")]
    public string? ReporterName { get; set; }

    /// <summary>
    /// Số điện thoại liên lạc khẩn cấp
    /// </summary>
    [MaxLength(20, ErrorMessage = "Số điện thoại không hợp lệ")]
    public string? ReporterPhone { get; set; }

    /// <summary>
    /// Danh sách URL hình ảnh hoặc video hiện trường đã upload trước (tùy chọn)
    /// </summary>
    public List<string> MediaUrls { get; set; } = new();
}
