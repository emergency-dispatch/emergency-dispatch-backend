using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Team;

namespace EmergencyDispatch.Application.Interfaces;

public interface IStationTeamService
{
    Task<ApiResponseDto<List<StationTeamDto>>> GetTeamsAsync(Guid stationId, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<StationTeamDto>> CreateAsync(Guid stationId, StationTeamInputDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<StationTeamDto>> UpdateAsync(Guid stationId, Guid teamId, StationTeamInputDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<bool>> DeleteAsync(Guid stationId, Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tương thích API cũ assign-station-team: đưa nhân viên vào tổ theo tên (tạo tổ nếu chưa có), null = rời tổ
    /// </summary>
    Task AssignByNameAsync(Guid staffId, Guid? stationId, string? teamName, CancellationToken cancellationToken = default);
}
