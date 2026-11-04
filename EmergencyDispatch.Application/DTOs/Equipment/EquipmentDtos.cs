using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Equipment;

public class EquipmentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EquipmentCategory Category { get; set; }
    public EquipmentStatus Status { get; set; }
    public int Quantity { get; set; }
    public Guid StationId { get; set; }
    public string? StationName { get; set; }

    /// <summary>
    /// Xe đang trang bị thiết bị; null = cất tại trạm
    /// </summary>
    public Guid? RescueUnitId { get; set; }
    public string? RescueUnitPlate { get; set; }

    public string? Notes { get; set; }
    public string? StatusNote { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class EquipmentFilterDto
{
    public Guid? StationId { get; set; }
    public Guid? RescueUnitId { get; set; }
    public EquipmentStatus? Status { get; set; }
    public EquipmentCategory? Category { get; set; }
}

public class CreateEquipmentDto
{
    [Required(ErrorMessage = "Tên thiết bị không được để trống.")]
    [MaxLength(200, ErrorMessage = "Tên thiết bị tối đa 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    public EquipmentCategory Category { get; set; } = EquipmentCategory.Other;

    [Range(1, 1000, ErrorMessage = "Số lượng phải từ 1 đến 1000.")]
    public int Quantity { get; set; } = 1;

    [Required(ErrorMessage = "Vui lòng chọn trạm sở hữu thiết bị.")]
    public Guid StationId { get; set; }

    /// <summary>
    /// Xe trang bị (phải cùng trạm); bỏ trống = cất tại trạm
    /// </summary>
    public Guid? RescueUnitId { get; set; }

    [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Notes { get; set; }
}

public class UpdateEquipmentDto
{
    [Required(ErrorMessage = "Tên thiết bị không được để trống.")]
    [MaxLength(200, ErrorMessage = "Tên thiết bị tối đa 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    public EquipmentCategory Category { get; set; } = EquipmentCategory.Other;

    [Range(1, 1000, ErrorMessage = "Số lượng phải từ 1 đến 1000.")]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Đổi trạm sở hữu: chỉ Admin
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn trạm sở hữu thiết bị.")]
    public Guid StationId { get; set; }

    public Guid? RescueUnitId { get; set; }

    [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Notes { get; set; }
}

public class UpdateEquipmentStatusDto
{
    public EquipmentStatus Status { get; set; }

    /// <summary>
    /// Bắt buộc (≥ 5 ký tự) khi chuyển sang Maintenance: lý do hỏng / bảo trì
    /// </summary>
    [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Note { get; set; }
}
