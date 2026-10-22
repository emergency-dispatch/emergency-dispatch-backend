using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Audit;

/// <summary>
/// Một dòng nhật ký thao tác quản trị (BR-AD-06)
/// </summary>
public class SystemAuditLogDto
{
    public Guid Id { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? ActorRole { get; set; }
    public SystemAuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SystemAuditLogFilterDto
{
    /// <summary>
    /// Station, RescueUnit, User, StationTeam, Settings, EscalationRule, BlockedNumber, Report
    /// </summary>
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? ActorId { get; set; }
    public SystemAuditAction? Action { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Tên loại đối tượng dùng trong nhật ký (tránh gõ sai chuỗi)
/// </summary>
public static class AuditEntityTypes
{
    public const string Station = "Station";
    public const string RescueUnit = "RescueUnit";
    public const string User = "User";
    public const string StationTeam = "StationTeam";
    public const string Settings = "Settings";
    public const string EscalationRule = "EscalationRule";
    public const string BlockedNumber = "BlockedNumber";
    public const string Report = "Report";
}
