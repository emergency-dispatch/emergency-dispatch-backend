using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Team;

/// <summary>
/// Tổ làm việc của trạm (khớp types/stationTeam.ts của Web)
/// </summary>
public class StationTeamDto
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Các xe tổ phụ trách (một tổ có thể nhiều xe); rỗng = tổ trực tại trạm
    /// </summary>
    public List<Guid> RescueUnitIds { get; set; } = new();
    public List<string> RescueUnitPlates { get; set; } = new();

    /// <summary>
    /// [Cũ — giữ cho FE chưa cập nhật] Xe đầu tiên của tổ; dùng RescueUnitIds
    /// </summary>
    public Guid? RescueUnitId { get; set; }
    public string? RescueUnitPlate { get; set; }

    public List<StationTeamMemberDto> Members { get; set; } = new();

    /// <summary>
    /// Tổ dưới 2 người → cảnh báo thiếu người khi giao xe
    /// </summary>
    public bool IsUnderstaffed { get; set; }
}

public class StationTeamMemberDto
{
    public Guid StaffId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public CrewRole CrewRole { get; set; }
}

/// <summary>
/// Tạo / cập nhật tổ làm việc
/// </summary>
public class StationTeamInputDto
{
    [Required(ErrorMessage = "Vui lòng nhập tên tổ.")]
    [MaxLength(100, ErrorMessage = "Tên tổ tối đa 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Các xe tổ phụ trách (cùng trạm, chưa thuộc tổ khác). null = dùng RescueUnitId cũ
    /// </summary>
    public List<Guid>? RescueUnitIds { get; set; }

    /// <summary>
    /// [Cũ] Một xe duy nhất; chỉ dùng khi không gửi RescueUnitIds
    /// </summary>
    public Guid? RescueUnitId { get; set; }

    public List<StationTeamMemberInputDto> Members { get; set; } = new();

    /// <summary>
    /// Danh sách xe thực tế: ưu tiên RescueUnitIds, nếu không có thì lấy RescueUnitId cũ
    /// </summary>
    public List<Guid> EffectiveRescueUnitIds()
        => (RescueUnitIds ?? (RescueUnitId.HasValue ? new List<Guid> { RescueUnitId.Value } : new List<Guid>()))
            .Distinct()
            .ToList();
}

public class StationTeamMemberInputDto
{
    [Required]
    public Guid StaffId { get; set; }

    public CrewRole CrewRole { get; set; } = CrewRole.Rescuer;
}
