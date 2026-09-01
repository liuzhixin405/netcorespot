using CryptoSpot.Application.DTOs.Risk;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.Risk;

public interface IRiskEventService
{
    Task<RiskEventDto?> RecordIfNewAsync(RiskEventInput input, TimeSpan deduplicationWindow, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RiskEventDto>> ListAsync(long userId, RiskEventStatus? status = null, CancellationToken cancellationToken = default);
    Task<RiskDashboardDto> GetDashboardAsync(long userId, CancellationToken cancellationToken = default);
    Task<RiskEventDto> HandleAsync(long userId, long eventId, RiskEventStatus status, CancellationToken cancellationToken = default);
}
