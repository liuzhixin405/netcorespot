using CryptoSpot.Domain.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Interfaces.Trading;
using Microsoft.Extensions.Logging;
using CryptoSpot.Core.Interfaces.Users; // 为 IAssetService 所在命名空间(如果不正确请调整)

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 重构后的交易服务 - 使用新的仓储模式
    /// </summary>
    public class RefactoredTradeService : ITradeService
    {
        private readonly ITradeRepository _tradeRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAssetService _assetService; // 新增: 资产结算
        private readonly ILogger<RefactoredTradeService> _logger;

        public RefactoredTradeService(
            ITradeRepository tradeRepository,
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            IAssetService assetService, // 注入资产服务
            ILogger<RefactoredTradeService> logger)
        {
            _tradeRepository = tradeRepository;
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _assetService = assetService; // 赋值
            _logger = logger;
        }

        public async Task<Trade> ExecuteTradeAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
        {
            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var trade = new Trade
                    {
                        BuyOrderId = buyOrder.Id,
                        SellOrderId = sellOrder.Id,
                        BuyerId = buyOrder.UserId ?? 0,
                        SellerId = sellOrder.UserId ?? 0,
                        TradingPairId = buyOrder.TradingPairId,
                        TradeId = GenerateTradeId(),
                        Price = price,
                        Quantity = quantity,
                        Fee = CalculateFee(price, quantity),
                        FeeAsset = "USDT",
                        ExecutedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    var createdTrade = await _tradeRepository.AddAsync(trade);

                    // 资产结算（不再这里修改订单成交量/状态，避免与订单服务重复计算）
                    var tradingPair = await _tradingPairRepository.GetByIdAsync(buyOrder.TradingPairId);
                    if (tradingPair != null)
                    {
                        var baseAsset = tradingPair.BaseAsset;
                        var quoteAsset = tradingPair.QuoteAsset;
                        var notional = price * quantity;

                        if (buyOrder.UserId.HasValue)
                        {
                            var bd = await _assetService.DeductAssetAsync(buyOrder.UserId.Value, quoteAsset, notional, fromFrozen: true);
                            if (!bd) throw new InvalidOperationException($"买家资产扣减失败(User={buyOrder.UserId}, {quoteAsset} {notional})");
                            var ba = await _assetService.AddAssetAsync(buyOrder.UserId.Value, baseAsset, quantity);
                            if (!ba) throw new InvalidOperationException($"买家基础资产增加失败(User={buyOrder.UserId}, {baseAsset} {quantity})");
                        }
                        if (sellOrder.UserId.HasValue)
                        {
                            var sd = await _assetService.DeductAssetAsync(sellOrder.UserId.Value, baseAsset, quantity, fromFrozen: true);
                            if (!sd) throw new InvalidOperationException($"卖家资产扣减失败(User={sellOrder.UserId}, {baseAsset} {quantity})");
                            var sa = await _assetService.AddAssetAsync(sellOrder.UserId.Value, quoteAsset, notional);
                            if (!sa) throw new InvalidOperationException($"卖家报价资产增加失败(User={sellOrder.UserId}, {quoteAsset} {notional})");
                        }
                    }

                    await _unitOfWork.SaveChangesAsync();
                    _logger.LogInformation("交易执行成功: {TradeId}, 价格: {Price}, 数量: {Quantity}", trade.TradeId, price, quantity);
                    return createdTrade;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行交易时出错: BuyOrder={BuyOrderId}, SellOrder={SellOrderId}", buyOrder.OrderId, sellOrder.OrderId);
                throw;
            }
        }

        public async Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100)
        {
            return await _tradeRepository.GetTradeHistoryAsync(userId, symbol, limit);
        }

        public async Task<IEnumerable<Trade>> GetUserTradesAsync(int userId, string symbol = "", int limit = 100)
        {
            return await _tradeRepository.GetTradeHistoryAsync(userId, string.IsNullOrEmpty(symbol) ? null : symbol, limit);
        }

        public async Task<IEnumerable<Trade>> GetRecentTradesAsync(string symbol, int limit = 50)
        {
            return await _tradeRepository.GetRecentTradesAsync(symbol, limit);
        }

        public async Task<Trade?> GetTradeByIdAsync(long tradeId)
        {
            return await _tradeRepository.GetByIdAsync((int)tradeId);
        }

        public async Task<IEnumerable<Trade>> GetTradesByOrderIdAsync(int orderId)
        {
            return await _tradeRepository.FindAsync(t => t.BuyOrderId == orderId || t.SellOrderId == orderId);
        }

        public async Task<decimal> GetTradingVolumeAsync(string symbol, TimeSpan timeRange)
        {
            var startTime = DateTimeOffset.UtcNow.Add(-timeRange).ToUnixTimeMilliseconds();
            var trades = await _tradeRepository.FindAsync(t => t.ExecutedAt >= startTime);
            return trades.Sum(t => t.Price * t.Quantity);
        }

        public async Task<(decimal high, decimal low)> GetPriceRangeAsync(string symbol, TimeSpan timeRange)
        {
            var startTime = DateTimeOffset.UtcNow.Add(-timeRange).ToUnixTimeMilliseconds();
            var trades = await _tradeRepository.FindAsync(t => t.ExecutedAt >= startTime);
            
            if (!trades.Any())
                return (0, 0);
                
            var prices = trades.Select(t => t.Price);
            return (prices.Max(), prices.Min());
        }

        private string GenerateTradeId()
        {
            return $"TRD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";
        }

        private decimal CalculateFee(decimal price, decimal quantity)
        {
            // 简单的费率计算，实际应该根据交易对和用户等级计算
            return price * quantity * 0.001m; // 0.1% 手续费
        }
    }
}
