using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Services
{
    public class TradingService : ITradingService
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IKLineDataRepository _klineDataRepository;
        private readonly IUserRepository _userRepository;
        private readonly IAssetService _assetService;
        private readonly IOrderService _orderService;
        private readonly ITradeService _tradeService;
        private readonly IOrderMatchingEngine _orderMatchingEngine;
        private readonly ILogger<TradingService> _logger;

        public TradingService(
            ITradingPairRepository tradingPairRepository,
            IKLineDataRepository klineDataRepository,
            IUserRepository userRepository,
            IAssetService assetService,
            IOrderService orderService,
            ITradeService tradeService,
            IOrderMatchingEngine orderMatchingEngine,
            ILogger<TradingService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _klineDataRepository = klineDataRepository;
            _userRepository = userRepository;
            _assetService = assetService;
            _orderService = orderService;
            _tradeService = tradeService;
            _orderMatchingEngine = orderMatchingEngine;
            _logger = logger;
        }

        public async Task<IEnumerable<TradingPair>> GetTradingPairsAsync()
        {
            try
            {
                return await _tradingPairRepository.GetTopPairsAsync(5);
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
                return await _tradingPairRepository.GetBySymbolAsync(symbol);
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
                return await _klineDataRepository.GetBySymbolAndTimeFrameAsync(symbol, timeFrame, limit);
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
                return await _assetService.GetUserAssetsAsync(userId);
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
                // 使用 TradeService 来获取用户交易记录
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
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found for order submission", userId);
                    return null;
                }

                // Validate order
                if (request.Quantity <= 0)
                {
                    _logger.LogWarning("Invalid quantity {Quantity} for order submission", request.Quantity);
                    return null;
                }

                if (request.Type == OrderType.Limit && (!request.Price.HasValue || request.Price.Value <= 0))
                {
                    _logger.LogWarning("Invalid price for limit order submission");
                    return null;
                }

                // 验证用户资产余额并冻结资产
                var (hasBalance, freezeSuccess) = await ValidateAndFreezeAssetsAsync(userId, request);
                if (!hasBalance)
                {
                    _logger.LogWarning("Insufficient balance for user {UserId} to place order", userId);
                    return null;
                }
                
                if (!freezeSuccess)
                {
                    _logger.LogWarning("Failed to freeze assets for user {UserId}", userId);
                    return null;
                }

                // Create order using OrderService
                var order = await _orderService.CreateOrderAsync(
                    userId, 
                    request.Symbol, 
                    request.Side, 
                    request.Type, 
                    request.Quantity, 
                    request.Price);

                // 触发订单匹配
                var matchResult = await _orderMatchingEngine.ProcessOrderAsync(order);
                
                _logger.LogInformation("Order {OrderId} processed, {TradeCount} trades executed", 
                    order.OrderId, matchResult.Trades.Count);

                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting order for user {UserId}", userId);
                return null;
            }
        }

        public async Task<bool> CancelOrderAsync(int userId, long orderId)
        {
            try
            {
                // 使用 OrderMatchingEngine 来取消订单，它会处理资产解冻等逻辑
                var success = await _orderMatchingEngine.CancelOrderAsync(orderId);
                
                if (success)
                {
                    _logger.LogInformation("Order {OrderId} cancelled by user {UserId}", orderId, userId);
                }
                else
                {
                    _logger.LogWarning("Failed to cancel order {OrderId} for user {UserId}", orderId, userId);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling order {OrderId} for user {UserId}", orderId, userId);
                return false;
            }
        }

        private async Task<(bool hasBalance, bool freezeSuccess)> ValidateAndFreezeAssetsAsync(int userId, SubmitOrderRequest request)
        {
            try
            {
                string assetToFreeze;
                decimal amountToFreeze;

                if (request.Side == OrderSide.Buy)
                {
                    // 买单需要冻结 USDT
                    assetToFreeze = "USDT";
                    amountToFreeze = request.Type == OrderType.Market 
                        ? request.Quantity * 50000 // 市价单估算，使用较高价格
                        : request.Quantity * (request.Price ?? 0);
                }
                else
                {
                    // 卖单需要冻结基础资产
                    assetToFreeze = request.Symbol.Replace("USDT", "");
                    amountToFreeze = request.Quantity;
                }

                // 检查余额
                var hasBalance = await _assetService.HasSufficientBalanceAsync(userId, assetToFreeze, amountToFreeze);
                if (!hasBalance)
                {
                    return (false, false);
                }

                // 冻结资产
                var freezeSuccess = await _assetService.FreezeAssetAsync(userId, assetToFreeze, amountToFreeze);
                
                return (hasBalance, freezeSuccess);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating and freezing assets for user {UserId}", userId);
                return (false, false);
            }
        }

    }
}
