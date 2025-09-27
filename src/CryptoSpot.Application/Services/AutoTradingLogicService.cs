using CryptoSpot.Domain.Entities;
// removed: using CryptoSpot.Core.Interfaces.Trading;
// removed: using CryptoSpot.Core.Interfaces.MarketData;
// removed: using CryptoSpot.Core.Interfaces;
// removed: using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DTOs.Trading; // 引入 DTO 枚举与请求类型

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 自动交易逻辑服务 - 简化版本，专注于核心交易逻辑
    /// </summary>
    public class AutoTradingLogicService : IAutoTradingService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<AutoTradingLogicService> _logger;
        private readonly Random _random = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _tradingTask;

        public AutoTradingLogicService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<AutoTradingLogicService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }        public Task StartAutoTradingAsync()
        {
            _logger.LogInformation("自动交易服务启动");
            
            _tradingTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ExecuteTradingCycleAsync();
                        await Task.Delay(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "自动交易循环执行出错");
                        await Task.Delay(TimeSpan.FromSeconds(60), _cancellationTokenSource.Token);
                    }
                }
            }, _cancellationTokenSource.Token);
            
            return Task.CompletedTask;
        }

        public async Task StopAutoTradingAsync()
        {
            _logger.LogInformation("自动交易服务停止");
            _cancellationTokenSource.Cancel();
            
            if (_tradingTask != null)
            {
                await _tradingTask;
            }
        }

        private async Task ExecuteTradingCycleAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var tradingPairService = scope.ServiceProvider.GetRequiredService<ITradingPairService>();
            var priceDataService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

            try
            {
                // 获取活跃的交易对
                var tradingPairs = await tradingPairService.GetActiveTradingPairsAsync();
                
                foreach (var pair in tradingPairs.Take(5)) // 限制处理数量
                {
                    await CreateMarketMakingOrdersAsync(pair.Symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行交易周期时出错");
            }
        }

        public async Task CreateMarketMakingOrdersAsync(string symbol)
        {
            try
            {
                const int systemUserId = 1; // 系统用户ID
                
                _logger.LogDebug("开始为交易对 {Symbol} 创建做市订单", symbol);

                using var scope = _serviceScopeFactory.CreateScope();
                var priceDataService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();
                var tradingService = scope.ServiceProvider.GetRequiredService<ITradingService>();

                // 获取当前价格
                var currentPrice = await priceDataService.GetCurrentPriceAsync(symbol);
                if (currentPrice == null)
                {
                    _logger.LogWarning("无法获取 {Symbol} 的当前价格", symbol);
                    return;
                }

                // 创建买卖订单 - 让价格更接近以便匹配
                var buyPrice = currentPrice.Price * 0.9995m; // 低于市价0.05%
                var sellPrice = currentPrice.Price * 1.0005m; // 高于市价0.05%
                var quantity = (decimal)(_random.NextDouble() * 0.1 + 0.01); // 随机数量

                // 使用交易服务创建订单（会触发撮合引擎）
                var buyRequest = new CreateOrderRequestDto
                {
                    Symbol = symbol,
                    Side = OrderSideDto.Buy,
                    Type = OrderTypeDto.Limit,
                    Quantity = quantity,
                    Price = buyPrice,
                    ClientOrderId = $"MM_BUY_{DateTime.UtcNow.Ticks}"
                };

                var sellRequest = new CreateOrderRequestDto
                {
                    Symbol = symbol,
                    Side = OrderSideDto.Sell,
                    Type = OrderTypeDto.Limit,
                    Quantity = quantity,
                    Price = sellPrice,
                    ClientOrderId = $"MM_SELL_{DateTime.UtcNow.Ticks}"
                };

                // 先创建买单，等待撮合完成
                var buyOrder = await tradingService.SubmitOrderAsync(systemUserId, buyRequest);
                if (buyOrder != null)
                {
                    _logger.LogDebug("买单创建成功: {OrderId}", buyOrder.Data?.OrderId);
                }

                // 再创建卖单，应该能匹配到买单
                var sellOrder = await tradingService.SubmitOrderAsync(systemUserId, sellRequest);
                if (sellOrder != null)
                {
                    _logger.LogDebug("卖单创建成功: {OrderId}", sellOrder.Data?.OrderId);
                }

                _logger.LogDebug("为 {Symbol} 创建做市订单完成", symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为 {Symbol} 创建做市订单时出错", symbol);
            }
        }

        public async Task CancelExpiredSystemOrdersAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            
            try
            {
                const int systemUserId = 1; // 系统用户ID
                
                // 获取系统用户的待处理订单
                var pendingOrders = await orderService.GetUserOrdersAsync(systemUserId, OrderStatus.Pending);
                
                // 取消超过5分钟的订单
                var expiredOrders = pendingOrders.Where(o => 
                    DateTime.UtcNow - o.CreatedDateTime > TimeSpan.FromMinutes(5)).ToList();

                foreach (var order in expiredOrders)
                {
                    await orderService.CancelOrderAsync(order.Id, systemUserId);
                    _logger.LogDebug("取消过期订单 {OrderId}", order.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期订单时出错");
            }
        }

        public async Task RebalanceSystemAssetsAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var assetService = scope.ServiceProvider.GetRequiredService<IAssetDomainService>();
            
            try
            {
                const int systemUserId = 1; // 系统用户ID
                
                // 获取系统资产
                var assets = await assetService.GetUserAssetsAsync(systemUserId);
                
                _logger.LogDebug("系统资产再平衡检查完成，资产数量: {Count}", assets.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "系统资产再平衡时出错");
            }
        }

        public async Task<AutoTradingStats> GetTradingStatsAsync(int systemAccountId)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var tradeService = scope.ServiceProvider.GetRequiredService<ITradeService>();
            var assetService = scope.ServiceProvider.GetRequiredService<IAssetDomainService>();
            
            try
            {
                // 获取活跃订单数量
                var activeOrders = await orderService.GetUserOrdersAsync(systemAccountId, OrderStatus.Pending);
                var activeOrdersCount = activeOrders.Count();

                // 获取今日交易数量
                var todayTrades = await tradeService.GetUserTradesAsync(systemAccountId, string.Empty, limit: 1000);
                var todayStart = DateTime.UtcNow.Date;
                var todayTradesCount = todayTrades.Count(t => t.ExecutedDateTime >= todayStart);

                // 计算今日交易量
                var dailyVolume = todayTrades
                    .Where(t => t.ExecutedDateTime >= todayStart)
                    .Sum(t => t.TotalValue);

                // 获取资产余额
                var assets = await assetService.GetUserAssetsAsync(systemAccountId);
                var assetBalances = assets.ToDictionary(a => a.Symbol, a => a.Total);

                return new AutoTradingStats
                {
                    UserId = systemAccountId,
                    DailyVolume = dailyVolume,
                    DailyProfit = 0, // 简化实现，实际应该计算盈亏
                    ActiveOrdersCount = activeOrdersCount,
                    TotalTradesCount = todayTradesCount,
                    AssetBalances = assetBalances
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易统计时出错");
                return new AutoTradingStats
                {
                    UserId = systemAccountId,
                    DailyVolume = 0,
                    DailyProfit = 0,
                    ActiveOrdersCount = 0,
                    TotalTradesCount = 0,
                    AssetBalances = new Dictionary<string, decimal>()
                };
            }
        }

        public void Dispose()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto trading service cleanup");
            }
        }
    }
}
