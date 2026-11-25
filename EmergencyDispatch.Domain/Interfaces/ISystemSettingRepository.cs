using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

public interface ISystemSettingRepository
{
    Task<SystemSetting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Atomically creates a default without overwriting an existing setting.</summary>
    Task<SystemSetting> GetOrCreateAsync(string key, string initialValueJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo mới hoặc cập nhật giá trị theo khóa (đã SaveChanges)
    /// </summary>
    Task<SystemSetting> UpsertAsync(string key, string valueJson, Guid? updatedByUserId, CancellationToken cancellationToken = default);
}
