using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Application.DTOs.Users; // 新增资产操作 DTO

using System.Text.Json;

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
    private readonly IMarketMakerRegistry _marketMakerRegistry;
    private readonly IDtoMappingService _mapping;

        public TradeService(
            ITradeRepository tradeRepository,
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            IAssetService assetService,
            ILogger<TradeService> logger,
            IMarketMakerRegistry marketMakerRegistry,
            IDtoMappingService mapping)
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
            // Persist to MySQL
            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var now = ServiceHelper.NowMs();
                    var dbTrade = new Trade
                    {
                        BuyOrderId = buyOrder.Id,
                        SellOrderId = sellOrder.Id,
                        BuyerId = buyOrder.UserId ?? 0,
                        SellerId = sellOrder.UserId ?? 0,
                        TradingPairId = buyOrder.TradingPairId,
                        TradeId = ServiceHelper.GenerateId("TRD"),
                        Price = price,
                        Quantity = quantity,
                        Fee = CalculateFee(price, quantity),
                        FeeAsset = "USDT",
                        ExecutedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    var createdTrade = await _tradeRepository.AddAsync(dbTrade);
                    await _unitOfWork.SaveChangesAsync();
                    _logger.LogInformation("交易执行成功 (DB): {TradeId}, 价格: {Price}, 数量: {Quantity}", dbTrade.TradeId, price, quantity);

                    return createdTrade;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行交易时出错: BuyOrder={BuyOrderId}, SellOrder={SellOrderId}", buyOrder.OrderId, sellOrder.OrderId);
                throw;
            }

        }

        private Task<Trade> ExecuteTradeInternalAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
            => ExecuteTradeRawAsync(buyOrder, sellOrder, price, quantity); // 复用原实现主体

        // ========== 接口实现: DTO 执行接口 ==========
        public Task<ApiResponseDto<TradeDto?>> ExecuteTradeAsync(ExecuteTradeRequestDto request)
        {
            return ServiceHelper.ExecuteAsync<TradeDto?>(async () =>
            {
                var buyOrder = await _orderRepository.GetByIdAsync(request.BuyOrderId) ?? throw new InvalidOperationException("买单不存在");
                var sellOrder = await _orderRepository.GetByIdAsync(request.SellOrderId) ?? throw new InvalidOperationException("卖单不存在");
                var trade = await ExecuteTradeInternalAsync(buyOrder, sellOrder, request.Price, request.Quantity);
                return _mapping.MapToDto(trade);
            }, _logger, "执行交易失败");
        }

        // ========== DTO 查询实现 ==========
        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradeHistoryAsync(long userId, string? symbol = null, int limit = 100)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var trades = await _tradeRepository.GetTradeHistoryAsync(userId, symbol, limit);
                return _mapping.MapToDto(trades).Select(dto =>
                {
                    var trade = trades.FirstOrDefault(t => t.Id == dto.Id);
                    if (trade != null)
                        dto.Side = trade.BuyerId == userId ? Domain.Entities.OrderSide.Buy : Domain.Entities.OrderSide.Sell;
                    return dto;
                });
            }, _logger, "获取交易历史失败");
        }

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(long userId, string symbol = "", int limit = 100)
            => GetTradeHistoryAsync(userId, string.IsNullOrEmpty(symbol) ? null : symbol, limit);

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetRecentTradesAsync(string symbol, int limit = 50)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var trades = await _tradeRepository.GetRecentTradesAsync(symbol, limit);
                return _mapping.MapToDto(trades);
            }, _logger, "获取最近成交失败");
        }

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradesByOrderIdAsync(long orderId)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var trades = await _tradeRepository.FindAsync(t => t.BuyOrderId == orderId || t.SellOrderId == orderId);
                return _mapping.MapToDto(trades);
            }, _logger, "获取订单成交记录失败");
        }

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetOrderTradesAsync(long orderId) => GetTradesByOrderIdAsync(orderId);

        public Task<ApiResponseDto<IEnumerable<MarketTradeDto>>> GetMarketRecentTradesAsync(string symbol, int limit = 50)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol) ?? throw new InvalidOperationException($"交易对 {symbol} 不存在");
                var trades = await _tradeRepository.GetRecentTradesByPairIdAsync((int)tradingPair.Id, limit * 2);
                
                return trades
                    .Where(t => !(_marketMakerRegistry.IsMaker(t.BuyerId) && _marketMakerRegistry.IsMaker(t.SellerId)))
                    .Take(limit)
                    .Select(t => new MarketTradeDto
                    {
                        Id = t.Id,
                        Symbol = symbol,
                        Price = t.Price,
                        Quantity = t.Quantity,
                        ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(t.ExecutedAt).DateTime,
                        IsBuyerMaker = false
                    });
            }, _logger, "获取市场最近成交失败");
        }

        private static decimal CalculateFee(decimal price, decimal quantity) => price * quantity * 0.001m; // 0.1%
    }
}