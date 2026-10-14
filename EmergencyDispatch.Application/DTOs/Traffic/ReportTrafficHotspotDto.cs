using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Traffic;

/// <summary>
/// Yêu cầu báo cáo điểm nghẽn giao thông / chướng ngại vật nội khu
/// </summary>
public class ReportTrafficHotspotDto
{
    [Required(ErrorMessage = "Tiêu đề điểm nghẽn là bắt buộc.")]
    [MaxLength(200, ErrorMessage = "Tiêu đề không được vượt quá 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Vĩ độ (Latitude) là bắt buộc.")]
    [Range(-90.0, 90.0, ErrorMessage = "Vĩ độ không hợp lệ.")]
    public double Latitude { get; set; }

    [Required(ErrorMessage = "Kinh độ (Longitude) là bắt buộc.")]
    [Range(-180.0, 180.0, ErrorMessage = "Kinh độ không hợp lệ.")]
    public double Longitude { get; set; }

    [Required(ErrorMessage = "Địa chỉ/tên đường là bắt buộc.")]
    [MaxLength(300, ErrorMessage = "Địa chỉ không được vượt quá 300 ký tự.")]
    public string LocationAddress { get; set; } = string.Empty;

    /// <summary>
    /// Mức độ kẹt xe: Low (0), Moderate (1), Severe (2), Blocked (3)
    /// </summary>
    public TrafficCongestionLevel CongestionLevel { get; set; } = TrafficCongestionLevel.Moderate;

    /// <summary>
    /// URL ảnh chụp hiện trường
    /// </summary>
    [MaxLength(500, ErrorMessage = "URL media không được vượt quá 500 ký tự.")]
    public string? MediaUrl { get; set; }

    [MaxLength(100, ErrorMessage = "Tên người báo không được vượt quá 100 ký tự.")]
    public string? ReporterName { get; set; }

    [MaxLength(30, ErrorMessage = "Số điện thoại không được vượt quá 30 ký tự.")]
    public string? ReporterPhone { get; set; }

    /// <summary>
    /// Thời gian tồn tại ước tính theo phút (TTL: mặc định 45 phút, tối đa 180 phút)
    /// </summary>
    [Range(10, 180, ErrorMessage = "Thời hạn hiển thị (TTL) phải từ 10 đến 180 phút.")]
    public int TtlMinutes { get; set; } = 45;
}
