using CryptoSpot.Core.Interfaces.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.ExternalServices
{
    /// <summary>
    /// Binance后台服务 - 纯粹的后台服务，只负责生命周期管理
    /// </summary>
    public class BinanceService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<BinanceService> _logger;
        private IMarketDataProvider? _marketDataProvider;

        public BinanceService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<BinanceService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            
            try
            {
                // 获取市场数据提供者服务
                _marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
                
                if (_marketDataProvider == null)
                {
                    _logger.LogError("Failed to resolve IMarketDataProvider");
                    return;
                }

                // 检查服务可用性
                var isAvailable = await _marketDataProvider.IsAvailableAsync();
                if (!isAvailable)
                {
                    _logger.LogWarning("Market data provider is not available");
                    return;
                }

                // 启动数据同步
                await _marketDataProvider.StartRealTimeDataSyncAsync();
                _logger.LogInformation("Binance market data sync started");

                // 保持服务运行直到收到取消请求
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Binance service is being cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Binance service execution");
            }
            finally
            {
                // 停止数据同步
                if (_marketDataProvider != null)
                {
                    try
                    {
                        await _marketDataProvider.StopRealTimeDataSyncAsync();
                        _logger.LogInformation("Binance market data sync stopped");
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogWarning("ServiceProvider disposed during Binance service shutdown");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error stopping Binance market data sync");
                    }
                }
            }
        }
    }
}
