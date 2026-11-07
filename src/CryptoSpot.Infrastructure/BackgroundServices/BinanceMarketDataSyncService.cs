using CryptoSpot.Application.Abstractions.Services.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices;

/// <summary>
/// 可选的 Binance REST 行情同步宿主。仅在 <c>Binance:Enabled=true</c> 时启动，
/// 默认关闭，避免与 OKX WebSocket 主源重复拉取。
/// </summary>
public sealed class BinanceMarketDataSyncService : BackgroundService
{
    private readonly IMarketDataProvider _provider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BinanceMarketDataSyncService> _logger;

    public BinanceMarketDataSyncService(
        IMarketDataProvider provider,
        IConfiguration configuration,
        ILogger<BinanceMarketDataSyncService> logger)
    {
        _provider = provider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Binance:Enabled", false))
        {
            _logger.LogInformation("Binance REST 行情同步已禁用（Binance:Enabled=false）");
            return;
        }

        _logger.LogInformation("Binance REST 行情同步已启用");

        try
        {
            await _provider.StartRealTimeDataSyncAsync();
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _provider.StopRealTimeDataSyncAsync();
        }
    }
}
