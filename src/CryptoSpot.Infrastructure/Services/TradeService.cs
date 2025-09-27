using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Trading;
using CryptoSpot.Application.Abstractions.Users;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 交易服务基础设施实现（由 Application.Services.RefactoredTradeService 迁移）
    /// </summary>
    public class TradeService : ITradeService
    {
        private readonly ITradeRepository _tradeRepository;
        private readonly IOrderRepository _orderRepository; // 预留: 若未来需要直接更新订单
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAssetService _assetService;
        private readonly ILogger<TradeService> _logger;
        private readonly IMarketMakerRegistry _marketMakerRegistry; // 新增

        public TradeService(
            ITradeRepository tradeRepository,
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            IAssetService assetService,
            ILogger<TradeService> logger,
            IMarketMakerRegistry marketMakerRegistry) // 新增
        {
            _tradeRepository = tradeRepository;
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _assetService = assetService;
            _logger = logger;
            _marketMakerRegistry = marketMakerRegistry;
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

                    // 资产结算
                    var tradingPair = await _tradingPairRepository.GetByIdAsync(buyOrder.TradingPairId);
                    if (tradingPair != null)
                    {
                        var baseAsset = tradingPair.BaseAsset;
                        var quoteAsset = tradingPair.QuoteAsset;
                        var notional = price * quantity;

                        if (buyOrder.UserId.HasValue)
                        {
                            var buyIsMaker = _marketMakerRegistry.IsMaker(buyOrder.UserId.Value);
                            var bd = await _assetService.DeductAssetAsync(buyOrder.UserId.Value, quoteAsset, notional, fromFrozen: !buyIsMaker);
                            if (!bd) throw new InvalidOperationException($"买家资产扣减失败(User={buyOrder.UserId}, {quoteAsset} {notional}, fromFrozen={!buyIsMaker})");
                            var ba = await _assetService.AddAssetAsync(buyOrder.UserId.Value, baseAsset, quantity);
                            if (!ba) throw new InvalidOperationException($"买家基础资产增加失败(User={buyOrder.UserId}, {baseAsset} {quantity})");
                        }
                        if (sellOrder.UserId.HasValue)
                        {
                            var sellIsMaker = _marketMakerRegistry.IsMaker(sellOrder.UserId.Value);
                            var sd = await _assetService.DeductAssetAsync(sellOrder.UserId.Value, baseAsset, quantity, fromFrozen: !sellIsMaker);
                            if (!sd) throw new InvalidOperationException($"卖家资产扣减失败(User={sellOrder.UserId}, {baseAsset} {quantity}, fromFrozen={!sellIsMaker})");
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

        public Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100) => _tradeRepository.GetTradeHistoryAsync(userId, symbol, limit);

        public Task<IEnumerable<Trade>> GetUserTradesAsync(int userId, string symbol = "", int limit = 100) => _tradeRepository.GetTradeHistoryAsync(userId, string.IsNullOrEmpty(symbol) ? null : symbol, limit);

        public Task<IEnumerable<Trade>> GetRecentTradesAsync(string symbol, int limit = 50) => _tradeRepository.GetRecentTradesAsync(symbol, limit);

        public Task<Trade?> GetTradeByIdAsync(long tradeId) => _tradeRepository.GetByIdAsync((int)tradeId);

        public Task<IEnumerable<Trade>> GetTradesByOrderIdAsync(int orderId) => _tradeRepository.FindAsync(t => t.BuyOrderId == orderId || t.SellOrderId == orderId);

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
            if (!trades.Any()) return (0, 0);
            var prices = trades.Select(t => t.Price);
            return (prices.Max(), prices.Min());
        }

        private string GenerateTradeId() => $"TRD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";
        private decimal CalculateFee(decimal price, decimal quantity) => price * quantity * 0.001m; // 0.1%
    }
}