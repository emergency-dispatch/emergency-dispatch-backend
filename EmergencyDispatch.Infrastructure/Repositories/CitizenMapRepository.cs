using System.Security.Cryptography;
using System.Text.Json;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Domain.Models;
using EmergencyDispatch.Domain.Policies;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public sealed class CitizenMapRepository(ApplicationDbContext db) : ICitizenMapRepository
{
    private IQueryable<Incident> Visible() => db.Incidents.AsNoTracking()
        .Where(CitizenIncidentVisibility.ConfirmedActiveSince(CitizenIncidentVisibility.AllTimeUtc))
        .Where(i => i.Latitude >= -90 && i.Latitude <= 90 && i.Longitude >= -180 && i.Longitude <= 180);

    private static IQueryable<CitizenMapIncident> Project(IQueryable<Incident> query) => query.Select(i => new CitizenMapIncident(
        i.Id, i.Latitude, i.Longitude, i.LocationAddress, i.EventType, i.Severity, i.Status, i.CreatedAt, i.UpdatedAt));

    public async Task<(IReadOnlyList<CitizenMapIncident> Items, int Total)> GetAsync(CitizenMapBounds? bounds, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var query = Visible();
        if (bounds != null)
        {
            query = query.Where(i => i.Latitude >= bounds.South && i.Latitude <= bounds.North);
            // A west bound greater than east crosses the international date line.
            query = bounds.West <= bounds.East
                ? query.Where(i => i.Longitude >= bounds.West && i.Longitude <= bounds.East)
                : query.Where(i => i.Longitude >= bounds.West || i.Longitude <= bounds.East);
        }
        var count = await query.CountAsync(ct);
        var rows = await Project(query.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id))
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (rows, count);
    }

    public Task<CitizenMapIncident?> GetAsync(Guid id, CancellationToken ct = default)
        => Project(Visible().Where(i => i.Id == id)).FirstOrDefaultAsync(ct);

    public Task<CitizenMapIncident?> GetByAlertAsync(Guid publicAlertId, CancellationToken ct = default)
        => Project(Visible().Where(i => db.CitizenIncidentAlerts.Any(a => a.Id == publicAlertId
            && a.IncidentId == i.Id && !a.IsDeleted))).FirstOrDefaultAsync(ct);

    public async Task<string> GetRevisionAsync(CancellationToken ct = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await foreach (var row in Project(Visible().OrderBy(i => i.Id)).AsAsyncEnumerable().WithCancellation(ct))
        {
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(row));
            hash.AppendData(new byte[] { 10 });
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
