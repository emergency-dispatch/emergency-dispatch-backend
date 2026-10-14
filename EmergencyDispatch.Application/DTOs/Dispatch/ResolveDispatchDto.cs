using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Báo cáo nghiệm thu hoàn thành sự cố của đội cứu hộ
/// </summary>
public class ResolveDispatchDto
{
    [Required(ErrorMessage = "Báo cáo tóm tắt xử lý hiện trường là bắt buộc.")]
    [MinLength(10, ErrorMessage = "Tóm tắt xử lý phải có ít nhất 10 ký tự.")]
    [MaxLength(2000, ErrorMessage = "Tóm tắt xử lý không được vượt quá 2000 ký tự.")]
    public string ResolutionSummary { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách URL hình ảnh nghiệm thu hiện trường
    /// </summary>
    public List<string> EvidenceMediaUrls { get; set; } = new();

    /// <summary>
    /// Ghi chú bàn giao hoặc các vật tư đã sử dụng
    /// </summary>
    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? HandoverNotes { get; set; }
}
