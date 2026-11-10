using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Quy tắc tự gom báo cáo trùng (BR-07, phương án "gom trước, Operator / người báo tách được"):
/// lớp 1 = gần nhau + cùng khung thời gian + cùng loại (nếu biết). Hàm thuần, không truy cập DB.
/// </summary>
public static class IncidentLinking
{
    /// <summary>
    /// Báo cáo cách sự cố gốc xa hơn tỉ lệ này của bán kính gom thì đánh dấu lệch để Operator kiểm tra
    /// </summary>
    public const double OutlierRadiusRatio = 0.7;

    private static readonly string[] FireWords = { "cháy", "hỏa hoạn", "hoả hoạn", "nổ", "khói", "bốc lửa", "fire", "smoke", "explosion" };
    private static readonly string[] AccidentWords = { "tai nạn", "va chạm", "đâm", "tông", "lật xe", "accident", "crash", "collision" };

    public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        => StationAssignment.DistanceKm(lat1, lng1, lat2, lng2) * 1000.0;

    /// <summary>
    /// Khung tọa độ bao quanh điểm (để DB lọc thô trước khi tính khoảng cách chính xác)
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLng, double MaxLng) BoundingBox(double lat, double lng, double radiusMeters)
    {
        var dLat = radiusMeters / 111_320.0;
        var dLng = radiusMeters / (111_320.0 * Math.Max(0.01, Math.Cos(lat * Math.PI / 180.0)));
        return (lat - dLat, lat + dLat, lng - dLng, lng + dLng);
    }

    /// <summary>
    /// Loại sự cố suy ra từ mô tả; null nếu không có từ khóa hoặc có cả hai nhóm
    /// </summary>
    public static IncidentEventType? KeywordType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        var fire = FireWords.Any(t.Contains);
        var accident = AccidentWords.Any(t.Contains);
        return fire == accident ? null : fire ? IncidentEventType.FireExplosion : IncidentEventType.TrafficAccident;
    }

    /// <summary>
    /// Loại sự cố tốt nhất đang biết: Operator xác nhận → AI thành công → người báo tự chọn → từ khóa mô tả
    /// </summary>
    public static IncidentEventType? KnownType(Incident incident)
    {
        if (incident.EventType != IncidentEventType.None) return incident.EventType;
        if (incident.AiClassification is { IsSuccess: true, EventType: not IncidentEventType.None } ai) return ai.EventType;
        if (incident.ReportedEventType is { } reported && reported != IncidentEventType.None) return reported;
        return KeywordType($"{incident.Title} {incident.Description}");
    }

    /// <summary>
    /// Hai loại tương thích khi một bên chưa biết hoặc hai bên giống nhau
    /// </summary>
    public static bool Compatible(IncidentEventType? a, IncidentEventType? b) => a == null || b == null || a == b;

    /// <summary>
    /// Nhóm "Khác" gồm nhiều loại chi tiết (đuối nước, sập công trình...): hai bên đều đã chọn loại chi tiết cụ thể
    /// (không phải mã chung "Other") mà khác nhau thì không gom
    /// </summary>
    public static bool DetailedTypeCompatible(Incident candidate, string? newReportTypeCode)
    {
        const string generic = nameof(IncidentEventType.Other);
        if (string.IsNullOrWhiteSpace(newReportTypeCode) || newReportTypeCode == generic) return true;
        if (KnownType(candidate) != IncidentEventType.Other) return true;
        var candidateCode = DispatchScoring.IncidentTypeCodeFor(candidate);
        return string.IsNullOrWhiteSpace(candidateCode) || candidateCode == generic || candidateCode == newReportTypeCode;
    }

    /// <summary>
    /// Chọn sự cố gốc gần nhất thỏa: trong bán kính, có báo cáo từ windowStartUtc, tương thích loại. null = không gom
    /// </summary>
    public static (Incident Parent, double DistanceMeters)? PickParent(
        IEnumerable<Incident> candidates,
        double lat,
        double lng,
        IncidentEventType? newReportType,
        double radiusMeters,
        DateTime windowStartUtc,
        string? newReportTypeCode = null)
    {
        return candidates
            .Where(c => c.Status != IncidentStatus.Linked && !c.IsDeleted)
            .Where(c => (c.LatestReportAt ?? c.CreatedAt) >= windowStartUtc)
            .Where(c => Compatible(KnownType(c), newReportType))
            .Where(c => newReportType != IncidentEventType.Other || DetailedTypeCompatible(c, newReportTypeCode))
            .Select(c => (Parent: c, DistanceMeters: DistanceMeters(lat, lng, c.Latitude, c.Longitude)))
            .Where(x => x.DistanceMeters <= radiusMeters)
            .OrderBy(x => x.DistanceMeters)
            .Select(x => ((Incident, double)?)(x.Parent, x.DistanceMeters))
            .FirstOrDefault();
    }

    /// <summary>
    /// Báo cáo được gom có dấu hiệu không cùng vụ: ở rìa bán kính, hoặc loại tự chọn / YOLO khác loại của sự cố gốc
    /// </summary>
    public static bool IsOutlier(Incident report, IncidentEventType? parentType, double radiusMeters)
    {
        if (report.LinkDistanceMeters is { } d && d > radiusMeters * OutlierRadiusRatio) return true;
        if (!Compatible(parentType, report.ReportedEventType)) return true;
        return !Compatible(parentType, report.QuickCheckEventType);
    }
}
