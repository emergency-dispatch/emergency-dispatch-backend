namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Transaction đang giữ khóa dòng của một sự cố (SELECT ... FOR UPDATE).
/// Mọi SaveChanges trong lúc scope còn mở thuộc cùng transaction; dispose mà chưa Commit → rollback và nhả khóa.
/// </summary>
public interface IIncidentLockScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
