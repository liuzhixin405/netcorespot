using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Users; // 新增资产 DTO 引用
using OrderSide = CryptoSpot.Domain.Entities.OrderSide; // 添加枚举别名
using OrderType = CryptoSpot.Domain.Entities.OrderType;
using Microsoft.Extensions.Options;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 自动交易逻辑服务 - 简化版本，专注于核心交易逻辑
    /// </summary>
    public class AutoTradingLogicService : IAutoTradingService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<AutoTradingLogicService> _logger;
        private readonly IMarketMakingStrategy _marketMakingStrategy;
        private readonly MarketMakerOptions _marketMakerOptions;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _tradingTask;

        public AutoTradingLogicService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<AutoTradingLogicService> logger,
            IMarketMakingStrategy marketMakingStrategy,
            IOptions<MarketMakerOptions> marketMakerOptions)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _marketMakingStrategy = marketMakingStrategy;
            _marketMakerOptions = marketMakerOptions.Value;
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
            await CancelExpiredSystemOrdersAsync();
            using var scope = _serviceScopeFactory.CreateScope();
            var tradingPairService = scope.ServiceProvider.GetRequiredService<ITradingPairService>();
            var priceDataService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var orderMatchingEngine = scope.ServiceProvider.GetRequiredService<IOrderMatchingEngine>();

            try
            {
                // 获取活跃的交易对 (已改为 ApiResponseDto)
                var tradingPairsResp = await tradingPairService.GetActiveTradingPairsAsync();
                if (!tradingPairsResp.Success || tradingPairsResp.Data == null)
                {
                    _logger.LogWarning("获取活跃交易对失败: {Error}", tradingPairsResp.Error ?? "Unknown");
                    return;
                }
                var tradingPairs = tradingPairsResp.Data;
                
                foreach (var pair in tradingPairs.Take(5)) // 限制处理数量
                {
                    try
                    {
                        // 1. 先执行订单匹配，处理现有的pending订单
                        var matchedTrades = await orderMatchingEngine.MatchOrdersAsync(pair.Symbol);
                        if (matchedTrades.Any())
                        {
                            _logger.LogInformation("为交易对 {Symbol} 匹配了 {Count} 笔交易", pair.Symbol, matchedTrades.Count);
                        }
                        
                        // 2. 创建做市订单
                        await CreateMarketMakingOrdersAsync(pair.Symbol);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理交易对 {Symbol} 时出错", pair.Symbol);
                    }
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
                var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();

                // 获取当前价格
                var currentPrice = await priceDataService.GetCurrentPriceAsync(symbol);
                if (currentPrice == null)
                {
                    _logger.LogWarning("无法获取 {Symbol} 的当前价格", symbol);
                    return;
                }

                var decision = await GetMarketMakingDecisionAsync(
                    symbol,
                    currentPrice.Price,
                    tradingService,
                    assetService);
                var buyPrice = decision.BuyPrice;
                var sellPrice = decision.SellPrice;
                var quantity = decision.Quantity;
                _logger.LogInformation(
                    "Market-making decision for {Symbol}: buy={BuyPrice}, sell={SellPrice}, quantity={Quantity}. {Rationale}",
                    symbol, buyPrice, sellPrice, quantity, decision.Rationale);

                // 使用交易服务创建订单（会触发撮合引擎）
                var buyRequest = new CreateOrderRequestDto
                {
                    Symbol = symbol,
                    Side = OrderSide.Buy,
                    Type = OrderType.Limit,
                    Quantity = quantity,
                    Price = buyPrice,
                    ClientOrderId = $"MM_BUY_{DateTime.UtcNow.Ticks}"
                };

                var sellRequest = new CreateOrderRequestDto
                {
                    Symbol = symbol,
                    Side = OrderSide.Sell,
                    Type = OrderType.Limit,
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

        private async Task<MarketMakingDecision> GetMarketMakingDecisionAsync(
            string symbol,
            decimal currentPrice,
            ITradingService tradingService,
            IAssetService assetService)
        {
            try
            {
                var pairTask = tradingService.GetTradingPairAsync(symbol);
                var candlesTask = tradingService.GetKLineDataAsync(symbol, "1m", 30);
                var orderBookTask = tradingService.GetOrderBookDepthAsync(symbol, 20);
                var tradesTask = tradingService.GetMarketRecentTradesAsync(symbol, 50);
                await Task.WhenAll(pairTask, candlesTask, orderBookTask, tradesTask);

                var pair = pairTask.Result.Data;
                var assets = pair == null
                    ? null
                    : await assetService.GetUserAssetsAsync(1);
                var baseInventory = pair == null || assets == null || !assets.Success || assets.Data == null
                    ? 0m
                    : assets.Data.FirstOrDefault(asset =>
                        string.Equals(asset.Symbol, pair.BaseAsset, StringComparison.OrdinalIgnoreCase))?.Total ?? 0m;
                var bidDepth = orderBookTask.Result.Success && orderBookTask.Result.Data != null
                    ? orderBookTask.Result.Data.Bids.Sum(level => level.Quantity)
                    : 0m;
                var askDepth = orderBookTask.Result.Success && orderBookTask.Result.Data != null
                    ? orderBookTask.Result.Data.Asks.Sum(level => level.Quantity)
                    : 0m;
                var targetInventory = pair == null || currentPrice <= 0
                    ? 0m
                    : _marketMakerOptions.TargetInventoryUsdt / currentPrice;

                return _marketMakingStrategy.Decide(new MarketMakingContext
                {
                    Symbol = symbol,
                    CurrentPrice = currentPrice,
                    RecentClosePrices = candlesTask.Result.Success && candlesTask.Result.Data != null
                        ? candlesTask.Result.Data.OrderBy(candle => candle.OpenTime).Select(candle => candle.Close).ToArray()
                        : Array.Empty<decimal>(),
                    CurrentInventory = baseInventory,
                    TargetInventory = targetInventory,
                    BidDepth = bidDepth,
                    AskDepth = askDepth,
                    RecentTradeCount = tradesTask.Result.Success && tradesTask.Result.Data != null
                        ? tradesTask.Result.Data.Count()
                        : 0
                });
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Falling back to fixed market-making quotes for {Symbol}", symbol);
                return new MarketMakingDecision
                {
                    BuyPrice = currentPrice * 0.9995m,
                    SellPrice = currentPrice * 1.0005m,
                    Quantity = _marketMakerOptions.BaseOrderSize,
                    Rationale = "Fallback: strategy inputs were unavailable."
                };
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
                var pendingOrdersResp = await orderService.GetUserOrdersDtoAsync(systemUserId, OrderStatus.Pending);
                var pendingOrders = pendingOrdersResp.Success && pendingOrdersResp.Data != null ? pendingOrdersResp.Data : Enumerable.Empty<OrderDto>();
                
                // 取消超过5分钟的订单
                var expiredOrders = pendingOrders.Where(o => 
                    DateTime.UtcNow - o.CreatedDateTime > TimeSpan.FromMinutes(5)).ToList();

                foreach (var order in expiredOrders)
                {
                    await orderService.CancelOrderDtoAsync(order.Id, systemUserId);
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
            var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();
            
            try
            {
                const int systemUserId = 1; // 系统用户ID
                
                // 获取系统资产
                var assetsResp = await assetService.GetUserAssetsAsync(systemUserId);
                var assets = assetsResp.Success && assetsResp.Data != null ? assetsResp.Data : Enumerable.Empty<AssetDto>();
                var assetCount = assets.Count();
                _logger.LogDebug("系统资产再平衡检查完成，资产数量: {Count}", assetCount);
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
            var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();
            
            try
            {
                // 获取活跃订单数量
                var activeOrdersResp = await orderService.GetUserOrdersDtoAsync(systemAccountId, OrderStatus.Pending);
                var activeOrders = activeOrdersResp.Success && activeOrdersResp.Data != null ? activeOrdersResp.Data : Enumerable.Empty<OrderDto>();
                var activeOrdersCount = activeOrders.Count();

                // 获取今日交易数量
                var todayTradesResp = await tradeService.GetTradeHistoryAsync(systemAccountId, null, 1000);
                var todayTrades = todayTradesResp.Success && todayTradesResp.Data != null ? todayTradesResp.Data : Enumerable.Empty<TradeDto>();
                var todayStart = DateTime.UtcNow.Date;
                var todayTradesCount = todayTrades.Count(t => t.ExecutedDateTime >= todayStart);

                // 计算今日交易量
                var dailyVolume = todayTrades
                    .Where(t => t.ExecutedDateTime >= todayStart)
                    .Sum(t => t.TotalValue);

                // 获取资产余额
                var assetsResp2 = await assetService.GetUserAssetsAsync(systemAccountId);
                var assets2 = assetsResp2.Success && assetsResp2.Data != null ? assetsResp2.Data : Enumerable.Empty<AssetDto>();
                var assetBalances = assets2.ToDictionary(a => a.Symbol, a => a.Total);

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
