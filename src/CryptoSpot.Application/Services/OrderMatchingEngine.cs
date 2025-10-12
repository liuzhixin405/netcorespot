using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Repositories; 
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Users; // 新增: 资产操作 DTO

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 订单匹配引擎实现
    /// </summary>
    public class OrderMatchingEngine : IOrderMatchingEngine
    {
        private readonly IOrderRawAccess _orderRawAccess; // 使用最小原始访问
        private readonly ITradeService _tradeService;
        private readonly IAssetService _assetService;
        private readonly ITradingPairService _tradingPairService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrderMatchingEngine> _logger;
        private readonly IDtoMappingService _mapping;

        // 用于防止并发匹配的锁
        private readonly Dictionary<string, SemaphoreSlim> _symbolLocks = new();

        public OrderMatchingEngine(
            IOrderRawAccess orderRawAccess,
            ITradeService tradeService,
            IAssetService assetService,
            ITradingPairService tradingPairService,
            IServiceProvider serviceProvider,
            ILogger<OrderMatchingEngine> logger,
            IDtoMappingService mapping)
        {
            _orderRawAccess = orderRawAccess;
            _tradeService = tradeService;
            _assetService = assetService;
            _tradingPairService = tradingPairService;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _mapping = mapping;
        }

        // 新接口实现：接收下单请求 DTO
        public async Task<OrderMatchResultDto> ProcessOrderAsync(CreateOrderRequestDto orderRequest, int userId = 0)
        {
            var pairResp = await _tradingPairService.GetTradingPairAsync(orderRequest.Symbol);
            if (!pairResp.Success || pairResp.Data == null)
            {
                return new OrderMatchResultDto { Order = new OrderDto { Symbol = orderRequest.Symbol }, Trades = new List<TradeDto>() };
            }
            var domainOrder = _mapping.MapToDomain(orderRequest, userId, pairResp.Data.Id);
            // 调用内部旧逻辑 (旧逻辑方法重命名为 ProcessDomainOrderAsync)
            var legacy = await ProcessDomainOrderAsync(domainOrder);
            return new OrderMatchResultDto
            {
                Order = _mapping.MapToDto(legacy.Order),
                Trades = legacy.Trades.Select(_mapping.MapToDto).ToList(),
                IsFullyMatched = legacy.IsFullyMatched,
                TotalMatchedQuantity = legacy.TotalMatchedQuantity,
                AveragePrice = legacy.AveragePrice
            };
        }

        // DTO 匹配执行
        public async Task<List<TradeDto>> MatchOrdersAsync(string symbol)
        {
            var domainTrades = await MatchDomainOrdersAsync(symbol);
            return domainTrades.Select(_mapping.MapToDto).ToList();
        }

        public async Task<OrderBookDepthDto> GetOrderBookDepthAsync(string symbol, int depth = 20)
        {
            var snapshot = await GetOrderBookDepthDomainAsync(symbol, depth);
            var dto = new OrderBookDepthDto
            {
                Symbol = snapshot.Symbol,
                Timestamp = snapshot.Timestamp,
                Bids = snapshot.Bids.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount }).ToList(),
                Asks = snapshot.Asks.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount }).ToList()
            };
            return dto;
        }

        public async Task<bool> CancelOrderAsync(int orderId, int userId = 0)
        {
            return await CancelDomainOrderAsync(orderId);
        }

        public Task<bool> CanMatchOrderAsync(OrderDto buyOrder, OrderDto sellOrder)
        {
            var buy = new Order { Id = buyOrder.Id, Side = (OrderSide)buyOrder.Side, Price = buyOrder.Price, UserId = buyOrder.UserId };
            var sell = new Order { Id = sellOrder.Id, Side = (OrderSide)sellOrder.Side, Price = sellOrder.Price, UserId = sellOrder.UserId };
            return CanMatchOrderAsync(buy, sell);
        }

        // ================= 旧域逻辑入口重命名 (原 public 方法改 internal/private) =================
        private async Task<OrderMatchResult> ProcessDomainOrderAsync(Order order)
        {
            // 原 ProcessOrderAsync 主体保留，这里调用其主体实现 —— 为简洁使用现有主体代码
            return await ProcessOrderCoreAsync(order);
        }

        private async Task<List<Trade>> MatchDomainOrdersAsync(string symbol)
        {
            return await MatchOrdersCoreAsync(symbol);
        }

        private async Task<OrderBookDepthDomain> GetOrderBookDepthDomainAsync(string symbol, int depth)
        {
            return await GetOrderBookDepthCoreAsync(symbol, depth);
        }

        private async Task<bool> CancelDomainOrderAsync(int orderId)
        {
            return await CancelOrderCoreAsync(orderId);
        }

        // ============== 以下为抽取出的 Core 实现骨架，需要将原来的实现主体迁移/替换 ==============
        private async Task<OrderMatchResult> ProcessOrderCoreAsync(Order order)
        {
            var result = new OrderMatchResult { Order = order };
            string symbol = string.Empty;

            // 新增: 记录增量受影响价位集合
            var impactedBidPrices = new HashSet<decimal>();
            var impactedAskPrices = new HashSet<decimal>();
            List<OrderBookLevelDomain> bidDeltaLevels = new();
            List<OrderBookLevelDomain> askDeltaLevels = new();

            try
            {
                // 获取交易对符号
                var tradingPairResp = await _tradingPairService.GetTradingPairByIdAsync(order.TradingPairId);
                if (!tradingPairResp.Success || tradingPairResp.Data == null)
                {
                    _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", order.TradingPairId);
                    order.Status = OrderStatus.Rejected;
                    await _orderRawAccess.UpdateOrderStatusRawAsync(order.Id, OrderStatus.Rejected);
                    return result;
                }
                var tradingPair = tradingPairResp.Data;
                symbol = tradingPair.Symbol;
                
                // 获取或创建该交易对的锁
                var symbolLock = GetSymbolLock(symbol);
                
                await symbolLock.WaitAsync();
                try
                {
                    // 如果是市价单，立即匹配
                    if (order.Type == OrderType.Market)
                    {
                        result.Trades = await MatchMarketOrderAsync(order, impactedBidPrices, impactedAskPrices);
                    }
                    else
                    {
                        // 限价单先尝试匹配，未匹配部分进入订单簿
                        result.Trades = await MatchLimitOrderAsync(order, impactedBidPrices, impactedAskPrices);
                    }

                    // 更新订单状态
                    await UpdateOrderStatusAfterMatch(order, result.Trades);

                    // 在锁内构建增量层级，保证一致性
                    if (impactedBidPrices.Count > 0 || impactedAskPrices.Count > 0)
                    {
                        var activeOrders = await _orderRawAccess.GetActiveOrdersRawAsync(symbol);
                        bidDeltaLevels = AggregateLevels(activeOrders, impactedBidPrices, OrderSide.Buy);
                        askDeltaLevels = AggregateLevels(activeOrders, impactedAskPrices, OrderSide.Sell);
                    }
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
                    await _orderRawAccess.UpdateOrderStatusRawAsync(order.Id, OrderStatus.Rejected);
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "更新订单状态失败: OrderId={OrderId}", order.OrderId);
                }
            }

            // 推送增量订单簿 (替换原先每次全量推送)
            if (!string.IsNullOrEmpty(symbol))
            {
                try
                {
                    var realTimeDataPushService = _serviceProvider.GetRequiredService<IRealTimeDataPushService>();
                    if (bidDeltaLevels.Count > 0 || askDeltaLevels.Count > 0)
                    {
                        // 将 Domain level 转换为 DTO level
                        var bidDto = bidDeltaLevels.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount });
                        var askDto = askDeltaLevels.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount });
                        await realTimeDataPushService.PushOrderBookDeltaAsync(symbol, bidDto.ToList(), askDto.ToList());
                    }
                    else
                    {
                        var depthSnapshot = await GetOrderBookDepthDomainAsync(symbol, 20);
                        var depthDto = new OrderBookDepthDto
                        {
                            Symbol = depthSnapshot.Symbol,
                            Timestamp = depthSnapshot.Timestamp,
                            Bids = depthSnapshot.Bids.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount }).ToList(),
                            Asks = depthSnapshot.Asks.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount }).ToList()
                        };
                        await realTimeDataPushService.PushOrderBookDataAsync(symbol, depthDto);
                    }

                    // 计算并推送最新成交价与中间价
                    decimal? lastPrice = result.Trades?.LastOrDefault()?.Price;
                    decimal? lastQty = result.Trades?.LastOrDefault()?.Quantity;

                    // 仅在订单簿变化或有成交时读取当前顶级价
                    if (bidDeltaLevels.Count > 0 || askDeltaLevels.Count > 0 || lastPrice.HasValue)
                    {
                        var depthTop = await GetOrderBookDepthDomainAsync(symbol, 1); // 只取顶层
                        decimal? bestBid = depthTop.Bids.FirstOrDefault()?.Price;
                        decimal? bestAsk = depthTop.Asks.FirstOrDefault()?.Price;
                        decimal? mid = (bestBid.HasValue && bestAsk.HasValue && bestBid > 0 && bestAsk > 0) ? (bestBid + bestAsk) / 2m : null;
                        await realTimeDataPushService.PushLastTradeAndMidPriceAsync(symbol, lastPrice, lastQty, bestBid, bestAsk, mid, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "推送订单簿/价格数据失败: Symbol={Symbol}", symbol);
                }
            }

            return result;
        }

        private async Task<List<Trade>> MatchOrdersCoreAsync(string symbol)
        {
            var trades = new List<Trade>();
            
            try
            {
                var symbolLock = GetSymbolLock(symbol);
                await symbolLock.WaitAsync();
                
                try
                {
                    // 获取活跃的买单和卖单
                    var activeOrders = await _orderRawAccess.GetActiveOrdersRawAsync(symbol);
                    
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
                                    var trade = await CreateTradeAsync(buyOrder, sellOrder, sellOrder.Price ?? 0, Math.Min(buyOrder.RemainingQuantity, sellOrder.RemainingQuantity));
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
                            var depthSnapshot = await GetOrderBookDepthDomainAsync(symbol, 20);
                            var depthDto = new OrderBookDepthDto
                            {
                                Symbol = depthSnapshot.Symbol,
                                Timestamp = depthSnapshot.Timestamp,
                                Bids = depthSnapshot.Bids.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount }).ToList(),
                                Asks = depthSnapshot.Asks.Select(l => new OrderBookLevelDto { Price = l.Price, Quantity = l.Quantity, Total = l.Total, OrderCount = l.OrderCount }).ToList()
                            };
                            await realTimeDataPushService.PushOrderBookDataAsync(symbol, depthDto);
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

        private async Task<OrderBookDepthDomain> GetOrderBookDepthCoreAsync(string symbol, int depth)
        {
            var orderBookDepth = new OrderBookDepthDomain { Symbol = symbol };

            try
            {
                var activeOrders = await _orderRawAccess.GetActiveOrdersRawAsync(symbol);
                
                var orderCount = activeOrders.Count();
                _logger.LogInformation($"📊 获取订单簿深度: Symbol={symbol}, 活跃订单数量={orderCount}, 请求深度={depth}");
                
                // 买单聚合
                var buyOrders = activeOrders
                    .Where(o => o.Side == OrderSide.Buy && o.Type == OrderType.Limit)
                    .GroupBy(o => o.Price)
                    .Select(g => new OrderBookLevelDomain
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
                    .Select(g => new OrderBookLevelDomain
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

        private async Task<bool> CancelOrderCoreAsync(int orderId)
        {
            try
            {
                var order = await _orderRawAccess.GetOrderRawAsync(orderId);
                if (order == null || order.Status != OrderStatus.Pending && order.Status != OrderStatus.PartiallyFilled)
                {
                    return false;
                }

                // 解冻资产
                await UnfreezeOrderAssets(order);

                // 更新订单状态
                await _orderRawAccess.CancelOrderRawAsync(orderId);

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

        private async Task<List<Trade>> MatchMarketOrderAsync(Order marketOrder, HashSet<decimal>? impactedBidPrices = null, HashSet<decimal>? impactedAskPrices = null)
        {
            var trades = new List<Trade>();
            // 获取交易对符号 (DTO)
            var tradingPairResp = await _tradingPairService.GetTradingPairByIdAsync(marketOrder.TradingPairId);
            if (!tradingPairResp.Success || tradingPairResp.Data == null)
            {
                _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", marketOrder.TradingPairId);
                return trades;
            }
            var symbol = tradingPairResp.Data.Symbol;
            
            // 获取对手方订单
            var activeOrders = await _orderRawAccess.GetActiveOrdersRawAsync(symbol);
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

                    // 增量更新买/卖双方订单
                    if (marketOrder.Side == OrderSide.Buy)
                    {
                        await _orderRawAccess.UpdateOrderStatusRawAsync(marketOrder.Id, marketOrder.Status, matchQuantity, matchPrice);
                        await _orderRawAccess.UpdateOrderStatusRawAsync(oppositeOrder.Id, oppositeOrder.Status, matchQuantity, matchPrice);
                    }
                    else
                    {
                        await _orderRawAccess.UpdateOrderStatusRawAsync(oppositeOrder.Id, oppositeOrder.Status, matchQuantity, matchPrice);
                        await _orderRawAccess.UpdateOrderStatusRawAsync(marketOrder.Id, marketOrder.Status, matchQuantity, matchPrice);
                    }

                    // 记录受影响价位（对手方价位）
                    RegisterImpacted(oppositeOrder, impactedBidPrices, impactedAskPrices);
                    remainingQuantity -= matchQuantity;
                }
            }

            // 市价单若未完全成交，直接取消（也可选择 Rejected）
            if (remainingQuantity > 0)
            {
                _logger.LogWarning("市价单未完全成交，剩余数量={Remaining}，订单将被取消: OrderId={OrderId}", remainingQuantity, marketOrder.OrderId);
                await _orderRawAccess.UpdateOrderStatusRawAsync(marketOrder.Id, OrderStatus.Cancelled);
            }

            return trades;
        }

        private async Task<List<Trade>> MatchLimitOrderAsync(Order limitOrder, HashSet<decimal>? impactedBidPrices = null, HashSet<decimal>? impactedAskPrices = null)
        {
            var trades = new List<Trade>();
            var tradingPairResp = await _tradingPairService.GetTradingPairByIdAsync(limitOrder.TradingPairId);
            if (!tradingPairResp.Success || tradingPairResp.Data == null)
            {
                _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", limitOrder.TradingPairId);
                return trades;
            }
            var symbol = tradingPairResp.Data.Symbol;
            
            // 获取可匹配的对手方订单
            var activeOrders = await _orderRawAccess.GetActiveOrdersRawAsync(symbol);
            _logger.LogDebug("找到 {Count} 个活跃订单", activeOrders.Count());
            
            var matchableOrders = activeOrders
                .Where(o => o.Side != limitOrder.Side && o.Type == OrderType.Limit)
                .Where(o => limitOrder.Side == OrderSide.Buy ? o.Price <= limitOrder.Price : o.Price >= limitOrder.Price)
                .OrderBy(o => limitOrder.Side == OrderSide.Buy ? o.Price : -o.Price)
                .ThenBy(o => o.CreatedAt)
                .ToList();
                
            _logger.LogDebug("找到 {Count} 个可匹配订单", matchableOrders.Count());

            var remainingQuantity = limitOrder.RemainingQuantity; // 使用当前剩余(已可能被部分成交)

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

                    if (limitOrder.Side == OrderSide.Buy)
                    {
                        await _orderRawAccess.UpdateOrderStatusRawAsync(limitOrder.Id, limitOrder.Status, matchQuantity, matchPrice);
                        await _orderRawAccess.UpdateOrderStatusRawAsync(oppositeOrder.Id, oppositeOrder.Status, matchQuantity, matchPrice);
                    }
                    else
                    {
                        await _orderRawAccess.UpdateOrderStatusRawAsync(oppositeOrder.Id, oppositeOrder.Status, matchQuantity, matchPrice);
                        await _orderRawAccess.UpdateOrderStatusRawAsync(limitOrder.Id, limitOrder.Status, matchQuantity, matchPrice);
                    }

                    // 记录双方价位
                    RegisterImpacted(limitOrder, impactedBidPrices, impactedAskPrices);
                    RegisterImpacted(oppositeOrder, impactedBidPrices, impactedAskPrices);
                    remainingQuantity -= matchQuantity;
                }
            }

            // 如果剩余部分进入订单簿（新增价位或更新价格层汇总）
            if (limitOrder.RemainingQuantity > 0)
            {
                RegisterImpacted(limitOrder, impactedBidPrices, impactedAskPrices);
            }

            // 限价单剩余部分保持 Active (由前面 CreateOrder 已设 Active)；若完全成交由增量更新内部已自动变为 Filled
            return trades;
        }

        private async Task<Trade?> CreateTradeAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
        {
            try
            {
                var tradeResp = await _tradeService.ExecuteTradeAsync(new ExecuteTradeRequestDto { BuyOrderId = buyOrder.Id, SellOrderId = sellOrder.Id, Price = price, Quantity = quantity });
                if (tradeResp.Success && tradeResp.Data != null)
                {
                    var dto = tradeResp.Data;
                    var ts = new DateTimeOffset(dto.ExecutedAt).ToUnixTimeMilliseconds();
                    var tradeDomain = new Trade
                    {
                        BuyOrderId = buyOrder.Id,
                        SellOrderId = sellOrder.Id,
                        BuyerId = buyOrder.UserId ?? 0,
                        SellerId = sellOrder.UserId ?? 0,
                        TradingPairId = buyOrder.TradingPairId,
                        TradeId = dto.TradeId,
                        Price = dto.Price,
                        Quantity = dto.Quantity,
                        Fee = dto.Fee,
                        FeeAsset = dto.FeeAsset ?? "USDT",
                        ExecutedAt = ts,
                        CreatedAt = ts,
                        UpdatedAt = ts
                    };
                    return tradeDomain;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建交易记录时出错(委托交易服务)");
                throw; // 向上抛出，避免静默失败
            }
        }

        private async Task UpdateOrderStatusAfterMatch(Order order, List<Trade> trades)
        {
            // 现在增量在匹配循环里已处理；这里只在完全无成交情况下保持状态或激活
            try
            {
                if (!trades.Any())
                {
                    if (order.Type == OrderType.Limit && order.Status == OrderStatus.Pending)
                    {
                        await _orderRawAccess.UpdateOrderStatusRawAsync(order.Id, OrderStatus.Active);
                    }
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
                await _orderRawAccess.UpdateOrderStatusRawAsync(order.Id, newStatus, order.FilledQuantity);
            }
        }

        private async Task ProcessAssetChanges(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
        {
            // 已由交易服务统一处理，这里留空以避免重复结算
            await Task.CompletedTask;
        }

        private async Task UnfreezeOrderAssets(Order order)
        {
            var tradingPairResp = await _tradingPairService.GetTradingPairByIdAsync(order.TradingPairId);
            if (!tradingPairResp.Success || tradingPairResp.Data == null)
            {
                _logger.LogError("Trading pair not found for TradingPairId: {TradingPairId}", order.TradingPairId);
                return;
            }
            var symbol = tradingPairResp.Data.Symbol;
            var remainingQuantity = order.RemainingQuantity;
            
            if (order.Side == OrderSide.Buy)
            {
                // 买单解冻USDT
                var unfreezeAmount = remainingQuantity * (order.Price ?? 0);
                if (order.UserId.HasValue)
                {
                    // 统一使用AssetService处理所有用户资产
                    await _assetService.UnfreezeAssetAsync(order.UserId.Value, new AssetOperationRequestDto { Symbol = "USDT", Amount = unfreezeAmount });
                }
            }
            else
            {
                // 卖单解冻基础资产
                var baseAsset = symbol.Replace("USDT", string.Empty);
                if (order.UserId.HasValue)
                {
                    // 统一使用AssetService处理所有用户资产
                    await _assetService.UnfreezeAssetAsync(order.UserId.Value, new AssetOperationRequestDto { Symbol = baseAsset, Amount = remainingQuantity });
                }
            }
        }

        // 新增: 汇总指定价位集合对应的订单簿层
        private List<OrderBookLevelDomain> AggregateLevels(IEnumerable<Order> activeOrders, HashSet<decimal> prices, OrderSide side)
        {
            var list = new List<OrderBookLevelDomain>();
            foreach (var price in prices)
            {
                var sideOrders = activeOrders.Where(o => o.Side == side && o.Type == OrderType.Limit && o.Price == price).ToList();
                if (!sideOrders.Any())
                {
                    list.Add(new OrderBookLevelDomain
                    {
                        Price = price,
                        Quantity = 0,
                        OrderCount = 0,
                        Total = 0
                    });
                }
                else
                {
                    var qty = sideOrders.Sum(o => o.RemainingQuantity);
                    list.Add(new OrderBookLevelDomain
                    {
                        Price = price,
                        Quantity = qty,
                        OrderCount = sideOrders.Count,
                        Total = qty
                    });
                }
            }
            // 买单按价格降序, 卖单按升序
            if (side == OrderSide.Buy)
                return list.OrderByDescending(l => l.Price).ToList();
            return list.OrderBy(l => l.Price).ToList();
        }

        // 新增: 记录受影响价位
        private static void RegisterImpacted(Order order, HashSet<decimal>? bidSet, HashSet<decimal>? askSet)
        {
            if (order.Type != OrderType.Limit) return;
            if (!order.Price.HasValue) return;
            if (order.Side == OrderSide.Buy)
                bidSet?.Add(order.Price.Value);
            else
                askSet?.Add(order.Price.Value);
        }

        #endregion

        // ============== 为保持编译，定义内部使用的旧结构（后续可完全删除） ==============
        private class OrderMatchResult
        {
            public Order Order { get; set; } = null!;
            public List<Trade> Trades { get; set; } = new();
            public bool IsFullyMatched { get; set; }
            public decimal TotalMatchedQuantity { get; set; }
            public decimal AveragePrice { get; set; }
        }
        private class OrderBookDepthDomain
        {
            public string Symbol { get; set; } = string.Empty;
            public List<OrderBookLevelDomain> Bids { get; set; } = new();
            public List<OrderBookLevelDomain> Asks { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        }
        private class OrderBookLevelDomain
        {
            public decimal Price { get; set; }
            public decimal Quantity { get; set; }
            public decimal Total { get; set; }
            public int OrderCount { get; set; }
        }
    }
}
