using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Các hàm tính toán thuần (không truy cập DB) của động cơ gợi ý điều phối (FR-OP-04, RQ2)
/// </summary>
public static class DispatchScoring
{
    /// <summary>
    /// Số nhiệm vụ đang chạy của trạm được coi là "đầy tải" khi chuẩn hóa về 0–1
    /// </summary>
    public const int MissionsSaturation = 3;

    /// <summary>
    /// Các nấc bán kính tìm xe (BR-12). Sự cố đã leo thang: nhân hệ số mở rộng, không vượt bán kính tối đa.
    /// </summary>
    public static IReadOnlyList<double> BuildSearchRadii(
        IReadOnlyList<double> stepsKm,
        bool escalated,
        double radiusMultiplier,
        double maxRadiusKm)
    {
        var steps = stepsKm.Where(r => r > 0).OrderBy(r => r).ToList();
        if (steps.Count == 0)
        {
            steps.Add(maxRadiusKm);
        }

        if (!escalated)
        {
            return steps;
        }

        return steps
            .Select(r => Math.Min(maxRadiusKm, r * radiusMultiplier))
            .Distinct()
            .OrderBy(r => r)
            .ToList();
    }

    /// <summary>
    /// Chuẩn hóa số nhiệm vụ đang chạy của trạm về khoảng 0–1
    /// </summary>
    public static double NormalizeActiveMissions(int stationActiveMissions)
        => Math.Min(1.0, stationActiveMissions / (double)MissionsSaturation);

    /// <summary>
    /// BR-11: Cost(u) = w1 · D/R + w2 · LoadRatio(S) + w3 · ActiveMissions(S). Cost càng thấp càng ưu tiên.
    /// </summary>
    public static double LoadAwareCost(
        double distanceKm,
        double radiusKm,
        double stationLoad,
        int stationActiveMissions,
        LoadAwareWeightsDto weights)
    {
        var distanceNorm = radiusKm > 0 ? Math.Min(1.0, distanceKm / radiusKm) : 1.0;
        var load = Math.Clamp(stationLoad, 0.0, 1.0);

        return weights.Distance * distanceNorm
               + weights.StationLoad * load
               + weights.ActiveMissions * NormalizeActiveMissions(stationActiveMissions);
    }

    /// <summary>
    /// BR-13: Khớp loại xe theo nhánh sự cố. Trả về null khi chưa xác định được nhánh (cho phép mọi loại xe).
    /// </summary>
    public static IReadOnlySet<RescueUnitType>? MatchingUnitTypes(
        IncidentEventType eventType,
        IEnumerable<string> hazardTags,
        string titleAndDescription,
        SeverityLevel severity)
    {
        var tags = string.Join(' ', hazardTags).ToLowerInvariant();
        var text = titleAndDescription.ToLowerInvariant();

        var isWater = tags.Contains("flood") || tags.Contains("drown") || text.Contains("đuối nước") || text.Contains("ngập");
        var isFire = eventType == IncidentEventType.FireExplosion
                     || tags.Contains("fire") || tags.Contains("smoke") || tags.Contains("explosion")
                     || text.Contains("cháy") || text.Contains("hỏa hoạn") || text.Contains("nổ");
        var isAccident = eventType == IncidentEventType.TrafficAccident
                         || tags.Contains("accident") || tags.Contains("crash") || tags.Contains("collision")
                         || text.Contains("tai nạn") || text.Contains("va chạm");

        if (isWater && !isFire && !isAccident)
        {
            return new HashSet<RescueUnitType> { RescueUnitType.RescueBoat };
        }

        if (isFire)
        {
            var types = new HashSet<RescueUnitType>
            {
                RescueUnitType.FireTruck,
                RescueUnitType.LadderTruck,
                RescueUnitType.HeavyRescueVehicle
            };
            // Level 3 cháy nổ: tự động bổ sung xe cấp cứu 115
            if (severity == SeverityLevel.Level3) types.Add(RescueUnitType.Ambulance);
            return types;
        }

        if (isAccident)
        {
            var types = new HashSet<RescueUnitType>
            {
                RescueUnitType.Ambulance,
                RescueUnitType.HeavyRescueVehicle
            };
            // Level 3 tai nạn có thể kèm cháy xe / rò rỉ nhiên liệu
            if (severity == SeverityLevel.Level3) types.Add(RescueUnitType.FireTruck);
            return types;
        }

        return null;
    }
}
