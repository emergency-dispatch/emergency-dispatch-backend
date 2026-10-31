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

/// <summary>
/// Trạng thái đánh giá và xác thực ùn tắc giao thông tại hiện trường sự cố
/// Tách bạch giữa nhận định ban đầu của AI và quyết định xác thực của Điều phối viên (Human-in-the-loop).
/// </summary>
public enum TrafficJamStatus
{
    /// <summary>
    /// 0: Cả hai mô hình AI đều không phát hiện dấu hiệu ùn tắc trên ảnh
    /// </summary>
    NoEvidence = 0,

    /// <summary>
    /// 1: AI ghi nhận dấu hiệu nghi ngờ ùn tắc (chỉ 1 AI báo hoặc cả 2 AI đồng thuận)
    /// </summary>
    Suspected = 1,

    /// <summary>
    /// 2: Hai mô hình AI bất đồng ý kiến (một bên báo có dấu hiệu, một bên không phát hiện)
    /// </summary>
    Disputed = 2,

    /// <summary>
    /// 3: Điều phối viên (con người) đã kiểm tra và XÁC NHẬN có ùn tắc giao thông
    /// </summary>
    Confirmed = 3,

    /// <summary>
    /// 4: Điều phối viên (con người) đã kiểm tra và BÁC BỎ (đường lưu thông bình thường)
    /// </summary>
    Dismissed = 4,

    /// <summary>
    /// 5: Chưa thể đánh giá (lỗi kỹ thuật, thiếu ảnh hoặc thiếu nguồn đối chiếu)
    /// </summary>
    Unverified = 5
}
