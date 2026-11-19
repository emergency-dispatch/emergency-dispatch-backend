namespace EmergencyDispatch.Application.Services;

public static class StationTransferMessages
{
    public const string Acknowledged = "Đã xác nhận trạm tiếp nhận sự cố.";
    public const string Changed = "Sự cố đã đổi trạm hoặc trạng thái. Vui lòng tải lại.";
    public const string Closed = "Không thể tiếp nhận hoặc chuyển trạm cho sự cố đã đóng hoặc đang được gom.";
    public const string NeedOperator = "Trạm nhận phải có ít nhất một Operator đang hoạt động.";
    public const string ActiveWork = "Sự cố đã có lệnh cứu hộ. Chỉ Admin được xác nhận bàn giao các nhiệm vụ đang hoạt động.";
    public const string Locked = "Sự cố đang được Operator khác xử lý.";
    public const string Transferred = "Đã chuyển trạm phụ trách sự cố.";
    public const string NoOperator = "Trạm phụ trách không có Operator đang hoạt động.";
    public const string NoResponse = "Trạm phụ trách quá hạn xác nhận tiếp nhận.";
    public const string Unavailable = "Trạm phụ trách không còn hoạt động.";
    public const string NoTarget = "Không có trạm thay thế phù hợp. Admin cần xử lý sự cố.";
    public const string Limit = "Đã đạt giới hạn chuyển trạm tự động. Admin cần tiếp quản.";
    public const string AlertTitle = "CẦN ADMIN XỬ LÝ CHUYỂN TRẠM";
    public const string TransferTitle = "SỰ CỐ ĐÃ CHUYỂN TRẠM";
    public const string TransferBody = "Sự cố {0} được chuyển từ {1} sang {2}.";
    public const string NoStationName = "chưa có trạm";
    public const string SettingsSaved = "Đã lưu cấu hình chuyển trạm tự động.";
    public const string SettingsInvalid = "Cấu hình chuyển trạm không hợp lệ.";
    public const string InvalidMultiplier = "Hệ số thời gian tiếp nhận phải từ 1 đến 10.";
    public const string InvalidLimit = "Số lần chuyển trạm tự động phải từ 1 đến 5.";
}
