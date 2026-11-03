using EmergencyDispatch.API.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EmergencyDispatch.Tests.Api;

public class CorsOriginPolicyTests
{
    private static CorsOriginPolicy Policy(string env, params string[] origins)
    {
        var values = origins.Select((o, i) => ($"Cors:AllowedOrigins:{i}", (string?)o)).ToArray();
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(env);
        return new CorsOriginPolicy(TestHelpers.Config(values), host.Object);
    }

    [Theory]
    [InlineData("https://dispatch.example.com", true)]
    [InlineData("https://DISPATCH.example.com/", true)]
    [InlineData("https://evil.example.com", false)]
    [InlineData("http://localhost:5173", false)]
    public void ExplicitOrigins_OnlyThoseAreAllowed(string origin, bool allowed)
    {
        var p = Policy(Environments.Development, "https://dispatch.example.com/");

        Assert.True(p.HasExplicitOrigins);
        Assert.False(p.AllowsLocalNetwork);
        Assert.Equal(allowed, p.IsAllowed(origin));
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    [InlineData("http://192.168.1.20:5173", true)]
    [InlineData("http://10.0.0.5", true)]
    [InlineData("http://172.16.0.1", true)]
    [InlineData("http://172.32.0.1", false)]
    [InlineData("http://8.8.8.8", false)]
    [InlineData("https://evil.example.com", false)]
    [InlineData("not-a-url", false)]
    public void DevelopmentWithoutConfig_AllowsLoopbackAndPrivateLan(string origin, bool allowed)
    {
        Assert.Equal(allowed, Policy(Environments.Development).IsAllowed(origin));
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://192.168.1.20:5173")]
    public void ProductionWithoutConfig_BlocksEverything(string origin)
    {
        var p = Policy(Environments.Production);

        Assert.False(p.AllowsLocalNetwork);
        Assert.False(p.IsAllowed(origin));
    }
}

public class GuestCancellationTokenServiceTests
{
    private readonly GuestCancellationTokenService _svc = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Token_ValidatesOnlyForItsIncident()
    {
        var id = Guid.NewGuid();
        var token = _svc.Create(id);

        Assert.True(_svc.Validate(id, token));
        Assert.False(_svc.Validate(Guid.NewGuid(), token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tampered-token")]
    public void InvalidToken_IsRejected(string? token)
    {
        Assert.False(_svc.Validate(Guid.NewGuid(), token));
    }

    [Fact]
    public void TokenFromAnotherKeyRing_IsRejected()
    {
        var id = Guid.NewGuid();
        var foreign = new GuestCancellationTokenService(new EphemeralDataProtectionProvider()).Create(id);

        Assert.False(_svc.Validate(id, foreign));
    }
}
