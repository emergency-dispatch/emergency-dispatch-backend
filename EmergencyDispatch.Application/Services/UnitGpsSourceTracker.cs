namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Kết quả xét một mẫu GPS nhân viên cứu hộ gửi cho xe
/// </summary>
public enum GpsSampleDecision
{
    /// <summary>Nhận mẫu, ghi vị trí xe</summary>
    Accepted,

    /// <summary>Bỏ qua: xe đang lấy GPS từ thành viên khác của tổ (nguồn chính còn gửi đều)</summary>
    NotPrimarySource,

    /// <summary>Bỏ qua: tọa độ nhảy quá xa so với mẫu trước (vận tốc suy ra vượt ngưỡng)</summary>
    ImplausibleJump
}

/// <summary>
/// Mỗi xe chỉ lấy GPS từ một thiết bị tại một thời điểm. Cả tổ ngồi trên xe đều mở màn hình nhiệm vụ nên đều gửi vị trí;
/// không chọn nguồn thì vị trí xe nhảy qua lại giữa các máy. Quy tắc:
/// người nhận lệnh luôn là nguồn ưu tiên; thành viên khác chỉ được làm nguồn khi nguồn hiện tại im lặng quá
/// <see cref="SourceTimeout"/> (máy hết pin, mất mạng); mẫu có vận tốc suy ra vượt <see cref="MaxPlausibleSpeedKmh"/> bị bỏ.
/// Trạng thái giữ trong bộ nhớ theo từng lượt phân công (singleton), khởi động lại server thì mẫu đầu tiên được nhận lại.
/// </summary>
public class UnitGpsSourceTracker
{
    public static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(30);
    public const double MaxPlausibleSpeedKmh = 200;

    /// <summary>
    /// Sai số GPS thông thường: dịch chuyển trong bán kính này không xét vận tốc
    /// </summary>
    public const double JitterToleranceKm = 0.3;

    private sealed class Entry
    {
        public Guid AssignmentId;
        public Guid SourceUserId;
        public DateTime SourceSeenAt;
        public double Lat;
        public double Lng;
        public DateTime SampleAt;
    }

    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly object _gate = new();

    public GpsSampleDecision Evaluate(Guid unitId, Guid assignmentId, Guid senderUserId, Guid? acceptedByUserId,
        double lat, double lng, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(unitId, out var entry) || entry.AssignmentId != assignmentId)
            {
                // Lượt mới (hoặc server vừa khởi động): mẫu đầu tiên được nhận, người gửi thành nguồn
                _entries[unitId] = new Entry
                {
                    AssignmentId = assignmentId,
                    SourceUserId = senderUserId,
                    SourceSeenAt = nowUtc,
                    Lat = lat,
                    Lng = lng,
                    SampleAt = nowUtc
                };
                return GpsSampleDecision.Accepted;
            }

            var sourceFresh = nowUtc - entry.SourceSeenAt <= SourceTimeout;
            var isSource = entry.SourceUserId == senderUserId;
            var isAccepter = acceptedByUserId.HasValue && acceptedByUserId.Value == senderUserId;
            if (!isSource && sourceFresh && !isAccepter)
            {
                return GpsSampleDecision.NotPrimarySource;
            }

            var distanceKm = HaversineKm(entry.Lat, entry.Lng, lat, lng);
            var hours = (nowUtc - entry.SampleAt).TotalHours;
            if (distanceKm > JitterToleranceKm && (hours <= 0 || distanceKm / hours > MaxPlausibleSpeedKmh))
            {
                // Mẫu nhảy không tính là "còn gửi": nguồn chỉ gửi tọa độ sai thì thành viên khác vẫn thay được sau SourceTimeout
                return GpsSampleDecision.ImplausibleJump;
            }

            entry.SourceUserId = senderUserId;
            entry.SourceSeenAt = nowUtc;
            entry.Lat = lat;
            entry.Lng = lng;
            entry.SampleAt = nowUtc;
            return GpsSampleDecision.Accepted;
        }
    }

    /// <summary>
    /// Xe hết nhiệm vụ (về trạm): xóa nguồn và mẫu cũ để lượt sau bắt đầu lại
    /// </summary>
    public void Reset(Guid unitId)
    {
        lock (_gate)
        {
            _entries.Remove(unitId);
        }
    }

    internal static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371.0;
        static double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
