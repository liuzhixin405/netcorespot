using CryptoSpot.Core.Interfaces.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.API.Services
{
    /// <summary>
    /// 市场数据同步后台服务
    /// 负责启动和停止外部数据源的实时数据同步
    /// </summary>
    public class MarketDataSyncService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<MarketDataSyncService> _logger;
        private readonly List<IMarketDataProvider> _providers = new();

        public MarketDataSyncService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<MarketDataSyncService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MarketDataSyncService 正在启动...");

            try
            {
                // 等待应用程序完全启动
                await Task.Delay(5000, stoppingToken);

                using var scope = _serviceScopeFactory.CreateScope();
                
                // 获取所有市场数据提供者
                var providers = scope.ServiceProvider.GetServices<IMarketDataProvider>();
                
                foreach (var provider in providers)
                {
                    try
                    {
                        _logger.LogInformation("测试 {ProviderName} 连接...", provider.ProviderName);
                        
                        // 测试连接
                        var isAvailable = await provider.IsAvailableAsync();
                        if (isAvailable)
                        {
                            _logger.LogInformation("✅ {ProviderName} 连接成功，开始数据同步", provider.ProviderName);
                            
                            // 启动数据同步
                            await provider.StartRealTimeDataSyncAsync();
                            _providers.Add(provider);
                            
                            _logger.LogInformation("🚀 {ProviderName} 数据同步已启动", provider.ProviderName);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ {ProviderName} 连接失败，跳过数据同步", provider.ProviderName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ 启动 {ProviderName} 数据同步时发生错误", provider.ProviderName);
                    }
                }

                if (_providers.Count == 0)
                {
                    _logger.LogWarning("⚠️ 没有可用的市场数据提供者，将使用模拟数据");
                }
                else
                {
                    _logger.LogInformation("🎉 成功启动 {Count} 个市场数据提供者的数据同步", _providers.Count);
                }

                // 保持服务运行，定期检查连接状态
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        
                        // 定期检查连接状态
                        foreach (var provider in _providers)
                        {
                            try
                            {
                                var isAvailable = await provider.IsAvailableAsync();
                                if (!isAvailable)
                                {
                                    _logger.LogWarning("⚠️ {ProviderName} 连接丢失，尝试重新连接", provider.ProviderName);
                                    
                                    // 尝试重新启动数据同步
                                    await provider.StartRealTimeDataSyncAsync();
                                    _logger.LogInformation("🔄 {ProviderName} 重新连接成功", provider.ProviderName);
                                }
                                else
                                {
                                    _logger.LogDebug("✅ {ProviderName} 连接正常", provider.ProviderName);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "检查 {ProviderName} 连接状态时发生错误", provider.ProviderName);
                            }
                        }
                        
                        _logger.LogInformation("🔄 市场数据同步服务运行正常，已监控 {Count} 个提供者", _providers.Count);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消，退出循环
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "市场数据同步服务监控循环出错");
                        // 出错后等待更长时间再重试
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarketDataSyncService 执行时发生错误");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MarketDataSyncService 正在停止...");

            try
            {
                // 停止所有提供者的数据同步
                foreach (var provider in _providers)
                {
                    try
                    {
                        await provider.StopRealTimeDataSyncAsync();
                        _logger.LogInformation("✅ {ProviderName} 数据同步已停止", provider.ProviderName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "停止 {ProviderName} 数据同步时发生错误", provider.ProviderName);
                    }
                }

                _providers.Clear();
                _logger.LogInformation("🎉 MarketDataSyncService 已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止 MarketDataSyncService 时发生错误");
            }

            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            // 清理资源
            foreach (var provider in _providers)
            {
                if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            
            base.Dispose();
        }
    }
}
