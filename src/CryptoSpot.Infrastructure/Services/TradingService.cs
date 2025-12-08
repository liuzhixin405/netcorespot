using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 交易服务实现 (简化版 - 移除Bus依赖)
    /// </summary>
    public class TradingService : ITradingService
    {
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<TradingService> _logger;
        private readonly ITradingPairService _tradingPairService;
        private readonly IOrderService _orderService;
        private readonly ITradeService _tradeService;
        private readonly IOrderMatchingEngine _matchingEngine;
        private readonly IAssetService _assetService;
        private readonly IKLineDataService _klineDataService;

        public TradingService(
            IDtoMappingService mappingService,
            ILogger<TradingService> logger,
            ITradingPairService tradingPairService,
            IOrderService orderService,
            ITradeService tradeService,
            IOrderMatchingEngine matchingEngine,
            IAssetService assetService,
            IKLineDataService klineDataService)
        {
            _mappingService = mappingService;
            _logger = logger;
            _tradingPairService = tradingPairService;
            _orderService = orderService;
            _tradeService = tradeService;
            _matchingEngine = matchingEngine;
            _assetService = assetService;
            _klineDataService = klineDataService;
        }

        #region 交易对相关

        public async Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTradingPairsAsync()
        {
            return await ServiceHelper.ExecuteWithResponseAsync(
                async () =>
                {
                    var topResp = await _tradingPairService.GetTopTradingPairsAsync(5);
                    return ServiceHelper.Forward(topResp, "获取交易对失败");
                },
                _logger, "获取交易对失败");
        }

        public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol)
        {
            return await ServiceHelper.ExecuteWithResponseAsync(
                () => _tradingPairService.GetTradingPairAsync(symbol),
                _logger, "获取交易对失败");
        }

        public async Task<ApiResponseDto<IEnumerable<TradingPairSummaryDto>>> GetTradingPairSummariesAsync()
        {
            return await ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var topResp = await _tradingPairService.GetTopTradingPairsAsync(20);
                    if (!topResp.Success || topResp.Data == null)
                        throw new InvalidOperationException(topResp.Error ?? "获取交易对摘要失败");
                    return topResp.Data.Select(tp => new TradingPairSummaryDto
                    {
                        Symbol = tp.Symbol,
                        BaseAsset = tp.BaseAsset,
                        QuoteAsset = tp.QuoteAsset,
                        Price = tp.Price,
                        Change24hPercent = tp.Price > 0 ? (tp.Change24h / tp.Price) * 100 : 0,
                        IsActive = tp.IsActive
                    });
                },
                _logger, "获取交易对摘要失败");
        }

        #endregion

        #region K线数据相关

        public Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string timeFrame, int limit = 100)
        {
            return ServiceHelper.ExecuteWithResponseAsync(
                () => _klineDataService.GetKLineDataAsync(symbol, timeFrame, limit),
                _logger, "获取K线数据失败");
        }

        public async Task<ApiResponseDto<KLineDataDto?>> GetLatestKLineDataAsync(string symbol, string timeFrame)
        {
            return await ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var dtoResponse = await _klineDataService.GetKLineDataAsync(symbol, timeFrame, 1);
                    if (!dtoResponse.Success || dtoResponse.Data == null)
                        throw new InvalidOperationException("获取最新K线数据失败");
                    return dtoResponse.Data.FirstOrDefault();
                },
                _logger, "获取最新K线数据失败");
        }

        #endregion

        #region 用户资产相关

        public Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(long userId)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var assetsResp = await _assetService.GetUserAssetsAsync(userId);
                    var assets = assetsResp.Success && assetsResp.Data != null ? assetsResp.Data : Enumerable.Empty<AssetDto>();
                    return assets;
                },
                _logger, "获取用户资产失败");
        }

        public Task<ApiResponseDto<AssetSummaryDto>> GetUserAssetSummaryAsync(long userId)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var assetsResp = await _assetService.GetUserAssetsAsync(userId);
                    var assetList = (assetsResp.Success && assetsResp.Data != null ? assetsResp.Data : Enumerable.Empty<AssetDto>()).ToList();
                    return new AssetSummaryDto
                    {
                        TotalValue = assetList.Sum(a => a.Total),
                        AvailableValue = assetList.Sum(a => a.Available),
                        FrozenValue = assetList.Sum(a => a.Frozen),
                        AssetTypes = assetList.Count(a => a.Total > 0),
                        LastUpdated = DateTime.UtcNow
                    };
                },
                _logger, "获取用户资产汇总失败");
        }

        #endregion

        #region 订单相关

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersAsync(long userId, string? symbol = null)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var resp = await _orderService.GetUserOrdersDtoAsync(userId);
                    if (!resp.Success || resp.Data == null)
                        throw new InvalidOperationException(resp.Error ?? "获取用户订单失败");
                    var orders = resp.Data;
                    if (!string.IsNullOrEmpty(symbol))
                        orders = orders.Where(o => o.Symbol == symbol);
                    return orders;
                },
                _logger, "获取用户订单失败");
        }

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetOpenOrdersAsync(long userId, string? symbol = null)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var resp = await _orderService.GetUserOrdersDtoAsync(userId, null, 500);
                    if (!resp.Success || resp.Data == null)
                        throw new InvalidOperationException(resp.Error ?? "获取开放订单失败");
                    return resp.Data
                        .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled)
                        .Where(o => string.IsNullOrEmpty(symbol) || o.Symbol == symbol);
                },
                _logger, "获取开放订单失败");
        }

        public Task<ApiResponseDto<OrderDto?>> GetOrderAsync(long userId, long orderId)
        {
            return ServiceHelper.ExecuteWithResponseAsync(
                () => _orderService.GetOrderByIdDtoAsync(orderId, userId),
                _logger, "获取订单失败");
        }

        public Task<ApiResponseDto<OrderDto?>> SubmitOrderAsync(long userId, CreateOrderRequestDto request)
        {
            return ServiceHelper.ExecuteWithResponseAsync(
                () => _orderService.CreateOrderDtoAsync(
                    userId,
                    request.Symbol,
                    (OrderSide)request.Side,
                    (OrderType)request.Type,
                    request.Quantity,
                    request.Price),
                _logger, "订单提交失败");
        }

        public Task<ApiResponseDto<bool>> CancelOrderAsync(long userId, long orderId)
        {
            return ServiceHelper.ExecuteWithResponseAsync(
                () => _orderService.CancelOrderDtoAsync(orderId, userId),
                _logger, "订单取消失败");
        }

        public Task<ApiResponseDto<BatchCancelOrdersResultDto>> CancelAllOrdersAsync(long userId, BatchCancelOrdersRequestDto? request = null)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var symbol = request?.Symbol;
                    var open = await GetOpenOrdersAsync(userId, symbol);
                    if (!open.Success || open.Data == null)
                        throw new InvalidOperationException(open.Error ?? "无法获取开放订单");

                    var resultDto = new BatchCancelOrdersResultDto
                    {
                        SuccessCount = 0,
                        FailedCount = 0,
                        Errors = new List<string>(),
                        CancelledOrderIds = new List<long>()
                    };

                    foreach (var o in open.Data)
                    {
                        var cancel = await CancelOrderAsync(userId, o.Id);
                        if (cancel.Success)
                        {
                            resultDto.SuccessCount++;
                            resultDto.CancelledOrderIds.Add(o.Id);
                        }
                        else
                        {
                            resultDto.FailedCount++;
                            resultDto.Errors.Add($"订单 {o.Id} 取消失败");
                        }
                    }
                    return resultDto;
                },
                _logger, "批量取消订单失败");
        }

        #endregion

        #region 交易记录相关

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(long userId, string? symbol = null)
        {
            return ServiceHelper.ExecuteWithResponseAsync(
                () => _tradeService.GetTradeHistoryAsync(userId, symbol),
                _logger, "获取用户交易失败");
        }

        public Task<ApiResponseDto<IEnumerable<TradeDto>>> GetOrderTradesAsync(long userId, long orderId)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var orderResp = await _orderService.GetOrderByIdDtoAsync(orderId, userId);
                    if (!orderResp.Success || orderResp.Data == null)
                        throw new InvalidOperationException(orderResp.Error ?? "订单不存在");
                    var resp = await _tradeService.GetTradesByOrderIdAsync(orderId);
                    if (!resp.Success || resp.Data == null)
                        throw new InvalidOperationException("获取订单交易失败");
                    return resp.Data;
                },
                _logger, "获取订单交易失败");
        }

        public Task<ApiResponseDto<TradeStatisticsDto>> GetUserTradeStatisticsAsync(long userId)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var resp = await _tradeService.GetTradeHistoryAsync(userId, null);
                    var trades = resp.Success && resp.Data != null ? resp.Data : Enumerable.Empty<TradeDto>();
                    var list = trades.ToList();
                    var totalTrades = list.Count;
                    var totalVolume = list.Sum(t => t.Quantity);
                    var totalValue = list.Sum(t => t.Price * t.Quantity);
                    var averageTradeSize = totalTrades > 0 ? totalValue / totalTrades : 0;
                    var totalFees = list.Sum(t => t.Fee);
                    var lastTrade = list.OrderByDescending(t => t.ExecutedDateTime).FirstOrDefault();

                    return new TradeStatisticsDto
                    {
                        TotalTrades = totalTrades,
                        TotalVolume = totalVolume,
                        TotalValue = totalValue,
                        AverageTradeSize = averageTradeSize,
                        TotalFees = totalFees,
                        LastTradeTime = lastTrade?.ExecutedDateTime,
                        ProfitLoss = 0,
                        WinRate = 0,
                        LargestWin = 0,
                        LargestLoss = 0
                    };
                },
                _logger, "获取交易统计失败");
        }

        #endregion

        #region 订单簿相关

        public Task<ApiResponseDto<OrderBookDepthDto>> GetOrderBookDepthAsync(string symbol, int depth = 20)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    var orderBook = await _matchingEngine.GetOrderBookDepthAsync(symbol, depth);
                    if (orderBook == null)
                        throw new InvalidOperationException("获取订单簿失败");
                    return orderBook;
                },
                _logger, "获取订单簿深度失败");
        }

        #endregion

        #region 市场公开数据

        public Task<ApiResponseDto<IEnumerable<MarketTradeDto>>> GetMarketRecentTradesAsync(string symbol, int limit = 50)
        {
            return ServiceHelper.ExecuteWithResponseAsync(
                () => _tradeService.GetMarketRecentTradesAsync(symbol, limit),
                _logger, "获取市场成交记录失败");
        }

        #endregion

        #region 订单测试相关

        public Task<ApiResponseDto<TestOrderResultDto>> TestOrderAsync(long userId, CreateOrderRequestDto request)
        {
            return ServiceHelper.ExecuteAsync(
                async () =>
                {
                    if (request.Quantity <= 0)
                        throw new ArgumentException("数量必须大于0");
                    if (request.Type == OrderType.Limit && (!request.Price.HasValue || request.Price <= 0))
                        throw new ArgumentException("限价单需提供有效价格");
                    
                    var pairResp = await _tradingPairService.GetTradingPairAsync(request.Symbol);
                    if (!pairResp.Success || pairResp.Data == null)
                        throw new InvalidOperationException(pairResp.Error ?? "交易对不存在");
                    
                    decimal? needQuote = null; decimal? needBase = null;
                    if (request.Side == OrderSide.Buy)
                    {
                        if (request.Type == OrderType.Limit)
                            needQuote = request.Quantity * request.Price!.Value;
                    }
                    else
                    {
                        needBase = request.Quantity;
                    }
                    
                    return new TestOrderResultDto
                    {
                        Success = true,
                        RequiredQuoteAmount = needQuote,
                        RequiredBaseAmount = needBase,
                        EstimatedTotal = needQuote ?? needBase ?? 0
                    };
                },
                _logger, "订单测试失败");
        }

        #endregion

        #region 批量操作相关
        #endregion
    }
}
