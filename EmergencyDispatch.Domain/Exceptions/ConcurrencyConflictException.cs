namespace EmergencyDispatch.Domain.Exceptions;

/// <summary>
/// Dữ liệu đã bị người khác (hoặc worker) thay đổi kể từ lúc đọc ra — lưu sẽ ghi đè thay đổi đó.
/// Tầng Infrastructure ném ra thay cho DbUpdateConcurrencyException để Application không phụ thuộc EF Core.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string entityName, Exception? innerException = null)
        : base($"Concurrency conflict on {entityName}.", innerException)
    {
        EntityName = entityName;
    }

    /// <summary>
    /// Tên entity bị xung đột (Incident, DispatchAssignment)
    /// </summary>
    public string EntityName { get; }
}
