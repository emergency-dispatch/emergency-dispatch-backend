using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Báo cáo nghiệm thu hoàn thành nhiệm vụ của một xe (FR-RS-03, BR-25, BR-26)
/// </summary>
public class ResolveDispatchDto
{
    /// <summary>
    /// Tóm tắt các hành động xử lý tại hiện trường
    /// </summary>
    [Required(ErrorMessage = "Báo cáo tóm tắt xử lý hiện trường là bắt buộc.")]
    [MinLength(10, ErrorMessage = "Tóm tắt xử lý phải có ít nhất 10 ký tự.")]
    [MaxLength(2000, ErrorMessage = "Tóm tắt xử lý không được vượt quá 2000 ký tự.")]
    public string ResolutionSummary { get; set; } = string.Empty;

    /// <summary>
    /// Ảnh hiện trường sau xử lý (đã upload qua /api/media/upload) — bắt buộc ít nhất 1 ảnh (MSG42)
    /// </summary>
    [Required(ErrorMessage = "Cần ít nhất một ảnh hiện trường sau xử lý.")]
    [MinLength(1, ErrorMessage = "Cần ít nhất một ảnh hiện trường sau xử lý.")]
    public List<string> EvidenceMediaUrls { get; set; } = new();

    /// <summary>
    /// Số nạn nhân được cứu / sơ cứu
    /// </summary>
    [Required(ErrorMessage = "Số nạn nhân được hỗ trợ là bắt buộc.")]
    [Range(0, 10000, ErrorMessage = "Số nạn nhân phải từ 0 trở lên.")]
    public int? VictimsAssistedCount { get; set; }

    /// <summary>
    /// Vật tư, thiết bị đã sử dụng (tùy chọn)
    /// </summary>
    public List<string> EquipmentUsed { get; set; } = new();

    // BR-26: Bàn giao liên ngành (bệnh viện tiếp nhận, công an, PCCC)
    [MaxLength(300)]
    public string? HandoverEntity { get; set; }

    [MaxLength(200)]
    public string? HandoverContactName { get; set; }

    [MaxLength(30)]
    public string? HandoverContactPhone { get; set; }

    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? HandoverNotes { get; set; }
}
