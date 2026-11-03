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

public class TrafficServiceTests
{
    private readonly Mock<ITrafficHotspotRepository> _repo = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly TrafficService _svc;

    public TrafficServiceTests()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<TrafficHotspot>())).ReturnsAsync((TrafficHotspot h) => h);
        _svc = new TrafficService(_repo.Object, _users.Object, _rt.Object, NullLogger<TrafficService>.Instance);
    }

    private static ReportTrafficHotspotDto Dto(int ttl = 45) => new()
    {
        Title = " Kẹt xe ", LocationAddress = " Nguyễn Văn Linh ", Latitude = 10.73, Longitude = 106.72,
        CongestionLevel = TrafficCongestionLevel.Moderate, TtlMinutes = ttl
    };

    [Theory]
    [InlineData(1, 10)]
    [InlineData(45, 45)]
    [InlineData(999, 180)]
    public async Task Report_TtlIsClampedBetween10And180(int ttl, int expected)
    {
        TrafficHotspot? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TrafficHotspot>())).Callback<TrafficHotspot>(h => saved = h).ReturnsAsync((TrafficHotspot h) => h);

        await _svc.ReportHotspotAsync(Dto(ttl));

        Assert.InRange((saved!.ExpiresAt - saved.CreatedAt).TotalMinutes, expected - 0.01, expected + 0.01);
        Assert.Equal("Kẹt xe", saved.Title);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Report_Anonymous_UsesDefaultReporterName_AndBroadcasts()
    {
        var r = await _svc.ReportHotspotAsync(Dto());

        Assert.Equal("Người dân / Công nhân", r.Data!.ReporterName);
        _rt.Verify(n => n.TrafficHotspotReportedAsync(It.IsAny<TrafficHotspotResponseDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Report_LoggedInUser_FillsNameAndPhoneFromProfile()
    {
        var u = new User { FullName = "Công nhân A", PhoneNumber = "0911111111" };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        TrafficHotspot? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TrafficHotspot>())).Callback<TrafficHotspot>(h => saved = h).ReturnsAsync((TrafficHotspot h) => h);

        await _svc.ReportHotspotAsync(Dto(), u.Id);

        Assert.Equal("Công nhân A", saved!.ReporterName);
        Assert.Equal("0911111111", saved.ReporterPhone);
        Assert.Equal(u.Id, saved.ReportedByUserId);
    }

    [Fact]
    public async Task Resolve_ActiveHotspot_DeactivatesAndBroadcasts()
    {
        var h = new TrafficHotspot { Title = "x", IsActive = true };
        _repo.Setup(r => r.GetByIdAsync(h.Id)).ReturnsAsync(h);
        var resolver = Guid.NewGuid();

        var r = await _svc.ResolveHotspotAsync(h.Id, resolver);

        Assert.True(r.Success);
        Assert.False(h.IsActive);
        Assert.Equal(resolver, h.ResolvedByUserId);
        _rt.Verify(n => n.TrafficHotspotReportedAsync(It.Is<TrafficHotspotResponseDto>(d => !d.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_InactiveOrUnknown_Fails()
    {
        var h = new TrafficHotspot { IsActive = false };
        _repo.Setup(r => r.GetByIdAsync(h.Id)).ReturnsAsync(h);

        Assert.False((await _svc.ResolveHotspotAsync(h.Id, Guid.NewGuid())).Success);
        Assert.False((await _svc.ResolveHotspotAsync(Guid.NewGuid(), Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task Cleanup_ReturnsRepositoryCount()
    {
        _repo.Setup(r => r.DeactivateExpiredHotspotsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);

        Assert.Equal(3, await _svc.CleanupExpiredHotspotsAsync());
    }
}
