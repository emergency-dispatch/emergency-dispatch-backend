using EmergencyDispatch.Domain.Entities;
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
    /// Mã loại sự cố dùng để tra quy tắc loại xe: loại chi tiết đã biết → nhóm (Operator xác nhận → AI → người báo).
    /// Mã nhóm trùng mã loại hệ thống (FireExplosion / TrafficAccident / Other). null = chưa biết loại
    /// </summary>
    public static string? IncidentTypeCodeFor(Incident incident)
    {
        if (!string.IsNullOrWhiteSpace(incident.IncidentTypeCode)) return incident.IncidentTypeCode;
        var category = incident.EventType != IncidentEventType.None
            ? incident.EventType
            : incident.AiClassification is { IsSuccess: true, EventType: not IncidentEventType.None } ai
                ? ai.EventType
                : incident.ReportedEventType ?? IncidentEventType.None;
        var reportedCode = string.IsNullOrWhiteSpace(incident.ReportedIncidentTypeCode) ? null : incident.ReportedIncidentTypeCode;
        if (category == IncidentEventType.None) return reportedCode;
        // Loại chi tiết người báo chọn còn đúng nhóm hiện tại → giữ loại chi tiết (VD "Đuối nước" thuộc Other)
        return reportedCode != null && incident.ReportedEventType == category ? reportedCode : category.ToString();
    }

    /// <summary>
    /// Quy tắc áp dụng ở mức độ này (sự cố chưa phân loại tính như Level 1), xếp theo ưu tiên.
    /// Không quy tắc nào đạt mức → dùng các quy tắc ở mức thấp nhất của loại đó
    /// </summary>
    public static List<IncidentTypeUnitRule> ApplicableRules(IReadOnlyList<IncidentTypeUnitRule> rules, SeverityLevel severity)
    {
        if (rules.Count == 0) return new List<IncidentTypeUnitRule>();
        var level = severity == SeverityLevel.Unclassified ? SeverityLevel.Level1 : severity;
        var applicable = rules.Where(r => r.MinSeverity <= level).ToList();
        if (applicable.Count == 0)
        {
            var lowest = rules.Min(r => r.MinSeverity);
            applicable = rules.Where(r => r.MinSeverity == lowest).ToList();
        }
        return applicable.OrderBy(r => r.Priority).ThenBy(r => r.MinSeverity).ToList();
    }

    /// <summary>
    /// BR-13: loại xe phù hợp. Có quy tắc của loại sự cố (bảng IncidentTypeUnitRules) → theo quy tắc;
    /// chưa có quy tắc → luật dự phòng theo nhóm sự cố, tag AI và từ khóa mô tả. null = cho phép mọi loại xe
    /// </summary>
    public static IReadOnlySet<string>? MatchingUnitTypes(
        IReadOnlyList<IncidentTypeUnitRule>? typeRules,
        IncidentEventType eventType,
        IEnumerable<string> hazardTags,
        string titleAndDescription,
        SeverityLevel severity)
    {
        if (typeRules is { Count: > 0 })
        {
            return ApplicableRules(typeRules, severity).Select(r => r.UnitTypeCode).ToHashSet(StringComparer.Ordinal);
        }
        return MatchingUnitTypes(eventType, hazardTags, titleAndDescription, severity);
    }

    /// <summary>
    /// Luật dự phòng (trước khi có danh mục quy tắc). Trả về null khi chưa xác định được nhánh (cho phép mọi loại xe).
    /// </summary>
    public static IReadOnlySet<string>? MatchingUnitTypes(
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
            return new HashSet<string> { RescueUnitType.RescueBoat };
        }

        if (isFire)
        {
            var types = new HashSet<string>
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
            var types = new HashSet<string>
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
