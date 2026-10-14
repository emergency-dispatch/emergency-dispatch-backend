namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Mức độ ùn tắc giao thông hoặc chướng ngại vật nội khu
/// </summary>
public enum TrafficCongestionLevel
{
    /// <summary>
    /// Ùn ứ nhẹ, xe di chuyển chậm
    /// </summary>
    Low = 0,

    /// <summary>
    /// Kẹt xe trung bình
    /// </summary>
    Moderate = 1,

    /// <summary>
    /// Kẹt xe nghiêm trọng, tốc độ di chuyển rất thấp
    /// </summary>
    Severe = 2,

    /// <summary>
    /// Tắc nghẽn hoàn toàn (đường bị phong tỏa, chướng ngại vật lớn, cây ngã, ngập nước...)
    /// </summary>
    Blocked = 3
}
