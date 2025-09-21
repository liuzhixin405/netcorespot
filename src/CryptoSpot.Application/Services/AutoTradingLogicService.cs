using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 自动交易逻辑服务 - 纯粹的业务服务，不继承BackgroundService
    /// </summary>
    public class AutoTradingLogicService : IAutoTradingService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<AutoTradingLogicService> _logger;
        private readonly Random _random = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _tradingTask;
        private Task? _cleanupTask;

        // 支持的交易对
        private readonly string[] _supportedSymbols = { "BTCUSDT", "ETHUSDT", "SOLUSDT" };
        
        // 做市参数
        private readonly decimal _spreadPercentage = 0.002m; // 0.2% 价差
        private readonly decimal _orderSizeRatio = 0.01m; // 每个订单占资产的1%
        private readonly int _maxOrdersPerSide = 5; // 每边最多5个订单

        public AutoTradingLogicService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<AutoTradingLogicService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        public async Task StartAutoTradingAsync()
        {
            _logger.LogInformation("启动自动交易服务");
            
            // 确保系统账号存在
            await EnsureSystemAccountsExistAsync();
            
            // 启动异步任务
            _tradingTask = Task.Run(async () => await TradingLoopAsync(_cancellationTokenSource.Token));
            _cleanupTask = Task.Run(async () => await CleanupLoopAsync(_cancellationTokenSource.Token));
        }

        public async Task StopAutoTradingAsync()
        {
            _logger.LogInformation("停止自动交易服务");
            
            // 取消所有任务
            _cancellationTokenSource.Cancel();
            
            try
            {
                // 等待任务完成
                if (_tradingTask != null)
                {
                    await _tradingTask;
                }
                if (_cleanupTask != null)
                {
                    await _cleanupTask;
                }
                
                // 取消所有系统订单
                await CancelAllSystemOrdersAsync();
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("ServiceProvider has been disposed, skipping cleanup operations");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto trading service cleanup");
            }
        }

        public async Task CreateMarketMakingOrdersAsync(string symbol)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var systemAccountService = scope.ServiceProvider.GetRequiredService<ISystemAccountService>();
            var systemAssetService = scope.ServiceProvider.GetRequiredService<ISystemAssetService>();
            var priceDataService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            
            try
            {
                var marketMakers = await systemAccountService.GetSystemAccountsByTypeAsync(UserType.MarketMaker);
                var activeMarketMaker = marketMakers.FirstOrDefault(a => a.IsActive && a.IsAutoTradingEnabled);
                
                if (activeMarketMaker == null)
                {
                    _logger.LogWarning("没有找到活跃的做市商账号");
                    return;
                }

                // 获取当前价格
                var currentPrice = await priceDataService.GetCurrentPriceAsync(symbol);
                if (currentPrice == null)
                {
                    _logger.LogWarning("无法获取 {Symbol} 的当前价格", symbol);
                    return;
                }

                var baseAsset = symbol.Replace("USDT", "");
                var quoteAsset = "USDT";
                
                var baseAssetBalance = await systemAssetService.GetSystemAssetAsync(activeMarketMaker.Id, baseAsset);
                var quoteAssetBalance = await systemAssetService.GetSystemAssetAsync(activeMarketMaker.Id, quoteAsset);
                
                if (baseAssetBalance == null || quoteAssetBalance == null)
                {
                    _logger.LogWarning("系统账号 {AccountId} 缺少必要的资产 {BaseAsset}/{QuoteAsset}", 
                        activeMarketMaker.Id, baseAsset, quoteAsset);
                    return;
                }

                // 先取消现有的系统订单
                await CancelExistingSystemOrdersAsync(activeMarketMaker.Id, symbol, orderService);

                // 创建买单
                await CreateBuyOrdersAsync(activeMarketMaker.Id, symbol, currentPrice.Price, quoteAssetBalance.Available, orderService);
                
                // 创建卖单
                await CreateSellOrdersAsync(activeMarketMaker.Id, symbol, currentPrice.Price, baseAssetBalance.Available, orderService);
                
                // 推送订单簿更新
                try
                {
                    var realTimeDataPushService = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                    await realTimeDataPushService.PushOrderBookDataAsync(symbol, 20);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "推送订单簿数据失败: Symbol={Symbol}", symbol);
                }
                
                _logger.LogInformation("为 {Symbol} 创建做市订单完成", symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为 {Symbol} 创建做市订单时出错", symbol);
            }
        }

        public async Task CancelExpiredSystemOrdersAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var systemAccountService = scope.ServiceProvider.GetRequiredService<ISystemAccountService>();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            
            try
            {
                var systemAccounts = await systemAccountService.GetActiveSystemAccountsAsync();
                
                foreach (var account in systemAccounts)
                {
                    var activeOrders = await orderService.GetActiveOrdersAsync();
                    var systemOrders = activeOrders.Where(o => o.UserId == account.Id);
                    
                    foreach (var order in systemOrders)
                    {
                        await orderService.CancelOrderAsync(order.Id, null);
                    }
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
            var systemAccountService = scope.ServiceProvider.GetRequiredService<ISystemAccountService>();
            var systemAssetService = scope.ServiceProvider.GetRequiredService<ISystemAssetService>();
            
            try
            {
                var systemAccounts = await systemAccountService.GetActiveSystemAccountsAsync();
                
                foreach (var account in systemAccounts)
                {
                    var assets = await systemAssetService.GetSystemAssetsAsync(account.Id);
                    
                    foreach (var asset in assets)
                    {
                        if (asset.Available < asset.MinReserve)
                        {
                            await systemAssetService.AutoRefillAssetAsync(account.Id, asset.Symbol);
                            _logger.LogInformation("自动充值 {Symbol} 为系统账号 {AccountId}", asset.Symbol, account.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重新平衡系统资产时出错");
            }
        }

        public async Task<AutoTradingStats> GetTradingStatsAsync(int systemAccountId)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var systemAssetService = scope.ServiceProvider.GetRequiredService<ISystemAssetService>();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            
            var stats = new AutoTradingStats { UserId = systemAccountId };

            try
            {
                var assets = await systemAssetService.GetSystemAssetsAsync(systemAccountId);
                stats.AssetBalances = assets.ToDictionary(a => a.Symbol, a => a.Total);

                var activeOrders = await orderService.GetActiveOrdersAsync();
                stats.ActiveOrdersCount = activeOrders.Count(o => o.UserId == systemAccountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易统计时出错");
            }

            return stats;
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }

        #region Private Methods

        private async Task TradingLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 初始延迟
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var orderMatchingEngine = scope.ServiceProvider.GetRequiredService<IOrderMatchingEngine>();
                        
                        foreach (var symbol in _supportedSymbols)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;
                                
                            await CreateMarketMakingOrdersAsync(symbol);
                            
                            // 执行订单匹配
                            await orderMatchingEngine.MatchOrdersAsync(symbol);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "执行交易逻辑时出错");
                    }
                    
                    // 等待30秒
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("交易循环已取消");
            }
        }

        private async Task CleanupLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 初始延迟
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CancelExpiredSystemOrdersAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "清理过期订单时出错");
                    }
                    
                    // 等待5分钟
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("清理循环已取消");
            }
        }

        private async Task EnsureSystemAccountsExistAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var systemAccountService = scope.ServiceProvider.GetRequiredService<ISystemAccountService>();
            var systemAssetService = scope.ServiceProvider.GetRequiredService<ISystemAssetService>();
            
            var marketMakers = await systemAccountService.GetSystemAccountsByTypeAsync(UserType.MarketMaker);
            
            if (!marketMakers.Any())
            {
                var marketMaker = await systemAccountService.CreateSystemAccountAsync(
                    "主要做市商", UserType.MarketMaker, "提供主要流动性的做市商账号");
                
                // 初始化资产
                var initialBalances = new Dictionary<string, decimal>
                {
                    ["USDT"] = 100000m,  // 10万USDT
                    ["BTC"] = 5m,        // 5个BTC
                    ["ETH"] = 50m,       // 50个ETH
                    ["SOL"] = 1000m      // 1000个SOL
                };
                
                await systemAssetService.InitializeSystemAssetsAsync(marketMaker.Id, initialBalances);
                
                _logger.LogInformation("创建并初始化做市商账号: {AccountId}", marketMaker.Id);
            }
        }

        private async Task CancelAllSystemOrdersAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var systemAccountService = scope.ServiceProvider.GetRequiredService<ISystemAccountService>();
                var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
                
                var systemAccounts = await systemAccountService.GetActiveSystemAccountsAsync();
                
                foreach (var account in systemAccounts)
                {
                    var activeOrders = await orderService.GetActiveOrdersAsync();
                    var systemOrders = activeOrders.Where(o => o.UserId == account.Id);
                    
                    foreach (var order in systemOrders)
                    {
                        await orderService.CancelOrderAsync(order.Id, null);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("ServiceProvider disposed, cannot cancel system orders");
                throw; // 重新抛出，让上层处理
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel system orders during cleanup");
                throw; // 重新抛出，让上层处理
            }
        }

        private async Task CancelExistingSystemOrdersAsync(int systemAccountId, string symbol, IOrderService orderService)
        {
            var activeOrders = await orderService.GetActiveOrdersAsync(symbol);
            var systemOrders = activeOrders.Where(o => o.UserId == systemAccountId);
            
            foreach (var order in systemOrders)
            {
                await orderService.CancelOrderAsync(order.Id, null);
            }
        }

        private async Task CreateBuyOrdersAsync(int systemAccountId, string symbol, decimal currentPrice, decimal availableBalance, IOrderService orderService)
        {
            var orderSize = availableBalance * _orderSizeRatio;
            if (orderSize < 10) return; // 最小订单金额10 USDT

            for (int i = 1; i <= _maxOrdersPerSide; i++)
            {
                var priceOffset = _spreadPercentage * i * (1 + (decimal)_random.NextSingle() * 0.5m);
                var orderPrice = currentPrice * (1 - (decimal)priceOffset);
                var quantity = orderSize / orderPrice;

                await orderService.CreateOrderAsync(
                    systemAccountId, symbol, OrderSide.Buy, OrderType.Limit, quantity, orderPrice);
            }
        }

        private async Task CreateSellOrdersAsync(int systemAccountId, string symbol, decimal currentPrice, decimal availableBalance, IOrderService orderService)
        {
            var baseSymbol = symbol.Replace("USDT", "");
            var orderSize = availableBalance * _orderSizeRatio;
            
            if (orderSize < 0.001m) return; // 最小数量

            for (int i = 1; i <= _maxOrdersPerSide; i++)
            {
                var priceOffset = _spreadPercentage * i * (1 + (decimal)_random.NextSingle() * 0.5m);
                var orderPrice = currentPrice * (1 + (decimal)priceOffset);
                var quantity = orderSize;

                await orderService.CreateOrderAsync(
                    systemAccountId, symbol, OrderSide.Sell, OrderType.Limit, quantity, orderPrice);
            }
        }

        #endregion
    }
}
