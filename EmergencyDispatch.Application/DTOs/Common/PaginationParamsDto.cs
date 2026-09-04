namespace EmergencyDispatch.Application.DTOs.Common;

/// <summary>
/// Tham số phân trang cơ bản
/// </summary>
public class PaginationParamsDto
{
    private const int MaxPageSize = 50;
    private int _pageSize = 10;

    /// <summary>
    /// Số thứ tự trang (bắt đầu từ 1)
    /// </summary>
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// Số phần tử trên một trang (tối đa 50)
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > MaxPageSize ? MaxPageSize : (value < 1 ? 1 : value);
    }
}
