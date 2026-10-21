using System.Security.Cryptography;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace EmergencyDispatch.API.Services;

public sealed class GuestCancellationTokenService : IGuestCancellationTokenService
{
    private readonly ITimeLimitedDataProtector _protector;

    public GuestCancellationTokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("EmergencyDispatch.GuestCancellation.v1").ToTimeLimitedDataProtector();
    }

    public string Create(Guid incidentId) => _protector.Protect(incidentId.ToString("D"), TimeSpan.FromHours(24));

    public bool Validate(Guid incidentId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            return _protector.Unprotect(token) == incidentId.ToString("D");
        }
        catch (CryptographicException) { return false; }
    }
}
