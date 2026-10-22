using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

public class IncidentFilterDto : PaginationParamsDto
{
    public IncidentStatus? Status { get; set; }

    private int _pageSize = 10;

    /// <summary>
    /// Tối đa 200 để màn hình điều phối lấy đủ sự cố đang hoạt động trong 1 lần gọi (các danh sách khác tối đa 50)
    /// </summary>
    public new int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 200);
    }

    /// <summary>
    /// Nhiều trạng thái, phân tách bằng dấu phẩy (VD "Verified,Dispatched,InProgress,Escalated"). Có giá trị thì bỏ qua Status.
    /// </summary>
    public string? Statuses { get; set; }

    /// <summary>
    /// Đọc Statuses; trả null nếu trống, ném ArgumentException nếu có giá trị không hợp lệ
    /// </summary>
    public IReadOnlyCollection<IncidentStatus>? ParseStatuses()
    {
        if (string.IsNullOrWhiteSpace(Statuses)) return null;
        var result = new List<IncidentStatus>();
        foreach (var part in Statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<IncidentStatus>(part, true, out var s) || !Enum.IsDefined(s))
            {
                throw new ArgumentException(LocalizationHelper.Localize("Trạng thái \"{0}\" không hợp lệ.", part));
            }
            if (!result.Contains(s)) result.Add(s);
        }
        return result;
    }
    public SeverityLevel? Severity { get; set; }
    public string? SearchTerm { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
