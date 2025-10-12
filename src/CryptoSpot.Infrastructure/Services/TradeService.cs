using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Application.DTOs.Users; // 新增资产操作 DTO

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
        private readonly IAssetService _assetService; // 替换领域接口
        private readonly ILogger<TradeService> _logger;
        private readonly IMarketMakerRegistry _marketMakerRegistry; // 新增
        private readonly IDtoMappingService _mapping; // 新增映射

        public TradeService(
            ITradeRepository tradeRepository,
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            IAssetService assetService,
            ILogger<TradeService> logger,
            IMarketMakerRegistry marketMakerRegistry,
            IDtoMappingService mapping) // 注入映射
        {
            _tradeRepository = tradeRepository;
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _assetService = assetService;
            _logger = logger;
            _marketMakerRegistry = marketMakerRegistry;
            _mapping = mapping;
        }

        public async Task<Trade> ExecuteTradeRawAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
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
                            var bdResp = await _assetService.DeductAssetAsync(buyOrder.UserId.Value, new AssetOperationRequestDto { Symbol = quoteAsset, Amount = notional });
                            var bd = bdResp.Success && bdResp.Data;
                            if (!bd) throw new InvalidOperationException($"买家资产扣减失败(User={buyOrder.UserId}, {quoteAsset} {notional}, fromFrozen={!buyIsMaker})");
                            var baResp = await _assetService.AddAssetAsync(buyOrder.UserId.Value, new AssetOperationRequestDto { Symbol = baseAsset, Amount = quantity });
                            var ba = baResp.Success && baResp.Data;
                            if (!ba) throw new InvalidOperationException($"买家基础资产增加失败(User={buyOrder.UserId}, {baseAsset} {quantity})");
                        }
                        if (sellOrder.UserId.HasValue)
                        {
                            var sellIsMaker = _marketMakerRegistry.IsMaker(sellOrder.UserId.Value);
                            var sdResp = await _assetService.DeductAssetAsync(sellOrder.UserId.Value, new AssetOperationRequestDto { Symbol = baseAsset, Amount = quantity });
                            var sd = sdResp.Success && sdResp.Data;
                            if (!sd) throw new InvalidOperationException($"卖家资产扣减失败(User={sellOrder.UserId}, {baseAsset} {quantity}, fromFrozen={!sellIsMaker})");
                            var saResp = await _assetService.AddAssetAsync(sellOrder.UserId.Value, new AssetOperationRequestDto { Symbol = quoteAsset, Amount = notional });
                            var sa = saResp.Success && saResp.Data;
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

        // 旧 Raw 方法重构: 新增 DTO 执行接口
        public async Task<ApiResponseDto<TradeDto?>> ExecuteTradeAsync(ExecuteTradeRequestDto request)
        {
            try
            {
                var buyOrder = await _orderRepository.GetByIdAsync(request.BuyOrderId);
                var sellOrder = await _orderRepository.GetByIdAsync(request.SellOrderId);
                if (buyOrder == null || sellOrder == null)
                    return ApiResponseDto<TradeDto?>.CreateError("订单不存在，无法执行成交");

                var trade = await ExecuteTradeInternalAsync(buyOrder, sellOrder, request.Price, request.Quantity);
                return ApiResponseDto<TradeDto?>.CreateSuccess(_mapping.MapToDto(trade));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行撮合成交失败 BuyOrder={BuyOrderId} SellOrder={SellOrderId}", request.BuyOrderId, request.SellOrderId);
                return ApiResponseDto<TradeDto?>.CreateError("执行成交失败");
            }
        }

        private Task<Trade> ExecuteTradeInternalAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
            => ExecuteTradeRawAsync(buyOrder, sellOrder, price, quantity); // 复用原实现主体

        // ========== DTO 查询实现 ==========
        public async Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100)
        {
            try
            {
                var trades = await _tradeRepository.GetTradeHistoryAsync(userId, symbol, limit);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateSuccess(_mapping.MapToDto(trades));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易历史失败: User={UserId}", userId);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取交易历史失败");
            }
        }

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(int userId, string symbol = "", int limit = 100)
            => GetTradeHistoryAsync(userId, string.IsNullOrEmpty(symbol) ? null : symbol, limit);

        public async Task<ApiResponseDto<IEnumerable<TradeDto>>> GetRecentTradesAsync(string symbol, int limit = 50)
        {
            try
            {
                var trades = await _tradeRepository.GetRecentTradesAsync(symbol, limit);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateSuccess(_mapping.MapToDto(trades));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最近成交失败: Symbol={Symbol}", symbol);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取最近成交失败");
            }
        }

        public async Task<ApiResponseDto<TradeDto?>> GetTradeByIdAsync(long tradeId)
        {
            try
            {
                var trade = await _tradeRepository.GetByIdAsync((int)tradeId);
                return ApiResponseDto<TradeDto?>.CreateSuccess(trade == null ? null : _mapping.MapToDto(trade));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易记录失败: TradeId={TradeId}", tradeId);
                return ApiResponseDto<TradeDto?>.CreateError("获取交易记录失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradesByOrderIdAsync(int orderId)
        {
            try
            {
                var trades = await _tradeRepository.FindAsync(t => t.BuyOrderId == orderId || t.SellOrderId == orderId);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateSuccess(_mapping.MapToDto(trades));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单成交记录失败: OrderId={OrderId}", orderId);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取订单成交记录失败");
            }
        }

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetOrderTradesAsync(int orderId) => GetTradesByOrderIdAsync(orderId);

        public async Task<ApiResponseDto<decimal>> GetTradingVolumeAsync(string symbol, TimeSpan timeRange)
        {
            try
            {
                var startTime = DateTimeOffset.UtcNow.Add(-timeRange).ToUnixTimeMilliseconds();
                var trades = await _tradeRepository.FindAsync(t => t.ExecutedAt >= startTime);
                var volume = trades.Sum(t => t.Price * t.Quantity);
                return ApiResponseDto<decimal>.CreateSuccess(volume);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易量失败: Symbol={Symbol}", symbol);
                return ApiResponseDto<decimal>.CreateError("获取交易量失败");
            }
        }

        public async Task<ApiResponseDto<(decimal high, decimal low)>> GetPriceRangeAsync(string symbol, TimeSpan timeRange)
        {
            try
            {
                var startTime = DateTimeOffset.UtcNow.Add(-timeRange).ToUnixTimeMilliseconds();
                var trades = await _tradeRepository.FindAsync(t => t.ExecutedAt >= startTime);
                if (!trades.Any()) return ApiResponseDto<(decimal, decimal)>.CreateSuccess((0, 0));
                var prices = trades.Select(t => t.Price);
                return ApiResponseDto<(decimal, decimal)>.CreateSuccess((prices.Max(), prices.Min()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取价格区间失败: Symbol={Symbol}", symbol);
                return ApiResponseDto<(decimal, decimal)>.CreateError("获取价格区间失败");
            }
        }

        private string GenerateTradeId() => $"TRD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";
        private decimal CalculateFee(decimal price, decimal quantity) => price * quantity * 0.001m; // 0.1%
    }
}