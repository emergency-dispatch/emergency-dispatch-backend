using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Security;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Quản lý danh sách số điện thoại bị chặn gửi SOS (FR-OP-03, BR-05)
/// </summary>
public class BlockedNumberService : IBlockedNumberService
{
    private readonly IBlockedPhoneRepository _repository;
    private readonly ILogger<BlockedNumberService> _logger;

    public BlockedNumberService(IBlockedPhoneRepository repository, ILogger<BlockedNumberService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<PaginatedResultDto<BlockedNumberDto>> GetPagedAsync(
        BlockedNumberStatus? status,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _repository.GetPagedAsync(status, search, pageIndex, pageSize, now, cancellationToken);
        return new PaginatedResultDto<BlockedNumberDto>(items.Select(b => Map(b, now)), total, pageIndex, pageSize);
    }

    public async Task<ApiResponseDto<BlockedNumberDto>> UnblockAsync(Guid id, UnblockNumberDto dto, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var block = await _repository.GetWithUsersAsync(id, cancellationToken);
        if (block == null)
        {
            return ApiResponseDto<BlockedNumberDto>.NotFoundResult("Không tìm thấy bản ghi chặn số.");
        }

        var now = DateTime.UtcNow;
        if (block.GetStatus(now) != BlockedNumberStatus.Active)
        {
            return ApiResponseDto<BlockedNumberDto>.FailureResult("Số điện thoại này không còn trong thời gian bị chặn.");
        }

        block.RemovedAt = now;
        block.RemovedByUserId = actorUserId;
        block.RemoveReason = dto.Reason.Trim();
        block.UpdatedAt = now;
        await _repository.UpdateAsync(block);

        _logger.LogInformation("Admin {Actor} gỡ chặn số {Phone}: {Reason}", actorUserId, PhoneMasking.Mask(block.PhoneNumber), block.RemoveReason);
        return ApiResponseDto<BlockedNumberDto>.SuccessResult(Map(block, now), "Đã gỡ chặn số điện thoại.");
    }

    public async Task<ApiResponseDto<BlockedNumberDto>> ExtendAsync(Guid id, ExtendBlockDto dto, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var block = await _repository.GetWithUsersAsync(id, cancellationToken);
        if (block == null)
        {
            return ApiResponseDto<BlockedNumberDto>.NotFoundResult("Không tìm thấy bản ghi chặn số.");
        }

        if (block.RemovedAt.HasValue)
        {
            return ApiResponseDto<BlockedNumberDto>.FailureResult("Bản ghi đã được gỡ chặn, không thể gia hạn. Hãy chặn lại từ sự cố mới.");
        }

        var now = DateTime.UtcNow;
        block.Duration = dto.Duration;
        block.ExpiresAt = BlockedPhoneNumber.CalculateExpiry(dto.Duration, now);
        block.LastExtendedAt = now;
        block.Reason = $"{block.Reason} | Gia hạn: {dto.Reason.Trim()}";
        if (block.Reason.Length > 500) block.Reason = block.Reason[..500];
        block.UpdatedAt = now;
        await _repository.UpdateAsync(block);

        _logger.LogInformation("Admin {Actor} gia hạn chặn số {Phone} ({Duration})", actorUserId, PhoneMasking.Mask(block.PhoneNumber), dto.Duration);
        return ApiResponseDto<BlockedNumberDto>.SuccessResult(Map(block, now), "Đã gia hạn thời gian chặn.");
    }

    private static BlockedNumberDto Map(BlockedPhoneNumber b, DateTime now) => new()
    {
        Id = b.Id,
        MaskedPhone = PhoneMasking.Mask(b.PhoneNumber) ?? string.Empty,
        Duration = b.Duration,
        Status = b.GetStatus(now),
        BlockedAt = b.BlockedAt,
        ExpiresAt = b.ExpiresAt,
        Reason = b.Reason,
        IncidentId = b.IncidentId,
        BlockedByName = b.BlockedByUser?.FullName,
        LastExtendedAt = b.LastExtendedAt,
        RemovedAt = b.RemovedAt,
        RemovedByName = b.RemovedByUser?.FullName,
        RemoveReason = b.RemoveReason
    };
}
