using EmergencyDispatch.API.Hubs;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Application.DTOs.Incident;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class SettingsRealtimeNotifierTests
{
    [Theory]
    [InlineData("serviceArea", true)]
    [InlineData("dispatch", false)]
    [InlineData("escalation", false)]
    [InlineData("duplicateDetection", false)]
    [InlineData("citizenIncidentAlerts", false)]
    public async Task OnlyPublicServiceAreaChangesAreBroadcastToEveryone(string key, bool broadcast)
    {
        var clients = new Mock<IHubClients>();
        var all = new Mock<IClientProxy>();
        var operators = new Mock<IClientProxy>();
        clients.SetupGet(c => c.All).Returns(all.Object);
        clients.Setup(c => c.Group(RealtimeContract.OperatorsGroup)).Returns(operators.Object);
        all.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        operators.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var incidents = new Mock<IHubContext<IncidentHub>>();
        incidents.SetupGet(h => h.Clients).Returns(clients.Object);
        var tracking = new Mock<IHubContext<TrackingHub>>();
        var notifier = new SignalRRealtimeNotifier(incidents.Object, tracking.Object, NullLogger<SignalRRealtimeNotifier>.Instance);
        var change = new SettingsChangedEventDto { Key = key, UpdatedAt = DateTime.UtcNow };
        using var cancellation = new CancellationTokenSource();

        await notifier.SettingsChangedAsync(change, cancellation.Token);

        var selected = broadcast ? all : operators;
        var other = broadcast ? operators : all;
        selected.Verify(p => p.SendCoreAsync(RealtimeContract.ReceiveSettingsChanged,
            It.Is<object?[]>(args => args.Length == 1 && ReferenceEquals(args[0], change)), cancellation.Token), Times.Once);
        other.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
        clients.VerifyGet(c => c.All, broadcast ? Times.Once() : Times.Never());
        clients.Verify(c => c.Group(It.IsAny<string>()), broadcast ? Times.Never() : Times.Once());
        tracking.VerifyNoOtherCalls();
    }
}
