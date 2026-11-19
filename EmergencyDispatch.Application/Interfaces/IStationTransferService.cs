using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Application.Interfaces;

public interface IStationTransferService
{
    Task InitializeAsync(Incident incident, bool readyForOperator, Guid? acknowledgedBy = null, CancellationToken cancellationToken = default);
    Task MarkReadyForOperatorAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<StationAcknowledgementDto>> AcknowledgeAsync(Guid incidentId, Guid actorUserId, int? expectedVersion, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<IncidentResponseDto>> TransferAsync(Guid incidentId, TransferIncidentDto dto, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<List<IncidentStationTransferDto>>> GetHistoryAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default);
}
