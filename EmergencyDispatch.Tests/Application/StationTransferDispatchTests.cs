using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class StationTransferDispatchTests
{
    [Theory]
    [InlineData(AutoDispatchOutcome.Dispatched)]
    [InlineData(AutoDispatchOutcome.NotEligible)]
    [InlineData(AutoDispatchOutcome.SkippedNoUnits)]
    [InlineData(AutoDispatchOutcome.Disabled)]
    public async Task DurablePendingTransfer_IsAttemptedOnceWithItsExactStationAndVersion(AutoDispatchOutcome result)
    {
        var store = new Mock<IIncidentStationTransferRepository>(); var incidents = new Mock<IIncidentRepository>();
        var ai = new Mock<IAutoDispatchService>();
        var transfer = new IncidentStationTransfer { IncidentId = Guid.NewGuid(), ToStationId = Guid.NewGuid(), AssignmentVersion = 3 };
        var pending = new List<IncidentStationTransfer> { transfer };
        store.Setup(s => s.GetPendingDispatchesAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(() => pending.ToArray());
        store.Setup(s => s.MarkDispatchAttemptedAsync(transfer.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => pending.Clear()).Returns(Task.CompletedTask);
        ai.Setup(a => a.TryAfterStationTransferAsync(transfer.IncidentId, transfer.ToStationId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        var service = new StationTransferDispatchService(store.Object, incidents.Object, ai.Object, NullLogger<StationTransferDispatchService>.Instance);
        await service.ProcessPendingAsync(); await service.ProcessPendingAsync();
        ai.Verify(a => a.TryAfterStationTransferAsync(transfer.IncidentId, transfer.ToStationId, 3, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkDispatchAttemptedAsync(transfer.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AiFailureFallsBackToOperatorAndDoesNotStopOtherTransfers()
    {
        var store = new Mock<IIncidentStationTransferRepository>(); var incidents = new Mock<IIncidentRepository>(); var ai = new Mock<IAutoDispatchService>();
        var one = new IncidentStationTransfer { IncidentId = Guid.NewGuid(), ToStationId = Guid.NewGuid(), AssignmentVersion = 2 };
        var two = new IncidentStationTransfer { IncidentId = Guid.NewGuid(), ToStationId = Guid.NewGuid(), AssignmentVersion = 2 };
        store.Setup(s => s.GetPendingDispatchesAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { one, two });
        ai.Setup(a => a.TryAfterStationTransferAsync(one.IncidentId, one.ToStationId, 2, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Provider unavailable"));
        var service = new StationTransferDispatchService(store.Object, incidents.Object, ai.Object, NullLogger<StationTransferDispatchService>.Instance);
        await service.ProcessPendingAsync();
        ai.Verify(a => a.TryAfterStationTransferAsync(two.IncidentId, two.ToStationId, 2, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkDispatchAttemptedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ShutdownLeavesPendingTransferForRestart()
    {
        var store = new Mock<IIncidentStationTransferRepository>(); var incidents = new Mock<IIncidentRepository>(); var ai = new Mock<IAutoDispatchService>();
        var transfer = new IncidentStationTransfer { IncidentId = Guid.NewGuid(), ToStationId = Guid.NewGuid(), AssignmentVersion = 2 };
        using var cancellation = new CancellationTokenSource();
        store.Setup(s => s.GetPendingDispatchesAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { transfer });
        ai.Setup(a => a.TryAfterStationTransferAsync(transfer.IncidentId, transfer.ToStationId, 2, It.IsAny<CancellationToken>()))
            .Callback(() => cancellation.Cancel()).ThrowsAsync(new OperationCanceledException());
        var service = new StationTransferDispatchService(store.Object, incidents.Object, ai.Object, NullLogger<StationTransferDispatchService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProcessPendingAsync(cancellation.Token));
        store.Verify(s => s.MarkDispatchAttemptedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
