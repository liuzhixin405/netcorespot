using CryptoSpot.Application.Abstractions.Services.MarketData;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CryptoSpot.Infrastructure.MarketData;

/// <summary>
/// 行情数据源健康检查。应用刚启动、后台服务尚未开始连接时视为健康，
/// 避免启动探针在行情服务启动前误报。
/// </summary>
public sealed class MarketDataHealthCheck : IHealthCheck
{
    private readonly IMarketDataStreamProvider _provider;
    private readonly MarketDataFeedOptions _options;

    public MarketDataHealthCheck(
        IMarketDataStreamProvider provider,
        MarketDataFeedOptions options)
    {
        _provider = provider;
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_provider.HasStarted)
        {
            return Task.FromResult(HealthCheckResult.Healthy("market data stream not started yet"));
        }

        if (!_provider.IsConnected)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"{_provider.ProviderName} market data WebSocket is disconnected"));
        }

        var last = _provider.LastMessageAt;
        if (last is { } timestamp && DateTimeOffset.UtcNow - timestamp > _options.IdleTimeout * 2)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"{_provider.ProviderName} market data is stale since {timestamp:O}"));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{_provider.ProviderName} market data connected"));
    }
}
