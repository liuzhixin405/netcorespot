using CryptoSpot.Application.Abstractions.Services.MarketData;
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
            _logger.LogInformation("🚀 BinanceService 正在启动...");
            
            try
            {
                // 启动数据同步
                await StartDataSyncAsync();
                _logger.LogInformation("✅ BinanceService 数据同步已启动");

                // 保持服务运行，定期检查状态
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                        
                        // 检查服务状态
                        await CheckAndRestartDataSyncAsync();
                        
                        _logger.LogInformation("🔄 BinanceService 运行正常，定期检查完成");
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消，退出循环
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ BinanceService 定期检查时出错");
                        // 出错后等待更长时间再重试
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 BinanceService 正在取消...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ BinanceService 执行时发生错误");
            }
            finally
            {
                // 停止数据同步
                await StopDataSyncAsync();
                _logger.LogInformation("🏁 BinanceService 已停止");
            }
        }

        private async Task StartDataSyncAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                _marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
                
                if (_marketDataProvider == null)
                {
                    _logger.LogError("❌ 无法解析 IMarketDataProvider");
                    return;
                }

                // 检查服务可用性
                var isAvailable = await _marketDataProvider.IsAvailableAsync();
                if (!isAvailable)
                {
                    _logger.LogWarning("⚠️ Market data provider 不可用");
                    return;
                }

                // 启动数据同步
                await _marketDataProvider.StartRealTimeDataSyncAsync();
                _logger.LogInformation("✅ Binance market data sync started");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 启动数据同步失败");
            }
        }

        private async Task CheckAndRestartDataSyncAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
                
                var isAvailable = await marketDataProvider.IsAvailableAsync();
                if (!isAvailable)
                {
                    _logger.LogWarning("⚠️ Market data provider 连接丢失，尝试重新启动");
                    await marketDataProvider.StartRealTimeDataSyncAsync();
                    _logger.LogInformation("🔄 Market data provider 重新启动成功");
                }
                else
                {
                    _logger.LogDebug("✅ Market data provider 连接正常");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 检查数据同步状态时出错");
            }
        }

        private async Task StopDataSyncAsync()
        {
            try
            {
                if (_marketDataProvider != null)
                {
                    await _marketDataProvider.StopRealTimeDataSyncAsync();
                    _logger.LogInformation("✅ Binance market data sync stopped");
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("⚠️ ServiceProvider disposed during Binance service shutdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 停止 Binance market data sync 时出错");
            }
        }
    }
}
