using CryptoSpot.Core.Commands.Trading;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Entities;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

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

        public RefactoredTradingService(
            ICommandBus commandBus,
            ITradingPairService tradingPairService,
            IOrderService orderService,
            ITradeService tradeService,
            ILogger<RefactoredTradingService> logger)
        {
            _commandBus = commandBus;
            _tradingPairService = tradingPairService;
            _orderService = orderService;
            _tradeService = tradeService;
            _logger = logger;
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

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string timeFrame, int limit = 100)
        {
            try
            {
                // 这里应该调用K线数据服务
                // 为了保持接口兼容性，暂时返回空列表
                return new List<KLineData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data for {Symbol} {TimeFrame}", symbol, timeFrame);
                return new List<KLineData>();
            }
        }

        public async Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId)
        {
            try
            {
                // 这里应该调用资产服务
                // 为了保持接口兼容性，暂时返回空列表
                return new List<Asset>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user assets for user {UserId}", userId);
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
