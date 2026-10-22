namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Hủy hiệu lực access token đang dùng của một tài khoản ngay lập tức
/// (khóa, đổi role, đổi trạm, xóa tài khoản) thay vì chờ token hết hạn.
/// </summary>
public interface IUserSessionInvalidator
{
    Task InvalidateAsync(Guid userId, string reason, CancellationToken cancellationToken = default);
}
