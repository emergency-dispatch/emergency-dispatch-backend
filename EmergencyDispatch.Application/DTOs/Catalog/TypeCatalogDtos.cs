using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Catalog;

/// <summary>
/// Loại phương tiện cứu hộ (danh mục Admin quản lý). Code là giá trị lưu ở RescueUnit.unitType
/// </summary>
public class RescueUnitTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameVi { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// Tên theo ngôn ngữ của request (Accept-Language)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class UpsertRescueUnitTypeDto
{
    /// <summary>
    /// Mã định danh (chữ cái đầu, chỉ A-Z, 0-9, _). Chỉ nhận khi tạo mới; không đổi được sau đó
    /// </summary>
    [MaxLength(50)]
    public string? Code { get; set; }

    [Required, MaxLength(100)]
    public string NameVi { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string NameEn { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Loại sự cố (danh mục Admin quản lý). Code là giá trị lưu ở Incident.incidentTypeCode
/// </summary>
public class IncidentTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameVi { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }

    /// <summary>
    /// Nhóm AI: FireExplosion / TrafficAccident (AI nhận diện được, được xét tự điều phối) hoặc Other (Operator thẩm định)
    /// </summary>
    public IncidentEventType Category { get; set; }

    /// <summary>
    /// true khi AI nhận diện được nhóm này (Category khác Other)
    /// </summary>
    public bool AiRecognizable { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Kế hoạch điều xe theo mức độ: luôn đủ Level1, Level2, Level3 (mức chưa cấu hình có units rỗng, totalUnits = 0)
    /// </summary>
    public List<DispatchPlanLevelDto> DispatchPlan { get; set; } = new();
}

public class UpsertIncidentTypeDto
{
    /// <summary>
    /// Mã định danh (chữ cái đầu, chỉ A-Z, 0-9, _). Chỉ nhận khi tạo mới
    /// </summary>
    [MaxLength(50)]
    public string? Code { get; set; }

    [Required, MaxLength(100)]
    public string NameVi { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string NameEn { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    /// <summary>
    /// Nhóm AI: FireExplosion, TrafficAccident hoặc Other (mặc định). Không đổi được với loại hệ thống
    /// </summary>
    public IncidentEventType Category { get; set; } = IncidentEventType.Other;

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Kế hoạch điều xe của một mức độ: các loại xe và số lượng cần điều (tổng tối đa 5 xe, BR-14)
/// </summary>
public class DispatchPlanLevelDto
{
    /// <summary>Level1 / Level2 / Level3</summary>
    public SeverityLevel Severity { get; set; }

    /// <summary>Tổng số xe của mức (chỉ đọc, tính từ units)</summary>
    public int TotalUnits { get; set; }

    public List<DispatchPlanUnitDto> Units { get; set; } = new();
}

public class DispatchPlanUnitDto
{
    /// <summary>Mã loại xe trong danh mục (GET /api/rescue-unit-types)</summary>
    public string UnitTypeCode { get; set; } = string.Empty;

    /// <summary>Tên loại xe (chỉ đọc, theo Accept-Language)</summary>
    public string? UnitTypeName { get; set; }

    /// <summary>Số xe loại này (1–5)</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Thứ tự điều khi thiếu xe (nhỏ = trước); bỏ trống thì theo thứ tự trong danh sách</summary>
    public int? Priority { get; set; }
}

/// <summary>
/// Thay toàn bộ kế hoạch điều xe của loại sự cố. Mức không gửi hoặc units rỗng = bỏ cấu hình mức đó
/// (dùng cấu hình mức thấp hơn gần nhất)
/// </summary>
public class UpdateDispatchPlanDto
{
    [Required]
    public List<DispatchPlanLevelDto> Levels { get; set; } = new();
}
