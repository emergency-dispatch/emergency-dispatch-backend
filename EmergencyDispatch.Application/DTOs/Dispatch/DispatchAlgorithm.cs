namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Thuật toán đề xuất điều phối phương tiện cứu hộ (Nghiên cứu RQ2)
/// </summary>
public enum DispatchAlgorithm
{
    /// <summary>
    /// Heuristic 1: Đơn vị gần nhất đang sẵn sàng (Nearest-Available)
    /// </summary>
    NearestAvailable = 0,

    /// <summary>
    /// Heuristic 2: Điều phối cân bằng tải (Load-Aware Dispatch)
    /// </summary>
    LoadAware = 1
}
