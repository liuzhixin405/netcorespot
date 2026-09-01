using CryptoSpot.Application.DTOs.Analytics;

namespace CryptoSpot.Application.Abstractions.Services.Analytics;

public interface ISignalService
{
    Task<SignalDto> GetLatestAsync(string symbol, string interval, StrategyType strategy,
        IReadOnlyDictionary<string, decimal>? parameters = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SignalDto>> GetHistoryAsync(string symbol, string interval, StrategyType strategy,
        int limit = 100, IReadOnlyDictionary<string, decimal>? parameters = null, CancellationToken cancellationToken = default);
}
