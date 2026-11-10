using System.Text.RegularExpressions;
using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Catalog;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Danh mục loại xe, loại sự cố và quy tắc loại xe theo loại sự cố (Admin quản lý).
/// Loại hệ thống (seed sẵn) không xóa được; loại đang được dùng chỉ ẩn được, không xóa.
/// </summary>
public class TypeCatalogService : ITypeCatalogService
{
    public const string UnitTypeNotFoundMessage = "Không tìm thấy loại phương tiện.";
    public const string IncidentTypeNotFoundMessage = "Không tìm thấy loại sự cố.";
    public const string InvalidCodeMessage = "Mã chỉ gồm chữ cái không dấu, số và dấu gạch dưới, bắt đầu bằng chữ cái (tối đa 50 ký tự).";
    public const string CodeTakenMessage = "Mã {0} đã tồn tại.";
    public const string SystemTypeDeleteMessage = "Không thể xóa loại hệ thống. Bạn có thể đổi tên hoặc ẩn.";
    public const string TypeInUseMessage = "Loại {0} đang được sử dụng nên không xóa được. Hãy ẩn (isActive = false) thay vì xóa.";
    public const string InvalidCategoryMessage = "Nhóm loại sự cố chỉ nhận FireExplosion, TrafficAccident hoặc Other.";
    public const string SystemCategoryLockedMessage = "Không đổi được nhóm của loại sự cố hệ thống.";
    public const string RuleUnitTypeMissingMessage = "Loại phương tiện {0} không tồn tại trong danh mục.";
    public const string RuleInvalidMessage = "Quy tắc không hợp lệ: mức độ tối thiểu phải từ Level1 đến Level3, số xe từ 1 đến 5.";
    public const string RuleDuplicateMessage = "Trùng quy tắc: loại xe {0} ở mức độ {1} được khai báo hai lần.";

    private static readonly Regex CodePattern = new("^[A-Za-z][A-Za-z0-9_]{0,49}$", RegexOptions.Compiled);

    private readonly ITypeCatalogRepository _repository;
    private readonly ISystemAuditService _auditService;

    public TypeCatalogService(ITypeCatalogRepository repository, ISystemAuditService auditService)
    {
        _repository = repository;
        _auditService = auditService;
    }

    // ==================== LOẠI PHƯƠNG TIỆN ====================

    public async Task<List<RescueUnitTypeDto>> GetUnitTypesAsync(bool includeInactive, CancellationToken cancellationToken = default)
        => (await _repository.GetUnitTypesAsync(includeInactive, cancellationToken)).Select(MapUnitType).ToList();

    public async Task<ApiResponseDto<RescueUnitTypeDto>> CreateUnitTypeAsync(UpsertRescueUnitTypeDto dto, CancellationToken cancellationToken = default)
    {
        var code = dto.Code?.Trim() ?? string.Empty;
        if (!CodePattern.IsMatch(code))
        {
            return ApiResponseDto<RescueUnitTypeDto>.FailureResult(InvalidCodeMessage);
        }
        if (await _repository.GetUnitTypeAsync(code, cancellationToken) != null)
        {
            return ApiResponseDto<RescueUnitTypeDto>.ConflictResult(LocalizationHelper.Localize(CodeTakenMessage, code));
        }

        var type = new RescueUnitTypeDefinition { Code = code, IsSystem = false };
        Apply(type, dto);
        await _repository.AddUnitTypeAsync(type, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.RescueUnitType, type.Id, type.Code,
            $"Thêm loại phương tiện {type.Code} ({type.NameVi})", cancellationToken: cancellationToken);

        return ApiResponseDto<RescueUnitTypeDto>.SuccessResult(MapUnitType(type), "Đã thêm loại phương tiện.");
    }

