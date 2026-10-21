using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class SystemSettingRepository : ISystemSettingRepository
{
    private readonly ApplicationDbContext _context;

    public SystemSettingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SystemSetting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.SystemSettings
            .Include(s => s.UpdatedByUser)
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
    }

    public async Task<SystemSetting> UpsertAsync(string key, string valueJson, Guid? updatedByUserId, CancellationToken cancellationToken = default)
    {
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting == null)
        {
            setting = new SystemSetting { Key = key, ValueJson = valueJson, UpdatedByUserId = updatedByUserId };
            await _context.SystemSettings.AddAsync(setting, cancellationToken);
        }
        else
        {
            setting.ValueJson = valueJson;
            setting.UpdatedByUserId = updatedByUserId;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return setting;
    }
}
