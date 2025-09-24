using CryptoSpot.Core.Commands.Trading;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Entities;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;
using CryptoSpot.Core.Interfaces.Users; // 新增 引入资产服务接口

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 重构后的交易服务 - 使用命令总线处理高频数据
    /// </summary>
    public class RefactoredTradingService : ITradingService
    {
        private readonly ICommandBus _commandBus;
        private readonly ITradingPairService _tradingPairService;
        private readonly IOrderService _orderService;
        private readonly ITradeService _tradeService;
        private readonly ILogger<RefactoredTradingService> _logger;
        private readonly IOrderMatchingEngine _matchingEngine; // 获取订单簿深度
        private readonly IAssetService _assetService; // 新增 资产服务

        public RefactoredTradingService(
            ICommandBus commandBus,
            ITradingPairService tradingPairService,
            IOrderService orderService,
            ITradeService tradeService,
            ILogger<RefactoredTradingService> logger,
            IOrderMatchingEngine matchingEngine,
            IAssetService assetService) // 新增注入
        {
            _commandBus = commandBus;
            _tradingPairService = tradingPairService;
            _orderService = orderService;
            _tradeService = tradeService;
            _logger = logger;
            _matchingEngine = matchingEngine;
            _assetService = assetService; // 赋值
        }

        public async Task<IEnumerable<TradingPair>> GetTradingPairsAsync()
        {
            try
            {
                return await _tradingPairService.GetTopTradingPairsAsync(5);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading pairs");
                return new List<TradingPair>();
            }
        }

        public async Task<TradingPair?> GetTradingPairAsync(string symbol)
        {
            try
            {
                return await _tradingPairService.GetTradingPairAsync(symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading pair {Symbol}", symbol);
                return null;
            }
        }

        public Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string timeFrame, int limit = 100)
        {
            // TODO: 集成真正的K线服务
            return Task.FromResult<IEnumerable<KLineData>>(new List<KLineData>());
        }

        public async Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId)
        {
            try
            {
                return await _assetService.GetUserAssetsAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user assets for {UserId}", userId);
                return new List<Asset>();
            }
        }

        public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, string? symbol = null)
        {
            try
            {
                return await _orderService.GetUserOrdersAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user orders for user {UserId}", userId);
                return new List<Order>();
            }
        }

        public async Task<IEnumerable<Trade>> GetUserTradesAsync(int userId, string? symbol = null)
        {
            try
            {
                return await _tradeService.GetTradeHistoryAsync(userId, symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user trades for user {UserId}", userId);
                return new List<Trade>();
            }
        }

        public async Task<Order?> SubmitOrderAsync(int userId, SubmitOrderRequest request)
        {
            try
            {
                // 使用命令总线处理订单提交
                var command = new SubmitOrderCommand
                {
                    UserId = userId,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    Type = request.Type,
                    Quantity = request.Quantity,
                    Price = request.Price,
                    ClientOrderId = request.ClientOrderId
                };

                var result = await _commandBus.SendAsync<SubmitOrderCommand, SubmitOrderResult>(command);

                if (result.Success && result.OrderId.HasValue)
                {
                    // 返回创建的订单
                    return await _orderService.GetOrderByIdAsync(result.OrderId.Value, userId);
                }

                _logger.LogWarning("Order submission failed for user {UserId}: {ErrorMessage}", 
                    userId, result.ErrorMessage);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting order for user {UserId}", userId);
                return null;
            }
        }

        public async Task<bool> CancelOrderAsync(int userId, int orderId)
        {
            try
            {
                // 使用命令总线处理订单取消
                var command = new CancelOrderCommand
                {
                    UserId = userId,
                    OrderId = orderId
                };

                var result = await _commandBus.SendAsync<CancelOrderCommand, CancelOrderResult>(command);

                if (result.Success)
                {
                    _logger.LogInformation("Order {OrderId} cancelled by user {UserId}", orderId, userId);
                }
                else
                {
                    _logger.LogWarning("Failed to cancel order {OrderId} for user {UserId}: {ErrorMessage}", 
                        orderId, userId, result.ErrorMessage);
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling order {OrderId} for user {UserId}", orderId, userId);
                return false;
            }
        }

        public async Task<Order?> GetOrderAsync(int userId, int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId, userId);
            return order;
        }

        public async Task<IEnumerable<Trade>> GetOrderTradesAsync(int userId, int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId, userId);
            if (order == null) return Enumerable.Empty<Trade>();
            return await _tradeService.GetOrderTradesAsync(orderId);
        }

        public async Task<IEnumerable<Order>> GetOpenOrdersAsync(int userId, string? symbol = null)
        {
            var all = await _orderService.GetUserOrdersAsync(userId, null, 500);
            return all.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled)
                      .Where(o => string.IsNullOrEmpty(symbol) || (o.TradingPair != null && o.TradingPair.Symbol == symbol));
        }

        public async Task<bool> CancelAllOrdersAsync(int userId, string? symbol = null)
        {
            var openOrders = await GetOpenOrdersAsync(userId, symbol);
            var success = true;
            foreach (var o in openOrders)
            {
                var cancelled = await CancelOrderAsync(userId, o.Id);
                if (!cancelled) success = false;
            }
            return success;
        }

        public async Task<OrderBookDepth> GetOrderBookDepthAsync(string symbol, int depth = 20)
        {
            // 利用撮合引擎聚合逻辑
            var depthData = await _matchingEngine.GetOrderBookDepthAsync(symbol, depth);
            return new OrderBookDepth
            {
                Symbol = depthData.Symbol,
                Bids = depthData.Bids.Select(b => new OrderBookLevel { Price = b.Price, Quantity = b.Quantity, OrderCount = b.OrderCount, Total = b.Total }).ToList(),
                Asks = depthData.Asks.Select(a => new OrderBookLevel { Price = a.Price, Quantity = a.Quantity, OrderCount = a.OrderCount, Total = a.Total }).ToList()
            };
        }

        public async Task<TestOrderResult> TestOrderAsync(int userId, SubmitOrderRequest request)
        {
            var result = new TestOrderResult { Success = false, NormalizedRequest = request };
            try
            {
                if (request.Quantity <= 0)
                {
                    result.Message = "数量必须大于0";
                    return result;
                }
                if (request.Type == OrderType.Limit && (!request.Price.HasValue || request.Price <= 0))
                {
                    result.Message = "限价单需提供有效价格";
                    return result;
                }
                var pair = await _tradingPairService.GetTradingPairAsync(request.Symbol);
                if (pair == null)
                {
                    result.Message = "交易对不存在";
                    return result;
                }
                decimal? needQuote = null; decimal? needBase = null;
                if (request.Side == OrderSide.Buy)
                {
                    if (request.Type == OrderType.Limit)
                        needQuote = request.Quantity * request.Price!.Value;
                    // 市价买单无法准确预估，这里简单返回0或留空
                }
                else
                {
                    needBase = request.Quantity;
                }
                result.RequiredQuoteAmount = needQuote;
                result.RequiredBaseAmount = needBase;
                result.Success = true;
                result.Message = "OK";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试下单失败");
                result.Message = "内部错误";
            }
            return result;
        }

        /// <summary>
        /// 批量更新价格 - 专门用于高频价格更新
        /// </summary>
        public async Task UpdatePricesAsync(IEnumerable<PriceUpdateData> priceUpdates)
        {
            try
            {
                var tasks = priceUpdates.Select(update => 
                    _commandBus.SendAsync<UpdatePriceCommand, UpdatePriceResult>(new UpdatePriceCommand
                    {
                        Symbol = update.Symbol,
                        Price = update.Price,
                        Change24h = update.Change24h,
                        Volume24h = update.Volume24h,
                        High24h = update.High24h,
                        Low24h = update.Low24h,
                        Timestamp = update.Timestamp
                    }));

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating prices in batch");
            }
        }

        /// <summary>
        /// 批量处理K线数据 - 专门用于高频K线数据处理
        /// </summary>
        public async Task ProcessKLineDataBatchAsync(IEnumerable<KLineDataUpdate> klineUpdates)
        {
            try
            {
                var tasks = klineUpdates.Select(update => 
                    _commandBus.SendAsync<ProcessKLineDataCommand, ProcessKLineDataResult>(new ProcessKLineDataCommand
                    {
                        Symbol = update.Symbol,
                        TimeFrame = update.TimeFrame,
                        KLineData = update.KLineData,
                        IsNewKLine = update.IsNewKLine
                    }));

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing KLine data batch");
            }
        }
    }

    /// <summary>
    /// 价格更新数据
    /// </summary>
    public class PriceUpdateData
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Change24h { get; set; }
        public decimal Volume24h { get; set; }
        public decimal High24h { get; set; }
        public decimal Low24h { get; set; }
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// K线数据更新
    /// </summary>
    public class KLineDataUpdate
    {
        public string Symbol { get; set; } = string.Empty;
        public string TimeFrame { get; set; } = string.Empty;
        public KLineData KLineData { get; set; } = null!;
        public bool IsNewKLine { get; set; }
    }
}
