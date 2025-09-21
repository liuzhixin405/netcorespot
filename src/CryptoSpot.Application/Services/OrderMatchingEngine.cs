using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

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
        private readonly ISystemAssetService _systemAssetService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrderMatchingEngine> _logger;

        // 用于防止并发匹配的锁
        private readonly Dictionary<string, SemaphoreSlim> _symbolLocks = new();

        public OrderMatchingEngine(
            IOrderService orderService,
            ITradeService tradeService,
            IAssetService assetService,
            ISystemAssetService systemAssetService,
            IServiceProvider serviceProvider,
            ILogger<OrderMatchingEngine> logger)
        {
            _orderService = orderService;
            _tradeService = tradeService;
            _assetService = assetService;
            _systemAssetService = systemAssetService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<OrderMatchResult> ProcessOrderAsync(Order order)
        {
            var result = new OrderMatchResult { Order = order };

            try
            {
                // 获取交易对符号
                var symbol = order.TradingPair.Symbol;
                
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
                order.Status = OrderStatus.Rejected;
                await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Rejected);
            }

            // 推送订单簿更新
            try
            {
                var realTimeDataPushService = _serviceProvider.GetRequiredService<IRealTimeDataPushService>();
                await realTimeDataPushService.PushOrderBookDataAsync(order.TradingPair.Symbol, 20);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "推送订单簿数据失败: Symbol={Symbol}", order.TradingPair.Symbol);
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

        public async Task<OrderBookDepth> GetOrderBookDepthAsync(string symbol, int depth = 20)
        {
            var orderBookDepth = new OrderBookDepth { Symbol = symbol };

            try
            {
                var activeOrders = await _orderService.GetActiveOrdersAsync(symbol);
                
                // 买单聚合
                var buyOrders = activeOrders
                    .Where(o => o.Side == OrderSide.Buy && o.Type == OrderType.Limit)
                    .GroupBy(o => o.Price)
                    .Select(g => new OrderBookLevel
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
                    .Select(g => new OrderBookLevel
                    {
                        Price = g.Key ?? 0,
                        Quantity = g.Sum(o => o.RemainingQuantity),
                        OrderCount = g.Count(),
                        Total = g.Sum(o => o.RemainingQuantity)
                    })
                    .OrderBy(l => l.Price)
                    .Take(depth)
                    .ToList();

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
            // 不能自成交（同一用户的订单），除非涉及系统账号
            if (buyOrder.UserId.HasValue && sellOrder.UserId.HasValue && buyOrder.UserId == sellOrder.UserId)
            {
                return false;
            }

            // 系统账号可以自成交（做市需要）
            if (buyOrder.User?.IsSystemAccount == true || sellOrder.User?.IsSystemAccount == true)
            {
                return true;
            }

            return true;
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
            
            // 获取对手方订单
            var activeOrders = await _orderService.GetActiveOrdersAsync(marketOrder.TradingPair.Symbol);
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
            
            // 获取可匹配的对手方订单
            var activeOrders = await _orderService.GetActiveOrdersAsync(limitOrder.TradingPair.Symbol);
            var matchableOrders = activeOrders
                .Where(o => o.Side != limitOrder.Side && o.Type == OrderType.Limit)
                .Where(o => limitOrder.Side == OrderSide.Buy ? o.Price <= limitOrder.Price : o.Price >= limitOrder.Price)
                .OrderBy(o => limitOrder.Side == OrderSide.Buy ? o.Price : -o.Price)
                .ThenBy(o => o.CreatedAt)
                .ToList();

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
                // 创建交易记录
                var trade = await _tradeService.ExecuteTradeAsync(buyOrder, sellOrder, price, quantity);

                // 更新订单状态
                buyOrder.FilledQuantity += quantity;
                sellOrder.FilledQuantity += quantity;
                
                await UpdateOrderStatusAfterFill(buyOrder);
                await UpdateOrderStatusAfterFill(sellOrder);

                // 处理资产变动
                await ProcessAssetChanges(buyOrder, sellOrder, price, quantity);

                return trade;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建交易记录时出错");
                return null;
            }
        }

        private async Task UpdateOrderStatusAfterMatch(Order order, List<Trade> trades)
        {
            var totalFilled = trades.Sum(t => t.Quantity);
            order.FilledQuantity += totalFilled;
            
            await UpdateOrderStatusAfterFill(order);
        }

        private async Task UpdateOrderStatusAfterFill(Order order)
        {
            OrderStatus newStatus;
            
            if (order.FilledQuantity >= order.Quantity)
            {
                newStatus = OrderStatus.Filled;
            }
            else if (order.FilledQuantity > 0)
            {
                newStatus = OrderStatus.PartiallyFilled;
            }
            else
            {
                newStatus = OrderStatus.Pending;
            }

            if (order.Status != newStatus)
            {
                await _orderService.UpdateOrderStatusAsync(order.Id, newStatus, order.FilledQuantity);
            }
        }

        private async Task ProcessAssetChanges(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
        {
            var symbol = buyOrder.TradingPair.Symbol;
            var baseAsset = symbol.Replace("USDT", "");
            var quoteAsset = "USDT";
            var totalValue = price * quantity;

            // 处理买方资产变动
            if (buyOrder.UserId.HasValue)
            {
                // 扣除USDT，增加基础资产
                await _assetService.DeductAssetAsync(buyOrder.UserId.Value, quoteAsset, totalValue, true);
                await _assetService.AddAssetAsync(buyOrder.UserId.Value, baseAsset, quantity);
            }
            else if (buyOrder.UserId.HasValue && buyOrder.User?.IsSystemAccount == true)
            {
                await _systemAssetService.DeductAssetAsync(buyOrder.UserId.Value, quoteAsset, totalValue, true);
                await _systemAssetService.AddAssetAsync(buyOrder.UserId.Value, baseAsset, quantity);
            }

            // 处理卖方资产变动
            if (sellOrder.UserId.HasValue)
            {
                // 扣除基础资产，增加USDT
                await _assetService.DeductAssetAsync(sellOrder.UserId.Value, baseAsset, quantity, true);
                await _assetService.AddAssetAsync(sellOrder.UserId.Value, quoteAsset, totalValue);
            }
            else if (sellOrder.UserId.HasValue && sellOrder.User?.IsSystemAccount == true)
            {
                await _systemAssetService.DeductAssetAsync(sellOrder.UserId.Value, baseAsset, quantity, true);
                await _systemAssetService.AddAssetAsync(sellOrder.UserId.Value, quoteAsset, totalValue);
            }
        }

        private async Task UnfreezeOrderAssets(Order order)
        {
            var symbol = order.TradingPair.Symbol;
            var remainingQuantity = order.RemainingQuantity;
            
            if (order.Side == OrderSide.Buy)
            {
                // 买单解冻USDT
                var unfreezeAmount = remainingQuantity * (order.Price ?? 0);
                if (order.UserId.HasValue)
                {
                    await _assetService.UnfreezeAssetAsync(order.UserId.Value, "USDT", unfreezeAmount);
                }
                else if (order.UserId.HasValue && order.User?.IsSystemAccount == true)
                {
                    await _systemAssetService.UnfreezeAssetAsync(order.UserId.Value, "USDT", unfreezeAmount);
                }
            }
            else
            {
                // 卖单解冻基础资产
                var baseAsset = symbol.Replace("USDT", "");
                if (order.UserId.HasValue)
                {
                    await _assetService.UnfreezeAssetAsync(order.UserId.Value, baseAsset, remainingQuantity);
                }
                else if (order.UserId.HasValue && order.User?.IsSystemAccount == true)
                {
                    await _systemAssetService.UnfreezeAssetAsync(order.UserId.Value, baseAsset, remainingQuantity);
                }
            }
        }

        #endregion
    }
}
