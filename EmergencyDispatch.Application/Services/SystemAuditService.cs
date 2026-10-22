using System.Text.Json;
using System.Text.Json.Serialization;
using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public class SystemAuditService : ISystemAuditService
{
    private const int MaxPageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private readonly ISystemAuditLogRepository _repository;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<SystemAuditService> _logger;

    public SystemAuditService(ISystemAuditLogRepository repository, ICurrentUserContext currentUser, ILogger<SystemAuditService> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task LogAsync(
        SystemAuditAction action,
        string entityType,
        Guid? entityId,
        string? entityName,
        string description,
        object? oldValue = null,
        object? newValue = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _repository.AddAsync(new SystemAuditLog
            {
                ActorId = _currentUser.UserId,
                ActorName = _currentUser.FullName ?? "System",
                ActorRole = _currentUser.Role?.ToString() ?? "System",
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                EntityName = entityName,
                Description = description.Length > 1000 ? description[..1000] : description,
                OldValueJson = oldValue == null ? null : JsonSerializer.Serialize(oldValue, JsonOptions),
                NewValueJson = newValue == null ? null : JsonSerializer.Serialize(newValue, JsonOptions),
                IpAddress = _currentUser.IpAddress,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không ghi được nhật ký quản trị {Action} {EntityType} {EntityId}", action, entityType, entityId);
        }
    }

    public async Task<PaginatedResultDto<SystemAuditLogDto>> GetPagedAsync(SystemAuditLogFilterDto filter, CancellationToken cancellationToken = default)
    {
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
        {
            throw new ArgumentException("Thời gian bắt đầu phải trước thời gian kết thúc.");
        }

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var (items, total) = await _repository.GetPagedAsync(
            filter.EntityType, filter.EntityId, filter.ActorId, filter.Action, filter.From, filter.To,
            pageIndex, pageSize, cancellationToken);

        var dtos = items.Select(l => new SystemAuditLogDto
        {
            Id = l.Id,
            ActorId = l.ActorId,
            ActorName = l.ActorName,
            ActorRole = l.ActorRole,
            Action = l.Action,
            EntityType = l.EntityType,
            EntityId = l.EntityId,
            EntityName = l.EntityName,
            Description = l.Description,
            OldValueJson = l.OldValueJson,
            NewValueJson = l.NewValueJson,
            IpAddress = l.IpAddress,
            Timestamp = l.Timestamp
        }).ToList();

        return new PaginatedResultDto<SystemAuditLogDto>(dtos, total, pageIndex, pageSize);
    }
}
