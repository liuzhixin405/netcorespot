using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class TradeService : ITradeService
    {
        private readonly IRepository<Trade> _tradeRepository;
        private readonly IRepository<Order> _orderRepository;
        private readonly ITradingPairService _tradingPairService;
        private readonly ILogger<TradeService> _logger;

        public TradeService(
            IRepository<Trade> tradeRepository,
            IRepository<Order> orderRepository,
            ITradingPairService tradingPairService,
            ILogger<TradeService> logger)
        {
            _tradeRepository = tradeRepository;
            _orderRepository = orderRepository;
            _tradingPairService = tradingPairService;
            _logger = logger;
        }

        public async Task<Trade> ExecuteTradeAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
        {
            try
            {
                var trade = new Trade
                {
                    BuyOrderId = buyOrder.Id,
                    SellOrderId = sellOrder.Id,
                    TradingPairId = buyOrder.TradingPairId,
                    TradeId = GenerateTradeId(),
                    Price = price,
                    Quantity = quantity,
                    Fee = CalculateFee(price, quantity),
                    FeeAsset = "USDT", // 默认使用USDT作为手续费
                    ExecutedAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
                };

                var createdTrade = await _tradeRepository.AddAsync(trade);
                
                _logger.LogInformation("Executed trade {TradeId}: {Quantity} at {Price} for {Symbol}", 
                    trade.TradeId, quantity, price, buyOrder.TradingPair?.Symbol ?? "Unknown");

                return createdTrade;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing trade between orders {BuyOrderId} and {SellOrderId}", 
                    buyOrder.Id, sellOrder.Id);
                throw;
            }
        }

        public async Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100)
        {
            try
            {
                IEnumerable<Trade> trades;
                
                if (string.IsNullOrEmpty(symbol))
                {
                    // 获取用户所有交易（通过买单或卖单）
                    var buyTrades = await _tradeRepository.FindAsync(t => t.BuyOrder.UserId == userId);
                    var sellTrades = await _tradeRepository.FindAsync(t => t.SellOrder.UserId == userId);
                    
                    trades = buyTrades.Concat(sellTrades)
                        .Distinct()
                        .OrderByDescending(t => t.ExecutedAt)
                        .Take(limit);
                }
                else
                {
                    var tradingPair = await _tradingPairService.GetTradingPairAsync(symbol);
                    if (tradingPair == null)
                    {
                        return new List<Trade>();
                    }

                    var buyTrades = await _tradeRepository.FindAsync(t => 
                        t.TradingPairId == tradingPair.Id && t.BuyOrder.UserId == userId);
                    var sellTrades = await _tradeRepository.FindAsync(t => 
                        t.TradingPairId == tradingPair.Id && t.SellOrder.UserId == userId);
                    
                    trades = buyTrades.Concat(sellTrades)
                        .Distinct()
                        .OrderByDescending(t => t.ExecutedAt)
                        .Take(limit);
                }

                return trades;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trades for user {UserId}", userId);
                return new List<Trade>();
            }
        }

        public async Task<IEnumerable<Trade>> GetRecentTradesAsync(string symbol, int limit = 50)
        {
            try
            {
                var tradingPair = await _tradingPairService.GetTradingPairAsync(symbol);
                if (tradingPair == null)
                {
                    return new List<Trade>();
                }

                return await _tradeRepository.FindAsync(t => t.TradingPairId == tradingPair.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trades for symbol {Symbol}", symbol);
                return new List<Trade>();
            }
        }

        public async Task<decimal> GetTradingVolumeAsync(string symbol, TimeSpan timeRange)
        {
            try
            {
                var tradingPair = await _tradingPairService.GetTradingPairAsync(symbol);
                if (tradingPair == null) return 0;

                var fromTime = DateTimeExtensions.GetCurrentUnixTimeMilliseconds() - (long)timeRange.TotalMilliseconds;
                var trades = await _tradeRepository.FindAsync(t => 
                    t.TradingPairId == tradingPair.Id && t.ExecutedAt >= fromTime);
                    
                return trades.Sum(t => t.TotalValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating trading volume for symbol {Symbol}", symbol);
                return 0;
            }
        }

        public async Task<(decimal high, decimal low)> GetPriceRangeAsync(string symbol, TimeSpan timeRange)
        {
            try
            {
                var tradingPair = await _tradingPairService.GetTradingPairAsync(symbol);
                if (tradingPair == null) return (0, 0);

                var fromTime = DateTimeExtensions.GetCurrentUnixTimeMilliseconds() - (long)timeRange.TotalMilliseconds;
                var trades = await _tradeRepository.FindAsync(t => 
                    t.TradingPairId == tradingPair.Id && t.ExecutedAt >= fromTime);
                    
                if (!trades.Any())
                {
                    return (0, 0);
                }

                var prices = trades.Select(t => t.Price);
                return (prices.Max(), prices.Min());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting price range for symbol {Symbol}", symbol);
                return (0, 0);
            }
        }

        public async Task<Trade?> GetTradeByIdAsync(long tradeId)
        {
            try
            {
                return await _tradeRepository.GetByIdAsync(tradeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trade {TradeId}", tradeId);
                return null;
            }
        }

        public async Task<IEnumerable<Trade>> GetTradesByOrderIdAsync(int orderId)
        {
            try
            {
                var buyTrades = await _tradeRepository.FindAsync(t => t.BuyOrderId == orderId);
                var sellTrades = await _tradeRepository.FindAsync(t => t.SellOrderId == orderId);
                
                return buyTrades.Concat(sellTrades).Distinct().OrderBy(t => t.ExecutedAt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trades for order {OrderId}", orderId);
                return new List<Trade>();
            }
        }

        private string GenerateTradeId()
        {
            return $"TRD_{DateTime.Now:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";
        }

        private decimal CalculateFee(decimal price, decimal quantity)
        {
            // 0.1% 交易手续费
            const decimal feeRate = 0.001m;
            return price * quantity * feeRate;
        }
    }
}
