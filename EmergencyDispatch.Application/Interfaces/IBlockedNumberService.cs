using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Security;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

public interface IBlockedNumberService
{
    Task<PaginatedResultDto<BlockedNumberDto>> GetPagedAsync(BlockedNumberStatus? status, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<BlockedNumberDto>> UnblockAsync(Guid id, UnblockNumberDto dto, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<BlockedNumberDto>> ExtendAsync(Guid id, ExtendBlockDto dto, Guid actorUserId, CancellationToken cancellationToken = default);
}
