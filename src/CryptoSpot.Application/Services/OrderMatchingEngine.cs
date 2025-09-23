using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Repositories; // 引入IUnitOfWork等接口
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using RepoOrderBookDepth = CryptoSpot.Core.Interfaces.Repositories.OrderBookDepth; // alias if needed
using RepoOrderBookLevel = CryptoSpot.Core.Interfaces.Repositories.OrderBookLevel; // alias if needed

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 订单匹配引擎实现
    /// </summary>
    public class OrderMatchingEngine : IOrderMatchingEngine
    {
        private readonly IOrderService _orderService;
        private readonly ITradeService _tradeService;
        private readonly IAssetService _assetService;
        private readonly ITradingPairService _tradingPairService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrderMatchingEngine> _logger;

        // 用于防止并发匹配的锁
        private readonly Dictionary<string, SemaphoreSlim> _symbolLocks = new();

        public OrderMatchingEngine(
            IOrderService orderService,
            ITradeService tradeService,
            IAssetService assetService,
            ITradingPairService tradingPairService,
            IServiceProvider serviceProvider,
            ILogger<OrderMatchingEngine> logger)
        {
            _orderService = orderService;
            _tradeService = tradeService;
            _assetService = assetService;
            _tradingPairService = tradingPairService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<OrderMatchResult> ProcessOrderAsync(Order order)
        {
            var result = new OrderMatchResult { Order = order };
            string symbol = string.Empty;

            try
            {
                // 获取交易对符号
                var tradingPair = await _tradingPairService.GetTradingPairByIdAsync(order.TradingPairId);
                if (tradingPair == null)
                {
                    _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", order.TradingPairId);
                    order.Status = OrderStatus.Rejected;
                    await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Rejected);
                    return result;
                }
                symbol = tradingPair.Symbol;
                
                // 获取或创建该交易对的锁
                var symbolLock = GetSymbolLock(symbol);
                
                await symbolLock.WaitAsync();
                try
                {
                    // 如果是市价单，立即匹配
                    if (order.Type == OrderType.Market)
                    {
                        result.Trades = await MatchMarketOrderAsync(order);
                    }
                    else
                    {
                        // 限价单先尝试匹配，未匹配部分进入订单簿
                        result.Trades = await MatchLimitOrderAsync(order);
                    }

                    // 更新订单状态
                    await UpdateOrderStatusAfterMatch(order, result.Trades);
                }
                finally
                {
                    symbolLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理订单时出错: OrderId={OrderId}", order.OrderId);
                try
                {
                    order.Status = OrderStatus.Rejected;
                    await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Rejected);
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "更新订单状态失败: OrderId={OrderId}", order.OrderId);
                }
            }

            // 推送订单簿更新
            if (!string.IsNullOrEmpty(symbol))
            {
                try
                {
                    var realTimeDataPushService = _serviceProvider.GetRequiredService<IRealTimeDataPushService>();
                    await realTimeDataPushService.PushOrderBookDataAsync(symbol, 20);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "推送订单簿数据失败: Symbol={Symbol}", symbol);
                }
            }

            return result;
        }

        public async Task<List<Trade>> MatchOrdersAsync(string symbol)
        {
            var trades = new List<Trade>();
            
            try
            {
                var symbolLock = GetSymbolLock(symbol);
                await symbolLock.WaitAsync();
                
                try
                {
                    // 获取活跃的买单和卖单
                    var activeOrders = await _orderService.GetActiveOrdersAsync(symbol);
                    
                    var buyOrders = activeOrders
                        .Where(o => o.Side == OrderSide.Buy && o.Type == OrderType.Limit)
                        .OrderByDescending(o => o.Price) // 买单按价格降序
                        .ThenBy(o => o.CreatedAt) // 同价格按时间优先
                        .ToList();
                    
                    var sellOrders = activeOrders
                        .Where(o => o.Side == OrderSide.Sell && o.Type == OrderType.Limit)
                        .OrderBy(o => o.Price) // 卖单按价格升序
                        .ThenBy(o => o.CreatedAt) // 同价格按时间优先
                        .ToList();

                    _logger.LogInformation("📊 订单撮合开始: Symbol={Symbol}, 买单数量={BuyCount}, 卖单数量={SellCount}", 
                        symbol, buyOrders.Count, sellOrders.Count);

                    // 匹配订单
                    foreach (var buyOrder in buyOrders)
                    {
                        if (buyOrder.RemainingQuantity <= 0) continue;

                        foreach (var sellOrder in sellOrders)
                        {
                            if (sellOrder.RemainingQuantity <= 0) continue;
                            
                            // 检查价格是否匹配
                            if (buyOrder.Price >= sellOrder.Price)
                            {
                                _logger.LogInformation("💰 发现价格匹配: 买单价格={BuyPrice}, 卖单价格={SellPrice}, 买单ID={BuyOrderId}, 卖单ID={SellOrderId}", 
                                    buyOrder.Price, sellOrder.Price, buyOrder.Id, sellOrder.Id);
                                
                                // 检查是否可以匹配（不能自成交，除非是系统账号）
                                if (await CanMatchOrderAsync(buyOrder, sellOrder))
                                {
                                    _logger.LogInformation("✅ 订单可以匹配，开始执行交易");
                                    var trade = await ExecuteTradeAsync(buyOrder, sellOrder);
                                    if (trade != null)
                                    {
                                        trades.Add(trade);
                                        _logger.LogInformation("🎉 交易执行成功: TradeId={TradeId}, Price={Price}, Quantity={Quantity}", 
                                            trade.TradeId, trade.Price, trade.Quantity);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("❌ 交易执行失败");
                                    }
                                }
                                else
                                {
                                    _logger.LogInformation("⚠️ 订单无法匹配（可能是自成交限制）");
                                }
                            }
                            else
                            {
                                _logger.LogDebug("⏭️ 价格不匹配: 买单价格={BuyPrice}, 卖单价格={SellPrice}", 
                                    buyOrder.Price, sellOrder.Price);
                                // 价格不匹配，跳出内层循环
                                break;
                            }
                        }
                    }

                    if (trades.Any())
                    {
                        _logger.LogInformation("为 {Symbol} 匹配了 {TradeCount} 笔交易", symbol, trades.Count);
                        
                        // 推送订单簿更新
                        try
                        {
                            var realTimeDataPushService = _serviceProvider.GetRequiredService<IRealTimeDataPushService>();
                            await realTimeDataPushService.PushOrderBookDataAsync(symbol, 20);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "推送订单簿数据失败: Symbol={Symbol}", symbol);
                        }
                    }
                }
                finally
                {
                    symbolLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "匹配 {Symbol} 订单时出错", symbol);
            }

            return trades;
        }

        public async Task<CryptoSpot.Core.Interfaces.Trading.OrderBookDepth> GetOrderBookDepthAsync(string symbol, int depth = 20)
        {
            var orderBookDepth = new CryptoSpot.Core.Interfaces.Trading.OrderBookDepth { Symbol = symbol };

            try
            {
                var activeOrders = await _orderService.GetActiveOrdersAsync(symbol);
                
                var orderCount = activeOrders.Count();
                _logger.LogInformation($"📊 获取订单簿深度: Symbol={symbol}, 活跃订单数量={orderCount}, 请求深度={depth}");
                
                // 买单聚合
                var buyOrders = activeOrders
                    .Where(o => o.Side == OrderSide.Buy && o.Type == OrderType.Limit)
                    .GroupBy(o => o.Price)
                    .Select(g => new CryptoSpot.Core.Interfaces.Trading.OrderBookLevel
                    {
                        Price = g.Key ?? 0,
                        Quantity = g.Sum(o => o.RemainingQuantity),
                        OrderCount = g.Count(),
                        Total = g.Sum(o => o.RemainingQuantity)
                    })
                    .OrderByDescending(l => l.Price)
                    .Take(depth)
                    .ToList();

                // 卖单聚合
                var sellOrders = activeOrders
                    .Where(o => o.Side == OrderSide.Sell && o.Type == OrderType.Limit)
                    .GroupBy(o => o.Price)
                    .Select(g => new CryptoSpot.Core.Interfaces.Trading.OrderBookLevel
                    {
                        Price = g.Key ?? 0,
                        Quantity = g.Sum(o => o.RemainingQuantity),
                        OrderCount = g.Count(),
                        Total = g.Sum(o => o.RemainingQuantity)
                    })
                    .OrderBy(l => l.Price)
                    .Take(depth)
                    .ToList();

                _logger.LogInformation("📈 订单簿数据: Symbol={Symbol}, 买单数量={BuyCount}, 卖单数量={SellCount}", 
                    symbol, buyOrders.Count, sellOrders.Count);

                orderBookDepth.Bids = buyOrders;
                orderBookDepth.Asks = sellOrders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 {Symbol} 订单簿深度时出错", symbol);
            }

            return orderBookDepth;
        }

        public async Task<bool> CancelOrderAsync(int orderId)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(orderId, null);
                if (order == null || order.Status != OrderStatus.Pending && order.Status != OrderStatus.PartiallyFilled)
                {
                    return false;
                }

                // 解冻资产
                await UnfreezeOrderAssets(order);

                // 更新订单状态
                await _orderService.UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled);

                _logger.LogInformation("取消订单成功: OrderId={OrderId}", order.OrderId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订单时出错: OrderId={OrderId}", orderId);
                return false;
            }
        }

        public async Task<bool> CanMatchOrderAsync(Order buyOrder, Order sellOrder)
        {
            // 不能自成交（同一用户且非系统账号）
            if (buyOrder.UserId.HasValue && sellOrder.UserId.HasValue && buyOrder.UserId == sellOrder.UserId && buyOrder.UserId != 1)
            {
                return await Task.FromResult(false);
            }
            return await Task.FromResult(true);
        }

        #region Private Methods

        private SemaphoreSlim GetSymbolLock(string symbol)
        {
            if (!_symbolLocks.ContainsKey(symbol))
            {
                _symbolLocks[symbol] = new SemaphoreSlim(1, 1);
            }
            return _symbolLocks[symbol];
        }

        private async Task<List<Trade>> MatchMarketOrderAsync(Order marketOrder)
        {
            var trades = new List<Trade>();
            
            // 获取交易对符号
            var tradingPair = await _tradingPairService.GetTradingPairByIdAsync(marketOrder.TradingPairId);
            if (tradingPair == null)
            {
                _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", marketOrder.TradingPairId);
                return trades;
            }
            
            // 获取对手方订单
            var activeOrders = await _orderService.GetActiveOrdersAsync(tradingPair.Symbol);
            var oppositeOrders = activeOrders
                .Where(o => o.Side != marketOrder.Side && o.Type == OrderType.Limit)
                .OrderBy(o => marketOrder.Side == OrderSide.Buy ? o.Price : -o.Price) // 买单匹配最低卖价，卖单匹配最高买价
                .ThenBy(o => o.CreatedAt)
                .ToList();

            var remainingQuantity = marketOrder.Quantity;

            foreach (var oppositeOrder in oppositeOrders)
            {
                if (remainingQuantity <= 0) break;
                if (oppositeOrder.RemainingQuantity <= 0) continue;
                if (!await CanMatchOrderAsync(marketOrder, oppositeOrder)) continue;

                var matchQuantity = Math.Min(remainingQuantity, oppositeOrder.RemainingQuantity);
                var matchPrice = oppositeOrder.Price ?? 0;

                var trade = await CreateTradeAsync(marketOrder, oppositeOrder, matchPrice, matchQuantity);
                if (trade != null)
                {
                    trades.Add(trade);
                    remainingQuantity -= matchQuantity;
                }
            }

            return trades;
        }

        private async Task<List<Trade>> MatchLimitOrderAsync(Order limitOrder)
        {
            var trades = new List<Trade>();
            
            // 获取交易对符号
            var tradingPair = await _tradingPairService.GetTradingPairByIdAsync(limitOrder.TradingPairId);
            if (tradingPair == null)
            {
                _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", limitOrder.TradingPairId);
                return trades;
            }
            
            // 获取可匹配的对手方订单
            var activeOrders = await _orderService.GetActiveOrdersAsync(tradingPair.Symbol);
            _logger.LogDebug("找到 {Count} 个活跃订单", activeOrders.Count());
            
            var matchableOrders = activeOrders
                .Where(o => o.Side != limitOrder.Side && o.Type == OrderType.Limit)
                .Where(o => limitOrder.Side == OrderSide.Buy ? o.Price <= limitOrder.Price : o.Price >= limitOrder.Price)
                .OrderBy(o => limitOrder.Side == OrderSide.Buy ? o.Price : -o.Price)
                .ThenBy(o => o.CreatedAt)
                .ToList();
                
            _logger.LogDebug("找到 {Count} 个可匹配订单", matchableOrders.Count());

            var remainingQuantity = limitOrder.Quantity;

            foreach (var oppositeOrder in matchableOrders)
            {
                if (remainingQuantity <= 0) break;
                if (oppositeOrder.RemainingQuantity <= 0) continue;
                if (!await CanMatchOrderAsync(limitOrder, oppositeOrder)) continue;

                var matchQuantity = Math.Min(remainingQuantity, oppositeOrder.RemainingQuantity);
                var matchPrice = oppositeOrder.Price ?? 0; // 使用对手方价格

                var trade = await CreateTradeAsync(limitOrder, oppositeOrder, matchPrice, matchQuantity);
                if (trade != null)
                {
                    trades.Add(trade);
                    remainingQuantity -= matchQuantity;
                }
            }

            return trades;
        }

        private async Task<Trade?> ExecuteTradeAsync(Order buyOrder, Order sellOrder)
        {
            try
            {
                var matchQuantity = Math.Min(buyOrder.RemainingQuantity, sellOrder.RemainingQuantity);
                var matchPrice = sellOrder.Price ?? 0; // 使用卖单价格

                return await CreateTradeAsync(buyOrder, sellOrder, matchPrice, matchQuantity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行交易时出错: BuyOrderId={BuyOrderId}, SellOrderId={SellOrderId}", 
                    buyOrder.OrderId, sellOrder.OrderId);
                return null;
            }
        }

        private async Task<Trade?> CreateTradeAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
        {
            try
            {
                // 交给交易服务执行(内部处理订单状态、资产结算、事务提交)
                var trade = await _tradeService.ExecuteTradeAsync(buyOrder, sellOrder, price, quantity);
                return trade;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建交易记录时出错(委托交易服务)");
                throw; // 向上抛出，避免静默失败
            }
        }

        private async Task UpdateOrderStatusAfterMatch(Order order, List<Trade> trades)
        {
            // 交易服务已处理订单成交量与状态，这里只刷新最新状态(从仓储再取或根据现有FilledQuantity判断)
            try
            {
                if (order.FilledQuantity >= order.Quantity)
                {
                    if (order.Status != OrderStatus.Filled)
                        await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Filled, order.FilledQuantity);
                }
                else if (order.FilledQuantity > 0)
                {
                    if (order.Status != OrderStatus.PartiallyFilled)
                        await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.PartiallyFilled, order.FilledQuantity);
                }
                else
                {
                    if (order.Status != OrderStatus.Pending)
                        await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Pending, order.FilledQuantity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "更新订单状态(撮合后)时发生异常: OrderId={OrderId}", order.OrderId);
            }
        }

        private async Task UpdateOrderStatusAfterFill(Order order)
        {
            // 不再在撮合引擎里调整 FilledQuantity，仅根据当前值同步状态
            OrderStatus newStatus = order.Status;
            if (order.FilledQuantity >= order.Quantity)
                newStatus = OrderStatus.Filled;
            else if (order.FilledQuantity > 0 && order.FilledQuantity < order.Quantity)
                newStatus = OrderStatus.PartiallyFilled;
            else if (order.FilledQuantity == 0)
                newStatus = OrderStatus.Pending;

            if (order.Status != newStatus)
            {
                await _orderService.UpdateOrderStatusAsync(order.Id, newStatus, order.FilledQuantity);
            }
        }

        private async Task ProcessAssetChanges(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
        {
            // 已由交易服务统一处理，这里留空以避免重复结算
            await Task.CompletedTask;
        }

        private async Task UnfreezeOrderAssets(Order order)
        {
            var tradingPair = await _tradingPairService.GetTradingPairByIdAsync(order.TradingPairId);
            if (tradingPair == null)
            {
                _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", order.TradingPairId);
                return;
            }
            var symbol = tradingPair.Symbol;
            var remainingQuantity = order.RemainingQuantity;
            
            if (order.Side == OrderSide.Buy)
            {
                // 买单解冻USDT
                var unfreezeAmount = remainingQuantity * (order.Price ?? 0);
                if (order.UserId.HasValue)
                {
                    // 统一使用AssetService处理所有用户资产
                    await _assetService.UnfreezeAssetAsync(order.UserId.Value, "USDT", unfreezeAmount);
                }
            }
            else
            {
                // 卖单解冻基础资产
                var baseAsset = symbol.Replace("USDT", "");
                if (order.UserId.HasValue)
                {
                    // 统一使用AssetService处理所有用户资产
                    await _assetService.UnfreezeAssetAsync(order.UserId.Value, baseAsset, remainingQuantity);
                }
            }
        }

        #endregion
    }
}
