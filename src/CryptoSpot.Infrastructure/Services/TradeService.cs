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
            // ⚠️ 重要提示：此方法仅用于 MySQL 持久化，不应再操作资产
            // 资产操作已在 RedisOrderMatchingEngine.ExecuteTrade 中通过 Lua 脚本原子性完成
            // 如果在这里再次操作资产，会导致：
            // 1. 重复扣款/加款（用户资金翻倍损失）
            // 2. Redis-MySQL 数据不一致（MySQL 事务回滚但 Redis 已操作）
            
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

                    // ✅ 已移除资产操作代码（避免与 RedisOrderMatchingEngine 重复）
                    // 资产操作流程：
                    // 1. RedisOrderMatchingEngine.ExecuteTrade → 使用 Lua 脚本原子性操作 4 个资产
                    // 2. 自动加入 sync_queue:assets 队列
                    // 3. SyncAssetsCommandHandler → 异步同步到 MySQL
                    // 
                    // 如果此处再次操作资产，会破坏这个流程并导致数据不一致

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
                var tradeDtos = _mapping.MapToDto(trades).Select(dto =>
                {
                    // 根据用户ID判断交易方向
                    var trade = trades.FirstOrDefault(t => t.Id == dto.Id);
                    if (trade != null)
                    {
                        dto.Side = trade.BuyerId == userId ? Domain.Entities.OrderSide.Buy : Domain.Entities.OrderSide.Sell;
                    }
                    return dto;
                }).ToList();
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateSuccess(tradeDtos);
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

        // Removed unused trade detail / volume / price range APIs; re-added order trade queries for TradingService

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

        public async Task<ApiResponseDto<IEnumerable<MarketTradeDto>>> GetMarketRecentTradesAsync(string symbol, int limit = 50)
        {
            try
            {
                // 先获取交易对ID
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair == null)
                {
                    return ApiResponseDto<IEnumerable<MarketTradeDto>>.CreateError($"交易对 {symbol} 不存在");
                }

                // 获取更多数据以便过滤后仍有足够记录
                var trades = await _tradeRepository.GetRecentTradesByPairIdAsync(tradingPair.Id, limit * 2);
                
                // 只过滤掉双方都是系统用户的成交,保留至少一方是真实用户的成交
                var filteredTrades = trades
                    .Where(t => !(_marketMakerRegistry.IsMaker(t.BuyerId) && _marketMakerRegistry.IsMaker(t.SellerId)))
                    .Take(limit)
                    .Select(t => new MarketTradeDto
                    {
                        Id = t.Id,
                        Symbol = symbol,
                        Price = t.Price,
                        Quantity = t.Quantity,
                        ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(t.ExecutedAt).DateTime,
                        IsBuyerMaker = false // TODO: 需要根据订单类型判断,暂时设为false
                    })
                    .ToList();

                return ApiResponseDto<IEnumerable<MarketTradeDto>>.CreateSuccess(filteredTrades);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取市场最近成交失败: Symbol={Symbol}", symbol);
                return ApiResponseDto<IEnumerable<MarketTradeDto>>.CreateError("获取市场最近成交失败");
            }
        }

        private string GenerateTradeId() => $"TRD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";
        private decimal CalculateFee(decimal price, decimal quantity) => price * quantity * 0.001m; // 0.1%
    }
}