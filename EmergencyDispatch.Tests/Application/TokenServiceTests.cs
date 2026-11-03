using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using Microsoft.IdentityModel.Tokens;

namespace EmergencyDispatch.Tests.Application;

public class TokenServiceTests
{
    private const string Key = "unit-test-signing-key-0123456789-abcdefghijklmnop";

    private static TokenService Svc(string? minutes = null, string? hours = null, string? key = Key)
        => new(TestHelpers.Config(("JwtSettings:Key", key), ("JwtSettings:Issuer", "iss"), ("JwtSettings:Audience", "aud"),
            ("JwtSettings:ExpireMinutes", minutes), ("JwtSettings:ExpireHours", hours)));

    private static User NewUser(Guid? station = null) => new()
    {
        FullName = "Nguyễn Văn A",
        Email = "a@test.local",
        Role = UserRole.Operator,
        StationId = station
    };

    [Fact]
    public void GenerateAccessToken_ContainsIdentityRoleAndStationClaims()
    {
        var station = Guid.NewGuid();
        var user = NewUser(station);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Svc().GenerateAccessToken(user));

        Assert.Equal("iss", jwt.Issuer);
        Assert.Contains("aud", jwt.Audiences);
        Assert.Contains(jwt.Claims, c => c.Value == user.Id.ToString());
        Assert.Contains(jwt.Claims, c => c.Value == "Operator");
        Assert.Contains(jwt.Claims, c => c.Type == "StationId" && c.Value == station.ToString());
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public void GenerateAccessToken_WithoutStation_HasNoStationClaim()
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Svc().GenerateAccessToken(NewUser()));

        Assert.DoesNotContain(jwt.Claims, c => c.Type == "StationId");
    }

    [Theory]
    [InlineData("30", null, 30)]
    [InlineData(null, "2", 120)]
    [InlineData("0", null, 60)]
    [InlineData(null, null, 60)]
    public void GenerateAccessToken_LifetimeFromConfig(string? minutes, string? hours, int expectedMinutes)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Svc(minutes, hours).GenerateAccessToken(NewUser()));

        var lifetime = jwt.ValidTo - DateTime.UtcNow;
        Assert.InRange(lifetime.TotalMinutes, expectedMinutes - 1, expectedMinutes + 1);
    }

    [Fact]
    public void GenerateAccessToken_MissingKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Svc(key: null).GenerateAccessToken(NewUser()));
    }

    [Fact]
    public void GenerateRefreshToken_IsRandom64BytesAndValidSevenDays()
    {
        var svc = Svc();
        var id = Guid.NewGuid();

        var a = svc.GenerateRefreshToken(id);
        var b = svc.GenerateRefreshToken(id);

        Assert.Equal(id, a.UserId);
        Assert.Equal(64, Convert.FromBase64String(a.Token).Length);
        Assert.NotEqual(a.Token, b.Token);
        Assert.InRange((a.ExpiresAt - DateTime.UtcNow).TotalDays, 6.99, 7.01);
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_ReadsOwnToken()
    {
        var svc = Svc();
        var user = NewUser();

        var principal = svc.GetPrincipalFromExpiredToken(svc.GenerateAccessToken(user))!;

        Assert.Equal(user.Id.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_RejectsTokenSignedWithOtherKey()
    {
        var foreign = Svc(key: "another-signing-key-0123456789-abcdefghijklmnopqr").GenerateAccessToken(NewUser());

        Assert.ThrowsAny<SecurityTokenException>(() => Svc().GetPrincipalFromExpiredToken(foreign));
    }
}
