using EmergencyDispatch.Application.DTOs.Report;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class ReportServiceTests
{
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<IReportFileGenerator> _files = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly List<Incident> _data = new();
    private int _total;
    private readonly ReportService _svc;

    public ReportServiceTests()
    {
        _incidents.Setup(i => i.GetClosedHistoryAsync(It.IsAny<IReadOnlyCollection<IncidentStatus>>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<Guid?>(), It.IsAny<SeverityLevel?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (_data, _total == 0 ? _data.Count : _total));
        _files.Setup(f => f.BuildIncidentCsv(It.IsAny<IReadOnlyList<IncidentHistoryItemDto>>(), It.IsAny<IncidentReportMetaDto>())).Returns(new byte[] { 1 });
        _files.Setup(f => f.BuildIncidentPdf(It.IsAny<IReadOnlyList<IncidentHistoryItemDto>>(), It.IsAny<IncidentReportMetaDto>())).Returns(new byte[] { 2 });
        _me.SetupGet(m => m.FullName).Returns("Admin A");
        _svc = new ReportService(_incidents.Object, _stations.Object, _files.Object, _audit.Object, _me.Object);
    }

    private static Incident Closed(IncidentStatus status, SeverityLevel sev, int arriveMin, int closeMin)
    {
        var t0 = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc);
        var inc = new Incident { Status = status, Severity = sev, CreatedAt = t0, ClosedAt = status == IncidentStatus.Completed ? t0.AddMinutes(closeMin) : null,
            CancelledAt = status == IncidentStatus.Cancelled ? t0.AddMinutes(closeMin) : null };
        inc.DispatchAssignments.Add(new DispatchAssignment
        {
            DispatchedAt = t0.AddMinutes(1), ArrivedAt = t0.AddMinutes(arriveMin), Status = DispatchAssignmentStatus.Completed,
            RescueUnit = new RescueUnit { PlateNumber = "51A-1", Station = new Station { Name = "Q7" } }
        });
        inc.DispatchAssignments.Add(new DispatchAssignment { DispatchedAt = t0, ArrivedAt = t0.AddMinutes(1), Status = DispatchAssignmentStatus.Rejected,
            RescueUnit = new RescueUnit { PlateNumber = "REJECTED" } });
        return inc;
    }

    [Fact]
    public void Map_IgnoresRejectedAssignments_AndComputesMinutes()
    {
        var row = ReportService.Map(Closed(IncidentStatus.Completed, SeverityLevel.Level2, arriveMin: 8, closeMin: 40));

        Assert.Equal(8, row.ResponseMinutes);
        Assert.Equal(40, row.ResolutionMinutes);
        Assert.Equal(new[] { "51A-1" }, row.Units);
        Assert.Equal(new[] { "Q7" }, row.Stations);
    }

    [Fact]
    public void Map_AiSeverityOnlyWhenAiSucceeded()
    {
        var ok = Closed(IncidentStatus.Completed, SeverityLevel.Level2, 5, 10);
        ok.AiClassification = new AiClassification { IsSuccess = true, SeverityScore = SeverityLevel.Level3 };
        var failed = Closed(IncidentStatus.Completed, SeverityLevel.Level2, 5, 10);
        failed.AiClassification = new AiClassification { IsSuccess = false, SeverityScore = SeverityLevel.Unclassified };

        Assert.Equal(3, ReportService.Map(ok).AiSeverityScore);
        Assert.Null(ReportService.Map(failed).AiSeverityScore);
    }

    [Fact]
    public void BuildSummary_CountsAndAverages()
    {
        var rows = new[]
        {
            Closed(IncidentStatus.Completed, SeverityLevel.Level2, 6, 30),
            Closed(IncidentStatus.Completed, SeverityLevel.Level3, 10, 50),
            Closed(IncidentStatus.Cancelled, SeverityLevel.Level1, 4, 5)
        }.Select(ReportService.Map).ToList();

        var s = ReportService.BuildSummary(rows);

        Assert.Equal(3, s.Total);
        Assert.Equal(2, s.Completed);
        Assert.Equal(1, s.Cancelled);
        Assert.Equal(1, s.BySeverity["Level3"]);
        Assert.Equal(6.7, s.AvgResponseMinutes);
        Assert.Equal(40, s.AvgResolutionMinutes);   // cancelled excluded
    }

    [Fact]
    public void BuildSummary_Empty_HasNullAverages()
    {
        var s = ReportService.BuildSummary(new List<IncidentHistoryItemDto>());

        Assert.Equal(0, s.Total);
        Assert.Null(s.AvgResponseMinutes);
    }

    [Fact]
    public async Task History_InvalidRange_Throws_AndPageSizeIsClamped()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.GetIncidentHistoryAsync(new IncidentHistoryFilterDto { From = DateTime.UtcNow, To = DateTime.UtcNow.AddDays(-1) }));

        var page = await _svc.GetIncidentHistoryAsync(new IncidentHistoryFilterDto { PageIndex = 0, PageSize = 1000 });
        Assert.Equal(1, page.PageIndex);
        Assert.Equal(ReportService.MaxPageSize, page.PageSize);
    }

    [Theory]
    [InlineData(ClosedIncidentFilter.Completed, new[] { IncidentStatus.Completed })]
    [InlineData(ClosedIncidentFilter.Cancelled, new[] { IncidentStatus.Cancelled })]
    [InlineData(ClosedIncidentFilter.All, new[] { IncidentStatus.Completed, IncidentStatus.Cancelled })]
    public async Task History_StatusFilter_MapsToClosedStatuses(ClosedIncidentFilter filter, IncidentStatus[] expected)
    {
        IReadOnlyCollection<IncidentStatus>? used = null;
        _incidents.Setup(i => i.GetClosedHistoryAsync(It.IsAny<IReadOnlyCollection<IncidentStatus>>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<Guid?>(), It.IsAny<SeverityLevel?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<IncidentStatus>, DateTime?, DateTime?, Guid?, SeverityLevel?, string?, int, int, CancellationToken>((s, _, _, _, _, _, _, _, _) => used = s)
            .ReturnsAsync((new List<Incident>(), 0));

        await _svc.GetIncidentHistoryAsync(new IncidentHistoryFilterDto { Status = filter });

        Assert.Equal(expected, used);
    }

    [Theory]
    [InlineData(ReportFormat.Csv, "text/csv; charset=utf-8", ".csv", 1)]
    [InlineData(ReportFormat.Pdf, "application/pdf", ".pdf", 2)]
    public async Task Export_ProducesFileAndAudits(ReportFormat format, string contentType, string ext, byte marker)
    {
        _data.Add(Closed(IncidentStatus.Completed, SeverityLevel.Level2, 5, 20));

        var file = await _svc.ExportIncidentsAsync(new IncidentHistoryFilterDto(), format);

        Assert.Equal(contentType, file.ContentType);
        Assert.EndsWith(ext, file.FileName);
        Assert.StartsWith("bao-cao-su-co_", file.FileName);
        Assert.Equal(new[] { marker }, file.Content);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.Exported, It.IsAny<string>(), null, file.FileName, It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Export_MarksTruncatedWhenMoreRowsThanLimit_AndDescribesFilter()
    {
        _data.Add(Closed(IncidentStatus.Completed, SeverityLevel.Level2, 5, 20));
        _total = ReportService.MaxExportRows + 10;
        var station = new Station { Name = "Trạm Q7" };
        _stations.Setup(s => s.GetByIdAsync(station.Id)).ReturnsAsync(station);
        IncidentReportMetaDto? meta = null;
        _files.Setup(f => f.BuildIncidentCsv(It.IsAny<IReadOnlyList<IncidentHistoryItemDto>>(), It.IsAny<IncidentReportMetaDto>()))
            .Callback<IReadOnlyList<IncidentHistoryItemDto>, IncidentReportMetaDto>((_, m) => meta = m).Returns(Array.Empty<byte>());

        await _svc.ExportIncidentsAsync(new IncidentHistoryFilterDto { StationId = station.Id, Severity = SeverityLevel.Level2, Search = " cháy " }, ReportFormat.Csv);

        Assert.True(meta!.Truncated);
        Assert.Equal("Admin A", meta.GeneratedBy);
        Assert.Contains("Trạm: Trạm Q7", meta.FilterDescription);
        Assert.Contains("Từ khóa: \"cháy\"", meta.FilterDescription);
    }
}
