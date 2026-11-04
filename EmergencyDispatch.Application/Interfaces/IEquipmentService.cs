using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Equipment;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Quản lý thiết bị cứu hộ của trạm (Available / Maintenance).
/// Admin: mọi trạm. Operator: trạm mình phụ trách. RescueStaff: xem và đổi trạng thái thiết bị trạm mình.
/// </summary>
public interface IEquipmentService
{
    Task<ApiResponseDto<List<EquipmentDto>>> GetListAsync(EquipmentFilterDto filter, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<EquipmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<EquipmentDto>> CreateAsync(CreateEquipmentDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<EquipmentDto>> UpdateAsync(Guid id, UpdateEquipmentDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<EquipmentDto>> UpdateStatusAsync(Guid id, UpdateEquipmentStatusDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
