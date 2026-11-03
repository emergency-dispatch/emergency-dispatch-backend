using System.Net;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Infrastructure.BackgroundJobs;
using EmergencyDispatch.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmergencyDispatch.Tests.Infrastructure;

public class MemoryOtpAttemptLimiterTests
{
    private readonly MemoryOtpAttemptLimiter _limiter = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void FifthFailure_ReachesLimit()
    {
        var results = Enumerable.Range(0, 5).Select(_ => _limiter.RegisterFailure("verify-email", "a@test.local")).ToList();

        Assert.Equal(new[] { false, false, false, false, true }, results);
    }

    [Fact]
    public void Counter_IsPerPurposeAndCaseInsensitiveEmail()
    {
        for (var i = 0; i < 4; i++) _limiter.RegisterFailure("verify-email", "A@Test.Local ");

        Assert.False(_limiter.RegisterFailure("reset-password", "a@test.local"));
        Assert.True(_limiter.RegisterFailure("verify-email", "a@test.local"));
    }

    [Fact]
    public void Reset_StartsCountingAgain()
    {
        for (var i = 0; i < 4; i++) _limiter.RegisterFailure("p", "e");
        _limiter.Reset("p", "e");

        Assert.False(_limiter.RegisterFailure("p", "e"));
    }
}

public class AiAnalysisQueueTests
{
    private static AiAnalysisJob Job() => new(Guid.NewGuid(), "https://x/a.jpg", null, DateTime.UtcNow);

    [Fact]
    public async Task Jobs_AreDeliveredInFifoOrder()
    {
        var q = new AiAnalysisQueue();
        var jobs = Enumerable.Range(0, 5).Select(_ => Job()).ToList();
        foreach (var j in jobs) await q.EnqueueAsync(j);

        var read = new List<AiAnalysisJob>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var j in q.DequeueAllAsync(cts.Token))
        {
            read.Add(j);
            if (read.Count == jobs.Count) break;
        }

        Assert.Equal(jobs, read);
    }

    [Fact]
    public async Task FullQueue_WaitsInsteadOfDropping()
    {
        var q = new AiAnalysisQueue(capacity: 1);
        await q.EnqueueAsync(Job());

        var pending = q.EnqueueAsync(Job()).AsTask();
        await Task.Delay(100);
        Assert.False(pending.IsCompleted);

        await using var e = q.DequeueAllAsync(CancellationToken.None).GetAsyncEnumerator();
        await e.MoveNextAsync();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(pending.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task NullJob_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new AiAnalysisQueue().EnqueueAsync(null!).AsTask());
    }

    [Fact]
    public void DefaultCapacity_Is500()
    {
        Assert.Equal(500, AiAnalysisQueue.DefaultCapacity);
    }
}

public class OsrmRoutingServiceTests
{
    private static OsrmRoutingService Svc(FakeHttpHandler handler, string? baseUrl = null)
        => new(new FakeHttpClientFactory().With(OsrmRoutingService.HttpClientName, handler), new MemoryCache(new MemoryCacheOptions()),
            TestHelpers.Config(("Routing:OsrmBaseUrl", baseUrl)), NullLogger<OsrmRoutingService>.Instance);

    private const string RouteJson = "{\"routes\":[{\"distance\":5230.4,\"duration\":612,\"geometry\":{\"coordinates\":[[106.70,10.77],[106.71,10.75],[106.72,10.73]]}}]}";

    [Fact]
    public async Task GetRoute_ParsesOsrmGeometryAsLatLng()
    {
        var h = FakeHttpHandler.Json(RouteJson);

        var r = await Svc(h, "http://osrm.test/").GetRouteAsync(10.77, 106.70, 10.73, 106.72);

        Assert.False(r.IsApproximate);
        Assert.Equal(5.23, r.DistanceKm, 2);
        Assert.Equal(10.2, r.DurationMinutes);
        Assert.Equal(3, r.Points.Count);
        Assert.Equal(10.77, r.Points[0].Latitude);
        Assert.StartsWith("http://osrm.test/route/v1/driving/106.700000,10.770000;106.720000,10.730000", h.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetRoute_IsCached()
    {
        var h = FakeHttpHandler.Json(RouteJson);
        var svc = Svc(h);

        await svc.GetRouteAsync(10.77, 106.70, 10.73, 106.72);
        await svc.GetRouteAsync(10.77, 106.70, 10.73, 106.72);

        Assert.Single(h.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "{\"routes\":[]}")]
    public async Task GetRoute_BadResponse_FallsBackToHaversine(HttpStatusCode code, string body)
    {
        var r = await Svc(FakeHttpHandler.Json(body, code)).GetRouteAsync(10, 106, 11, 106);

        Assert.True(r.IsApproximate);
        Assert.InRange(r.DistanceKm, 111.0, 111.4);
        Assert.Equal(Math.Round(r.DistanceKm / 35 * 60, 1), r.DurationMinutes);
        Assert.Equal(2, r.Points.Count);
    }

    [Fact]
    public async Task GetRoute_NetworkError_FallsBackToHaversine()
    {
        var r = await Svc(FakeHttpHandler.Throws(new HttpRequestException("down"))).GetRouteAsync(10, 106, 10.1, 106);

        Assert.True(r.IsApproximate);
    }

    [Fact]
    public async Task GetLegs_ParsesTableAndKeepsNullForUnreachable()
    {
        var h = FakeHttpHandler.Json("{\"durations\":[[300],[null]],\"distances\":[[2500],[null]]}");

        var legs = await Svc(h).GetLegsToAsync(new List<(double, double)> { (10.70, 106.70), (10.71, 106.71) }, 10.73, 106.72);

        Assert.Equal(2, legs!.Count);
        Assert.Equal(2.5, legs[0]!.DistanceKm);
        Assert.Equal(5, legs[0]!.DurationMinutes);
        Assert.Null(legs[1]);
        Assert.Contains("sources=0;1&destinations=2", h.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetLegs_EmptyOrigins_ReturnsEmptyWithoutCall()
    {
        var h = FakeHttpHandler.Json("{}");

        Assert.Empty((await Svc(h).GetLegsToAsync(new List<(double, double)>(), 10, 106))!);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task GetLegs_Failure_ReturnsNull()
    {
        Assert.Null(await Svc(FakeHttpHandler.Json("{}", HttpStatusCode.BadGateway)).GetLegsToAsync(new List<(double, double)> { (10, 106) }, 10.1, 106));
        Assert.Null(await Svc(FakeHttpHandler.Throws(new TaskCanceledException("timeout"))).GetLegsToAsync(new List<(double, double)> { (10, 106) }, 10.1, 106));
    }
}
