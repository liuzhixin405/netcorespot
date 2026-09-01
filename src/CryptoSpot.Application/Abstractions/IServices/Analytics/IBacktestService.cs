using CryptoSpot.Application.DTOs.Analytics;

namespace CryptoSpot.Application.Abstractions.Services.Analytics;

public interface IBacktestService
{
    Task<BacktestResultDto> RunAsync(long userId, BacktestRequestDto request, CancellationToken cancellationToken = default);
    Task<BacktestResultDto?> GetAsync(long userId, long id, CancellationToken cancellationToken = default);
    IReadOnlyList<StrategyDefinitionDto> GetStrategies();
}
