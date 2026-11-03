using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Enums;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class OperatorScopeTests
{
    private static readonly Guid StationA = Guid.NewGuid();
    private static readonly Guid StationB = Guid.NewGuid();

    private static ICurrentUserContext User(UserRole role, Guid? station)
    {
        var m = new Mock<ICurrentUserContext>();
        m.SetupGet(x => x.Role).Returns(role);
        m.SetupGet(x => x.StationId).Returns(station);
        return m.Object;
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.RescueStaff)]
    [InlineData(UserRole.Citizen)]
    public void NonOperators_AreNotStationScoped(UserRole role)
    {
        var u = User(role, null);

        Assert.False(u.IsStationScoped());
        Assert.Null(u.CheckStation(StationB));
        Assert.Null(u.CheckIncident(StationB));
        Assert.Null(u.FilterStationId());
    }

    [Fact]
    public void Operator_OwnStation_IsAllowed()
    {
        var u = User(UserRole.Operator, StationA);

        Assert.Null(u.CheckStation(StationA));
        Assert.Null(u.CheckIncident(StationA));
        Assert.Equal(StationA, u.FilterStationId());
    }

    [Fact]
    public void Operator_OtherStation_IsRejected()
    {
        var u = User(UserRole.Operator, StationA);

        Assert.Equal(OperatorScope.OtherStationMessage, u.CheckStation(StationB));
        Assert.Equal(OperatorScope.OtherStationIncidentMessage, u.CheckIncident(StationB));
    }

    [Fact]
    public void Operator_WithoutStation_IsRejected_AndFiltersToEmptyGuid()
    {
        var u = User(UserRole.Operator, null);

        Assert.Equal(OperatorScope.NoStationMessage, u.CheckStation(StationA));
        Assert.Equal(OperatorScope.NoStationMessage, u.CheckIncident(StationA));
        Assert.Equal(Guid.Empty, u.FilterStationId());
    }

    [Fact]
    public void Operator_IncidentWithoutResponsibleStation_IsAllowed()
    {
        Assert.Null(User(UserRole.Operator, StationA).CheckIncident(null));
    }
}
