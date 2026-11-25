using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Domain.Models;

namespace EmergencyDispatch.Application.Services;

public static class CitizenMapMessages
{
    public const string Forbidden = "Chỉ tài khoản người dân đang hoạt động được xem bản đồ sự cố.";
    public const string InvalidBounds = "Phải cung cấp đủ south, west, north, east hợp lệ; south không vượt quá north.";
    public const string InvalidPaging = "Trang phải từ 1 trở lên và số sự cố mỗi trang phải từ 1 đến 500.";
    public const string NotFound = "Không tìm thấy sự cố công khai đang hoạt động.";
}

public sealed class CitizenMapService(ICitizenMapRepository repository, IUserRepository users) : ICitizenMapService
{
    public async Task<bool> CanAccessAsync(Guid userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return userId != Guid.Empty && await users.GetByIdAsync(userId) is { IsDeleted: false, Role: UserRole.Citizen, Status: UserStatus.Active };
    }

    public async Task<ApiResponseDto<PaginatedResultDto<CitizenMapIncidentDto>>> GetAsync(Guid userId, CitizenMapQueryDto query, CancellationToken ct = default)
    {
        if (!await CanAccessAsync(userId, ct))
            return ApiResponseDto<PaginatedResultDto<CitizenMapIncidentDto>>.ForbiddenResult(CitizenMapMessages.Forbidden);
        if (query.PageIndex < 1 || query.PageSize is < 1 or > 500 || (long)(query.PageIndex - 1) * query.PageSize > int.MaxValue)
            return ApiResponseDto<PaginatedResultDto<CitizenMapIncidentDto>>.FailureResult(CitizenMapMessages.InvalidPaging);
        var values = new[] { query.South, query.West, query.North, query.East };
        CitizenMapBounds? bounds = null;
        if (values.Any(v => v.HasValue))
        {
            if (values.Any(v => !v.HasValue || !double.IsFinite(v.Value))
                || query.South < -90 || query.North > 90 || query.South > query.North
                || query.West < -180 || query.West > 180 || query.East < -180 || query.East > 180)
                return ApiResponseDto<PaginatedResultDto<CitizenMapIncidentDto>>.FailureResult(CitizenMapMessages.InvalidBounds);
            bounds = new(query.South!.Value, query.West!.Value, query.North!.Value, query.East!.Value);
        }
        var result = await repository.GetAsync(bounds, query.PageIndex, query.PageSize, ct);
        return ApiResponseDto<PaginatedResultDto<CitizenMapIncidentDto>>.Ok(new(result.Items.Select(Map), result.Total, query.PageIndex, query.PageSize));
    }

    public async Task<ApiResponseDto<CitizenMapIncidentDto>> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        if (!await CanAccessAsync(userId, ct)) return ApiResponseDto<CitizenMapIncidentDto>.ForbiddenResult(CitizenMapMessages.Forbidden);
        var item = await repository.GetAsync(id, ct);
        return item == null ? ApiResponseDto<CitizenMapIncidentDto>.NotFoundResult(CitizenMapMessages.NotFound)
            : ApiResponseDto<CitizenMapIncidentDto>.Ok(Map(item));
    }

    public async Task<ApiResponseDto<CitizenMapIncidentDto>> GetByAlertAsync(Guid userId, Guid publicAlertId, CancellationToken ct = default)
    {
        if (!await CanAccessAsync(userId, ct)) return ApiResponseDto<CitizenMapIncidentDto>.ForbiddenResult(CitizenMapMessages.Forbidden);
        var item = await repository.GetByAlertAsync(publicAlertId, ct);
        return item == null ? ApiResponseDto<CitizenMapIncidentDto>.NotFoundResult(CitizenMapMessages.NotFound)
            : ApiResponseDto<CitizenMapIncidentDto>.Ok(Map(item));
    }

    private static CitizenMapIncidentDto Map(CitizenMapIncident item)
    {
        var area = CitizenIncidentAlertPolicy.Area(new Incident { LocationAddress = item.LocationAddress, Latitude = item.Latitude, Longitude = item.Longitude });
        var localizedArea = LocalizationHelper.IsEnglish ? area.En : area.Vi;
        return new()
        {
            Id = item.Id, Latitude = item.Latitude, Longitude = item.Longitude, Area = localizedArea,
            Message = LocalizationHelper.Localize(CitizenIncidentAlertMessages.Body, localizedArea), EventType = item.EventType,
            Severity = item.Severity, Status = item.Status, ReportedAt = item.ReportedAt, UpdatedAt = item.UpdatedAt
        };
    }
}
