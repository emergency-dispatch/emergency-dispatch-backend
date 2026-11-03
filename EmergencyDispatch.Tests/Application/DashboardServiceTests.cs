using System.Linq.Expressions;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class DashboardServiceTests
{
    private static Mock<TRepo> Repo<TRepo, T>(IEnumerable<T> items) where TRepo : class, IGenericRepository<T> where T : class
    {
        var m = new Mock<TRepo>();
        m.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<T, bool>>?>(), It.IsAny<Func<IQueryable<T>, IQueryable<T>>?>()))
            .ReturnsAsync((Expression<Func<T, bool>>? p, Func<IQueryable<T>, IQueryable<T>>? _) => p == null ? items : items.Where(p.Compile()).ToList());
        return m;
    }

    [Fact]
    public async Task AdminStats_CountsByStatusSeverityRoleAndKpis()
    {
        var t0 = DateTime.UtcNow;
        var incidents = new List<Incident>
        {
            new() { Status = IncidentStatus.Pending, Severity = SeverityLevel.Unclassified, CreatedAt = t0.AddMinutes(-9) },
            new() { Status = IncidentStatus.AiProcessed, Severity = SeverityLevel.Level1, CreatedAt = t0.AddMinutes(-8) },
            new() { Status = IncidentStatus.Dispatched, Severity = SeverityLevel.Level2, ResponseTimeMs = 60_000, CreatedAt = t0.AddMinutes(-7) },
            new() { Status = IncidentStatus.Escalated, Severity = SeverityLevel.Level3, CreatedAt = t0.AddMinutes(-6) },
            new() { Status = IncidentStatus.Completed, Severity = SeverityLevel.Level3, ResponseTimeMs = 180_000, CreatedAt = t0.AddMinutes(-5) },
            new() { Status = IncidentStatus.Cancelled, Severity = SeverityLevel.Level1, CreatedAt = t0.AddMinutes(-4) },
            new() { Status = IncidentStatus.Completed, IsDeleted = true }
        };
        var units = new List<RescueUnit>
        {
            new() { Status = RescueUnitStatus.Available }, new() { Status = RescueUnitStatus.OnScene },
            new() { Status = RescueUnitStatus.Returning }, new() { Status = RescueUnitStatus.Maintenance }
        };
        var stations = new List<Station> { new() { IsActive = true }, new() { IsActive = false } };
        var users = new List<User>
        {
            new() { Role = UserRole.Citizen }, new() { Role = UserRole.Citizen }, new() { Role = UserRole.Operator },
            new() { Role = UserRole.RescueStaff }, new() { Role = UserRole.Admin }
        };
        var svc = new DashboardService(Repo<IIncidentRepository, Incident>(incidents).Object, Repo<IRescueUnitRepository, RescueUnit>(units).Object,
            Repo<IStationRepository, Station>(stations).Object, Repo<IUserRepository, User>(users).Object);

        var s = await svc.GetAdminStatsAsync();

        Assert.Equal(6, s.TotalIncidents);
        Assert.Equal(4, s.ActiveIncidents);
        Assert.Equal(2, s.PendingIncidents);
        Assert.Equal(1, s.DispatchedIncidents);
        Assert.Equal(1, s.ResolvedIncidents);
        Assert.Equal(1, s.CancelledIncidents);
        Assert.Equal(1, s.EscalatedIncidents);
        Assert.Equal(2, s.CriticalLevel3Incidents);
        Assert.Equal(2, s.OnMissionUnits);
        Assert.Equal(1, s.AvailableUnits);
        Assert.Equal(1, s.ActiveStations);
        Assert.Equal(2, s.TotalCitizens);
        Assert.Equal(2, s.AverageResponseTimeMinutes);
        Assert.Equal(16.7, s.IncidentResolutionRatePercent);
        Assert.Equal(6, s.RecentIncidents.Count);
        Assert.Equal(IncidentStatus.Cancelled.ToString(), s.RecentIncidents[0].Status);
    }

    [Fact]
    public async Task AdminStats_EmptySystem_HasZeroRates()
    {
        var svc = new DashboardService(Repo<IIncidentRepository, Incident>(new List<Incident>()).Object, Repo<IRescueUnitRepository, RescueUnit>(new List<RescueUnit>()).Object,
            Repo<IStationRepository, Station>(new List<Station>()).Object, Repo<IUserRepository, User>(new List<User>()).Object);

        var s = await svc.GetAdminStatsAsync();

        Assert.Equal(0, s.IncidentResolutionRatePercent);
        Assert.Equal(0, s.AverageResponseTimeMinutes);
    }
}
