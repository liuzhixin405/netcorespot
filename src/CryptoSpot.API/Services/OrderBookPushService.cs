using CryptoSpot.Core.Interfaces;
using CryptoSpot.Core.Interfaces.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.API.Services
{
    /// <summary>
    /// 订单簿数据定时推送服务
    /// </summary>
    public class OrderBookPushService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<OrderBookPushService> _logger;
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        // 支持的交易对
        private readonly string[] _supportedSymbols = { "BTCUSDT", "ETHUSDT", "SOLUSDT" };

        public OrderBookPushService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<OrderBookPushService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("订单簿推送服务启动");
            
            try
            {
                await PushOrderBookLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("订单簿推送服务正在停止");
            }
            finally
            {
                _logger.LogInformation("订单簿推送服务已停止");
            }
        }

        private async Task PushOrderBookLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 初始延迟
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var realTimeDataPushService = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();

                        // 为每个支持的交易对推送订单簿数据
                        var tasks = _supportedSymbols.Select(async symbol =>
                        {
                            try
                            {
                                await realTimeDataPushService.PushOrderBookDataAsync(symbol, 20);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "推送 {Symbol} 订单簿数据失败", symbol);
                            }
                        });

                        await Task.WhenAll(tasks);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "推送订单簿数据时出错");
                    }
                    
                    // 等待5秒
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("订单簿推送循环已取消");
            }
        }

        public override void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            base.Dispose();
        }
    }
}
