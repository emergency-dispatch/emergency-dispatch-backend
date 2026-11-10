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
    public List<UnitRuleDto> UnitRules { get; set; } = new();
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
/// Quy tắc: từ mức độ MinSeverity trở lên cần Quantity xe loại UnitTypeCode; Priority nhỏ = ưu tiên trước
/// </summary>
public class UnitRuleDto
{
    public string UnitTypeCode { get; set; } = string.Empty;
    public string? UnitTypeName { get; set; }
    public SeverityLevel MinSeverity { get; set; } = SeverityLevel.Level1;
    public int Quantity { get; set; } = 1;
    public int Priority { get; set; }
}

public class ReplaceUnitRulesDto
{
    [Required]
    public List<UnitRuleDto> Rules { get; set; } = new();
}
