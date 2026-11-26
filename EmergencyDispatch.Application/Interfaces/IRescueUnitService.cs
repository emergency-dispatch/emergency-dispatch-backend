using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

public interface IRescueUnitService
{
    Task<IReadOnlyList<RescueUnitResponseDto>> GetAllAsync();
    Task<RescueUnitResponseDto> GetByIdAsync(Guid id);
    Task<IReadOnlyList<RescueUnitResponseDto>> GetByStationIdAsync(Guid stationId);
    Task<IReadOnlyList<RescueUnitResponseDto>> GetAvailableUnitsAsync(string? unitType = null);
    Task<RescueUnitResponseDto> CreateAsync(CreateRescueUnitDto dto);
    Task<RescueUnitResponseDto> UpdateAsync(Guid id, UpdateRescueUnitDto dto);
    Task<RescueUnitResponseDto> UpdateLocationAsync(Guid id, UpdateRescueUnitLocationDto dto);

    /// <summary>
    /// Ghi GPS của xe. Nhân viên cứu hộ: chỉ thành viên tổ đang giữ lượt của xe được gửi; mỗi xe một nguồn GPS
    /// (người nhận lệnh ưu tiên); mẫu nhảy xa bất thường bị bỏ. Operator/Admin (giả lập GPS) không bị các ràng buộc này
    /// </summary>
    Task<RescueUnitResponseDto> UpdateLocationAsync(Guid id, UpdateRescueUnitLocationDto dto, GpsSender sender);
    Task<RescueUnitResponseDto> UpdateStatusAsync(Guid id, UpdateRescueUnitStatusDto dto);
    Task DeleteAsync(Guid id);
}