    public async Task<ApiResponseDto<RescueUnitTypeDto>> UpdateUnitTypeAsync(Guid id, UpsertRescueUnitTypeDto dto, CancellationToken cancellationToken = default)
    {
        var type = await _repository.GetUnitTypeByIdAsync(id, cancellationToken);
        if (type == null)
        {
            return ApiResponseDto<RescueUnitTypeDto>.NotFoundResult(UnitTypeNotFoundMessage);
        }

        Apply(type, dto);
        type.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.RescueUnitType, type.Id, type.Code,
            $"Sửa loại phương tiện {type.Code} ({type.NameVi}, {(type.IsActive ? "đang dùng" : "đã ẩn")})", cancellationToken: cancellationToken);

        return ApiResponseDto<RescueUnitTypeDto>.SuccessResult(MapUnitType(type), "Đã cập nhật loại phương tiện.");
    }

    public async Task<ApiResponseDto<bool>> DeleteUnitTypeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await _repository.GetUnitTypeByIdAsync(id, cancellationToken);
        if (type == null)
        {
            return ApiResponseDto<bool>.NotFoundResult(UnitTypeNotFoundMessage);
        }
        if (type.IsSystem)
        {
            return ApiResponseDto<bool>.FailureResult(SystemTypeDeleteMessage);
        }
        if (await _repository.IsUnitTypeInUseAsync(type.Code, cancellationToken))
        {
            return ApiResponseDto<bool>.ConflictResult(LocalizationHelper.Localize(TypeInUseMessage, type.Code));
        }

        _repository.RemoveUnitType(type);
        await _repository.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.RescueUnitType, type.Id, type.Code,
            $"Xóa loại phương tiện {type.Code}", cancellationToken: cancellationToken);
        return ApiResponseDto<bool>.SuccessResult(true, "Đã xóa loại phương tiện.");
    }

    // ==================== LOẠI SỰ CỐ ====================

    public async Task<List<IncidentTypeDto>> GetIncidentTypesAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var unitNames = await UnitTypeNamesAsync(cancellationToken);
        return (await _repository.GetIncidentTypesAsync(includeInactive, cancellationToken)).Select(t => MapIncidentType(t, unitNames)).ToList();
    }

    public async Task<ApiResponseDto<IncidentTypeDto>> CreateIncidentTypeAsync(UpsertIncidentTypeDto dto, CancellationToken cancellationToken = default)
    {
        var code = dto.Code?.Trim() ?? string.Empty;
        if (!CodePattern.IsMatch(code))
        {
            return ApiResponseDto<IncidentTypeDto>.FailureResult(InvalidCodeMessage);
        }
        if (!IsValidCategory(dto.Category))
        {
            return ApiResponseDto<IncidentTypeDto>.FailureResult(InvalidCategoryMessage);
        }
        if (await _repository.GetIncidentTypeAsync(code, cancellationToken) != null)
        {
            return ApiResponseDto<IncidentTypeDto>.ConflictResult(LocalizationHelper.Localize(CodeTakenMessage, code));
        }

        var type = new IncidentTypeDefinition { Code = code, IsSystem = false, Category = dto.Category };
        Apply(type, dto);
        await _repository.AddIncidentTypeAsync(type, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.IncidentType, type.Id, type.Code,
            $"Thêm loại sự cố {type.Code} ({type.NameVi}, nhóm {type.Category})", cancellationToken: cancellationToken);

        return ApiResponseDto<IncidentTypeDto>.SuccessResult(MapIncidentType(type, await UnitTypeNamesAsync(cancellationToken)), "Đã thêm loại sự cố.");
    }

    public async Task<ApiResponseDto<IncidentTypeDto>> UpdateIncidentTypeAsync(Guid id, UpsertIncidentTypeDto dto, CancellationToken cancellationToken = default)
    {
        var type = await _repository.GetIncidentTypeByIdAsync(id, cancellationToken);
        if (type == null)
        {
            return ApiResponseDto<IncidentTypeDto>.NotFoundResult(IncidentTypeNotFoundMessage);
        }
        if (!IsValidCategory(dto.Category))
        {
            return ApiResponseDto<IncidentTypeDto>.FailureResult(InvalidCategoryMessage);
        }
        if (type.IsSystem && dto.Category != type.Category)
        {
            return ApiResponseDto<IncidentTypeDto>.FailureResult(SystemCategoryLockedMessage);
        }

        type.Category = dto.Category;
        Apply(type, dto);
        type.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.IncidentType, type.Id, type.Code,
            $"Sửa loại sự cố {type.Code} ({type.NameVi}, nhóm {type.Category}, {(type.IsActive ? "đang dùng" : "đã ẩn")})", cancellationToken: cancellationToken);

        return ApiResponseDto<IncidentTypeDto>.SuccessResult(MapIncidentType(type, await UnitTypeNamesAsync(cancellationToken)), "Đã cập nhật loại sự cố.");
    }

    public async Task<ApiResponseDto<bool>> DeleteIncidentTypeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await _repository.GetIncidentTypeByIdAsync(id, cancellationToken);
        if (type == null)
        {
            return ApiResponseDto<bool>.NotFoundResult(IncidentTypeNotFoundMessage);
        }
        if (type.IsSystem)
        {
            return ApiResponseDto<bool>.FailureResult(SystemTypeDeleteMessage);
        }
        if (await _repository.IsIncidentTypeInUseAsync(type.Code, cancellationToken))
        {
            return ApiResponseDto<bool>.ConflictResult(LocalizationHelper.Localize(TypeInUseMessage, type.Code));
        }

        _repository.RemoveIncidentType(type);
        await _repository.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.IncidentType, type.Id, type.Code,
            $"Xóa loại sự cố {type.Code}", cancellationToken: cancellationToken);
        return ApiResponseDto<bool>.SuccessResult(true, "Đã xóa loại sự cố.");
    }

    public async Task<ApiResponseDto<IncidentTypeDto>> ReplaceUnitRulesAsync(Guid incidentTypeId, ReplaceUnitRulesDto dto, CancellationToken cancellationToken = default)
    {
        var type = await _repository.GetIncidentTypeByIdAsync(incidentTypeId, cancellationToken);
        if (type == null)
        {
            return ApiResponseDto<IncidentTypeDto>.NotFoundResult(IncidentTypeNotFoundMessage);
        }

        var unitNames = await UnitTypeNamesAsync(cancellationToken);
        var rules = dto.Rules ?? new List<UnitRuleDto>();
        var seen = new HashSet<(string, SeverityLevel)>();
        foreach (var rule in rules)
        {
            var unitCode = rule.UnitTypeCode?.Trim() ?? string.Empty;
            if (!unitNames.ContainsKey(unitCode))
            {
                return ApiResponseDto<IncidentTypeDto>.FailureResult(LocalizationHelper.Localize(RuleUnitTypeMissingMessage, unitCode));
            }
            if (rule.MinSeverity is < SeverityLevel.Level1 or > SeverityLevel.Level3 || rule.Quantity is < 1 or > 5)
            {
                return ApiResponseDto<IncidentTypeDto>.FailureResult(RuleInvalidMessage);
            }
            if (!seen.Add((unitCode, rule.MinSeverity)))
            {
                return ApiResponseDto<IncidentTypeDto>.FailureResult(LocalizationHelper.Localize(RuleDuplicateMessage, unitCode, rule.MinSeverity));
            }
        }

        var before = string.Join(", ", type.UnitRules.Select(Describe));
        _repository.RemoveUnitRules(type.UnitRules.ToList());
        type.UnitRules.Clear();
        var created = rules.Select(r => new IncidentTypeUnitRule
        {
            IncidentTypeId = type.Id,
            UnitTypeCode = r.UnitTypeCode.Trim(),
            MinSeverity = r.MinSeverity,
            Quantity = r.Quantity,
            Priority = r.Priority
        }).ToList();
        await _repository.AddUnitRulesAsync(created, cancellationToken);
        // EF tự gắn quy tắc mới vào type.UnitRules khi Add (cùng IncidentTypeId); chỉ thêm tay khi chưa có (VD repository giả trong test)
        foreach (var rule in created.Where(r => !type.UnitRules.Contains(r))) type.UnitRules.Add(rule);
        await _repository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.IncidentType, type.Id, type.Code,
            $"Đổi quy tắc loại xe của {type.Code}: [{before}] → [{string.Join(", ", created.Select(Describe))}]", cancellationToken: cancellationToken);

        return ApiResponseDto<IncidentTypeDto>.SuccessResult(MapIncidentType(type, unitNames), "Đã lưu quy tắc loại xe.");
    }

    // ==================== HỖ TRỢ ====================

    private static bool IsValidCategory(IncidentEventType category)
        => category is IncidentEventType.FireExplosion or IncidentEventType.TrafficAccident or IncidentEventType.Other;

    private static string Describe(IncidentTypeUnitRule r) => $"{r.UnitTypeCode}×{r.Quantity}≥{r.MinSeverity}";

    private async Task<Dictionary<string, RescueUnitTypeDefinition>> UnitTypeNamesAsync(CancellationToken cancellationToken)
        => (await _repository.GetUnitTypesAsync(includeInactive: true, cancellationToken)).ToDictionary(t => t.Code, StringComparer.Ordinal);

    private static void Apply(RescueUnitTypeDefinition type, UpsertRescueUnitTypeDto dto)
    {
        type.NameVi = dto.NameVi.Trim();
        type.NameEn = dto.NameEn.Trim();
        type.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        type.Icon = string.IsNullOrWhiteSpace(dto.Icon) ? null : dto.Icon.Trim();
        type.Color = string.IsNullOrWhiteSpace(dto.Color) ? null : dto.Color.Trim();
        type.IsActive = dto.IsActive;
        type.DisplayOrder = dto.DisplayOrder;
    }

    private static void Apply(IncidentTypeDefinition type, UpsertIncidentTypeDto dto)
    {
        type.NameVi = dto.NameVi.Trim();
        type.NameEn = dto.NameEn.Trim();
        type.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        type.Icon = string.IsNullOrWhiteSpace(dto.Icon) ? null : dto.Icon.Trim();
        type.Color = string.IsNullOrWhiteSpace(dto.Color) ? null : dto.Color.Trim();
        type.IsActive = dto.IsActive;
        type.DisplayOrder = dto.DisplayOrder;
    }

    public static RescueUnitTypeDto MapUnitType(RescueUnitTypeDefinition t) => new()
    {
        Id = t.Id,
        Code = t.Code,
        NameVi = t.NameVi,
        NameEn = t.NameEn,
        Name = LocalizationHelper.IsEnglish ? t.NameEn : t.NameVi,
        Description = t.Description,
        Icon = t.Icon,
        Color = t.Color,
        IsSystem = t.IsSystem,
        IsActive = t.IsActive,
        DisplayOrder = t.DisplayOrder
    };

    private static IncidentTypeDto MapIncidentType(IncidentTypeDefinition t, IReadOnlyDictionary<string, RescueUnitTypeDefinition> unitTypes) => new()
    {
        Id = t.Id,
        Code = t.Code,
        NameVi = t.NameVi,
        NameEn = t.NameEn,
        Name = LocalizationHelper.IsEnglish ? t.NameEn : t.NameVi,
        Description = t.Description,
        Icon = t.Icon,
        Color = t.Color,
        Category = t.Category,
        AiRecognizable = t.Category is IncidentEventType.FireExplosion or IncidentEventType.TrafficAccident,
        IsSystem = t.IsSystem,
        IsActive = t.IsActive,
        DisplayOrder = t.DisplayOrder,
        UnitRules = t.UnitRules
            .OrderBy(r => r.Priority).ThenBy(r => r.MinSeverity)
            .Select(r => new UnitRuleDto
            {
                UnitTypeCode = r.UnitTypeCode,
                UnitTypeName = unitTypes.TryGetValue(r.UnitTypeCode, out var u) ? (LocalizationHelper.IsEnglish ? u.NameEn : u.NameVi) : r.UnitTypeCode,
                MinSeverity = r.MinSeverity,
                Quantity = r.Quantity,
                Priority = r.Priority
            }).ToList()
    };
}
