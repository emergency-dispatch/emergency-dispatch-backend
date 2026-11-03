using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Security;
using EmergencyDispatch.Application.DTOs.Traffic;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class BlockedPhoneNumberEntityTests
{
    [Theory]
    [InlineData(BlockDuration.SevenDays, 7)]
    [InlineData(BlockDuration.ThirtyDays, 30)]
    [InlineData(BlockDuration.OneYear, 365)]
    public void CalculateExpiry_ByDuration(BlockDuration d, int days)
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(from.AddDays(days), BlockedPhoneNumber.CalculateExpiry(d, from));
    }

    [Fact]
    public void CalculateExpiry_Permanent_IsNull()
    {
        Assert.Null(BlockedPhoneNumber.CalculateExpiry(BlockDuration.Permanent, DateTime.UtcNow));
    }

    [Fact]
    public void GetStatus_ActiveExpiredRemoved()
    {
        var now = DateTime.UtcNow;

        Assert.Equal(BlockedNumberStatus.Active, new BlockedPhoneNumber { ExpiresAt = now.AddDays(1) }.GetStatus(now));
        Assert.Equal(BlockedNumberStatus.Active, new BlockedPhoneNumber { ExpiresAt = null }.GetStatus(now));
        Assert.Equal(BlockedNumberStatus.Expired, new BlockedPhoneNumber { ExpiresAt = now }.GetStatus(now));
        Assert.Equal(BlockedNumberStatus.Removed, new BlockedPhoneNumber { ExpiresAt = now.AddDays(1), RemovedAt = now }.GetStatus(now));
    }
}

public class BlockedNumberServiceTests
{
    private readonly Mock<IBlockedPhoneRepository> _repo = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly BlockedNumberService _svc;

    public BlockedNumberServiceTests()
    {
        _svc = new BlockedNumberService(_repo.Object, NullLogger<BlockedNumberService>.Instance, _audit.Object);
    }

    private BlockedPhoneNumber Block(Action<BlockedPhoneNumber>? tweak = null)
    {
        var b = new BlockedPhoneNumber { PhoneNumber = "0901234567", Reason = "Báo giả", Duration = BlockDuration.SevenDays, ExpiresAt = DateTime.UtcNow.AddDays(3) };
        tweak?.Invoke(b);
        _repo.Setup(r => r.GetWithUsersAsync(b.Id, It.IsAny<CancellationToken>())).ReturnsAsync(b);
        return b;
    }

    [Fact]
    public async Task Unblock_ActiveBlock_IsRemovedAndAuditedWithMaskedPhone()
    {
        var b = Block();
        var actor = Guid.NewGuid();

        var r = await _svc.UnblockAsync(b.Id, new UnblockNumberDto { Reason = "  Nhầm  " }, actor);

        Assert.True(r.Success);
        Assert.Equal(actor, b.RemovedByUserId);
        Assert.Equal("Nhầm", b.RemoveReason);
        Assert.Equal("090***567", r.Data!.MaskedPhone);
        Assert.Equal(BlockedNumberStatus.Removed, r.Data.Status);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.Unblocked, It.IsAny<string>(), b.Id, "090***567", It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unblock_ExpiredOrUnknown_Fails()
    {
        var expired = Block(x => x.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));

        Assert.False((await _svc.UnblockAsync(expired.Id, new UnblockNumberDto { Reason = "x" }, Guid.NewGuid())).Success);
        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.UnblockAsync(Guid.NewGuid(), new UnblockNumberDto { Reason = "x" }, Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task Extend_ChangesDurationAndAppendsReason()
    {
        var b = Block();

        var r = await _svc.ExtendAsync(b.Id, new ExtendBlockDto { Duration = BlockDuration.ThirtyDays, Reason = "Tái phạm" }, Guid.NewGuid());

        Assert.True(r.Success);
        Assert.Equal(BlockDuration.ThirtyDays, b.Duration);
        Assert.InRange((b.ExpiresAt!.Value - DateTime.UtcNow).TotalDays, 29.9, 30.1);
        Assert.EndsWith("Gia hạn: Tái phạm", b.Reason);
        Assert.NotNull(b.LastExtendedAt);
    }

    [Fact]
    public async Task Extend_ReasonIsCappedAt500Chars()
    {
        var b = Block(x => x.Reason = new string('a', 495));

        await _svc.ExtendAsync(b.Id, new ExtendBlockDto { Duration = BlockDuration.Permanent, Reason = "more text" }, Guid.NewGuid());

        Assert.Equal(500, b.Reason.Length);
        Assert.Null(b.ExpiresAt);
    }

    [Fact]
    public async Task Extend_RemovedBlock_Fails()
    {
        var b = Block(x => x.RemovedAt = DateTime.UtcNow);

        Assert.False((await _svc.ExtendAsync(b.Id, new ExtendBlockDto { Duration = BlockDuration.OneYear, Reason = "x" }, Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task GetPaged_ClampsPageSize()
    {
        _repo.Setup(r => r.GetPagedAsync(null, null, 1, 100, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<BlockedPhoneNumber>(), 0));

        await _svc.GetPagedAsync(null, null, 1, 5000);

        _repo.Verify(r => r.GetPagedAsync(null, null, 1, 100, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

