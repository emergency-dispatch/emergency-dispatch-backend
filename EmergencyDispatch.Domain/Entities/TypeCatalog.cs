using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Danh mục loại phương tiện cứu hộ (bảng RescueUnitTypes). Admin thêm / sửa / ẩn.
/// Code là khóa nghiệp vụ: RescueUnit.UnitType và FieldRequest.RequestedUnitType lưu Code, không đổi được sau khi tạo.
/// </summary>
public class RescueUnitTypeDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameVi { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }

    /// <summary>
    /// Loại hệ thống (6 loại gốc): không xóa được, không đổi Code
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Ẩn khỏi danh sách chọn khi tạo xe mới; xe đã dùng loại này giữ nguyên
    /// </summary>
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Danh mục loại sự cố (bảng IncidentTypes). Admin thêm / sửa / ẩn.
/// Category = nhóm mà AI và luật nghiệp vụ hiểu: FireExplosion / TrafficAccident (AI nhận diện được) hoặc Other.
/// </summary>
public class IncidentTypeDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameVi { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }

    /// <summary>
    /// Nhóm AI: FireExplosion / TrafficAccident = AI nhận diện được (được xét tự điều phối); Other = Operator thẩm định
    /// </summary>
    public IncidentEventType Category { get; set; } = IncidentEventType.Other;

    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<IncidentTypeUnitRule> UnitRules { get; set; } = new List<IncidentTypeUnitRule>();
}

/// <summary>
/// Kế hoạch điều xe của một loại sự cố (bảng IncidentTypeUnitRules), thay luật viết cứng BR-13 / BR-14:
/// ở mức độ Severity cần Quantity xe loại UnitTypeCode; Priority nhỏ = điều trước khi thiếu xe.
/// Tổng Quantity của một mức = số xe chuẩn của mức đó (tối đa 5)
/// </summary>
public class IncidentTypeUnitRule : BaseEntity
{
    public Guid IncidentTypeId { get; set; }
    public IncidentTypeDefinition? IncidentType { get; set; }

    public string UnitTypeCode { get; set; } = string.Empty;
    public SeverityLevel Severity { get; set; } = SeverityLevel.Level1;
    public int Quantity { get; set; } = 1;
    public int Priority { get; set; }
}
