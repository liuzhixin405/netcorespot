using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DomainCommands.Trading;
using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 交易服务实现 (原 TradingServiceV2)
    /// </summary>
    public class TradingService : ITradingService
    {
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<TradingService> _logger;
        private readonly ICommandBus _commandBus;
        private readonly ITradingPairService _tradingPairService;
        private readonly IOrderService _orderService;
        private readonly ITradeService _tradeService;
        private readonly IOrderMatchingEngine _matchingEngine;
        private readonly IAssetDomainService _assetService;
        private readonly IKLineDataDomainService _klineDataService; // 更新

        public TradingService(
            IDtoMappingService mappingService,
            ILogger<TradingService> logger,
            ICommandBus commandBus,
            ITradingPairService tradingPairService,
            IOrderService orderService,
            ITradeService tradeService,
            IOrderMatchingEngine matchingEngine,
            IAssetDomainService assetService,
            IKLineDataDomainService klineDataService)
        {
            _mappingService = mappingService;
            _logger = logger;
            _commandBus = commandBus;
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
            try
            {
                var tradingPairs = await _tradingPairService.GetTopTradingPairsAsync(5);
                var dtoList = _mappingService.MapToDto(tradingPairs);
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading pairs");
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateError("获取交易对失败");
            }
        }

        public async Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol)
        {
            try
            {
                var tradingPair = await _tradingPairService.GetTradingPairAsync(symbol);
                var dto = tradingPair != null ? _mappingService.MapToDto(tradingPair) : null;
                return ApiResponseDto<TradingPairDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading pair {Symbol}", symbol);
                return ApiResponseDto<TradingPairDto?>.CreateError("获取交易对失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradingPairSummaryDto>>> GetTradingPairSummariesAsync()
        {
            try
            {
                var tradingPairs = await _tradingPairService.GetTopTradingPairsAsync(20);
                var summaries = _mappingService.MapToSummaryDto(tradingPairs);
                return ApiResponseDto<IEnumerable<TradingPairSummaryDto>>.CreateSuccess(summaries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading pair summaries");
                return ApiResponseDto<IEnumerable<TradingPairSummaryDto>>.CreateError("获取交易对摘要失败");
            }
        }

        #endregion

        #region K线数据相关

        public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string timeFrame, int limit = 100)
        {
            try
            {
                var klineData = await _klineDataService.GetKLineDataAsync(symbol, timeFrame, limit);
                var dtoList = _mappingService.MapToDto(klineData, symbol);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data for {Symbol}", symbol);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("获取K线数据失败");
            }
        }

        public async Task<ApiResponseDto<KLineDataDto?>> GetLatestKLineDataAsync(string symbol, string timeFrame)
        {
            try
            {
                var klineData = await _klineDataService.GetKLineDataAsync(symbol, timeFrame, 1);
                var latest = klineData.FirstOrDefault();
                var dto = latest != null ? _mappingService.MapToDto(latest, symbol) : null;
                return ApiResponseDto<KLineDataDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest K-line data for {Symbol}", symbol);
                return ApiResponseDto<KLineDataDto?>.CreateError("获取最新K线数据失败");
            }
        }

        #endregion

        #region 用户资产相关

        public async Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(int userId)
        {
            try
            {
                var assets = await _assetService.GetUserAssetsAsync(userId);
                var dtoList = _mappingService.MapToDto(assets);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user assets for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateError("获取用户资产失败");
            }
        }

        public async Task<ApiResponseDto<AssetSummaryDto>> GetUserAssetSummaryAsync(int userId)
        {
            try
            {
                // 领域服务直接返回资产实体集合，不再是 ApiResponseDto
                var assets = await _assetService.GetUserAssetsAsync(userId);
                var assetList = assets.ToList();

                var summary = new AssetSummaryDto
                {
                    TotalValue = assetList.Sum(a => a.Total),
                    AvailableValue = assetList.Sum(a => a.Available),
                    FrozenValue = assetList.Sum(a => a.Frozen),
                    AssetTypes = assetList.Count(a => a.Total > 0),
                    LastUpdated = DateTime.UtcNow
                };

                return ApiResponseDto<AssetSummaryDto>.CreateSuccess(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user asset summary for user {UserId}", userId);
                return ApiResponseDto<AssetSummaryDto>.CreateError("获取用户资产汇总失败");
            }
        }

        #endregion

        #region 订单相关

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersAsync(int userId, string? symbol = null)
        {
            try
            {
                var orders = await _orderService.GetUserOrdersAsync(userId);
                if (!string.IsNullOrEmpty(symbol))
                    orders = orders.Where(o => o.TradingPair != null && o.TradingPair.Symbol == symbol);
                var dtoList = _mappingService.MapToDto(orders);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user orders for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取用户订单失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetOpenOrdersAsync(int userId, string? symbol = null)
        {
            try
            {
                var orders = await _orderService.GetUserOrdersAsync(userId, null, 500);
                var filtered = orders.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled)
                                     .Where(o => string.IsNullOrEmpty(symbol) || (o.TradingPair != null && o.TradingPair.Symbol == symbol));
                var dtoList = _mappingService.MapToDto(filtered);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting open orders for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取开放订单失败");
            }
        }

        public async Task<ApiResponseDto<OrderDto?>> GetOrderAsync(int userId, int orderId)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(orderId, userId);
                var dto = order != null ? _mappingService.MapToDto(order) : null;
                return ApiResponseDto<OrderDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order {OrderId}", orderId);
                return ApiResponseDto<OrderDto?>.CreateError("获取订单失败");
            }
        }

        public async Task<ApiResponseDto<OrderDto?>> SubmitOrderAsync(int userId, CreateOrderRequestDto request)
        {
            try
            {
                var command = new SubmitOrderCommand
                {
                    UserId = userId,
                    Symbol = request.Symbol,
                    Side = (OrderSide)request.Side,
                    Type = (OrderType)request.Type,
                    Quantity = request.Quantity,
                    Price = request.Price,
                    ClientOrderId = request.ClientOrderId
                };

                var result = await _commandBus.SendAsync<SubmitOrderCommand, SubmitOrderResult>(command);
                if (!result.Success || !result.OrderId.HasValue)
                {
                    return ApiResponseDto<OrderDto?>.CreateError(result.ErrorMessage ?? "订单提交失败");
                }

                var order = await _orderService.GetOrderByIdAsync(result.OrderId.Value, userId);
                if (order == null)
                {
                    return ApiResponseDto<OrderDto?>.CreateError("订单不存在");
                }

                var dto = _mappingService.MapToDto(order);
                return ApiResponseDto<OrderDto?>.CreateSuccess(dto, "订单提交成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting order for user {UserId}", userId);
                return ApiResponseDto<OrderDto?>.CreateError("订单提交失败");
            }
        }

        public async Task<ApiResponseDto<bool>> CancelOrderAsync(int userId, int orderId)
        {
            try
            {
                var command = new CancelOrderCommand { UserId = userId, OrderId = orderId };
                var result = await _commandBus.SendAsync<CancelOrderCommand, CancelOrderResult>(command);
                return result.Success ?
                    ApiResponseDto<bool>.CreateSuccess(true, "订单取消成功") :
                    ApiResponseDto<bool>.CreateError(result.ErrorMessage ?? "订单取消失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
                return ApiResponseDto<bool>.CreateError("订单取消失败");
            }
        }

        public async Task<ApiResponseDto<BatchCancelOrdersResultDto>> CancelAllOrdersAsync(int userId, BatchCancelOrdersRequestDto? request = null)
        {
            try
            {
                var symbol = request?.Symbol;
                var open = await GetOpenOrdersAsync(userId, symbol);
                if (!open.Success || open.Data == null)
                {
                    return ApiResponseDto<BatchCancelOrdersResultDto>.CreateError("无法获取开放订单");
                }

                var resultDto = new BatchCancelOrdersResultDto
                {
                    SuccessCount = 0,
                    FailedCount = 0,
                    Errors = new List<string>(),
                    CancelledOrderIds = new List<int>()
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

                return ApiResponseDto<BatchCancelOrdersResultDto>.CreateSuccess(resultDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling all orders for user {UserId}", userId);
                return ApiResponseDto<BatchCancelOrdersResultDto>.CreateError("批量取消订单失败");
            }
        }

        #endregion

        #region 交易记录相关

        public async Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(int userId, string? symbol = null)
        {
            try
            {
                var trades = await _tradeService.GetTradeHistoryAsync(userId, symbol);
                var dtoList = _mappingService.MapToDto(trades);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user trades for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取用户交易失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradeDto>>> GetOrderTradesAsync(int userId, int orderId)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(orderId, userId);
                if (order == null)
                {
                    return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("订单不存在");
                }
                var trades = await _tradeService.GetOrderTradesAsync(orderId);
                var dtoList = _mappingService.MapToDto(trades);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order trades for order {OrderId}", orderId);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取订单交易失败");
            }
        }

        public async Task<ApiResponseDto<TradeStatisticsDto>> GetUserTradeStatisticsAsync(int userId)
        {
            try
            {
                var trades = await _tradeService.GetTradeHistoryAsync(userId, null);
                var totalTrades = trades.Count();
                var totalVolume = trades.Sum(t => t.Quantity);
                var totalValue = trades.Sum(t => t.Price * t.Quantity);
                var averageTradeSize = totalTrades > 0 ? totalValue / totalTrades : 0;
                var totalFees = trades.Sum(t => t.Fee);
                var lastTrade = trades.OrderByDescending(t => t.ExecutedAt).FirstOrDefault();

                var dto = new TradeStatisticsDto
                {
                    TotalTrades = totalTrades,
                    TotalVolume = totalVolume,
                    TotalValue = totalValue,
                    AverageTradeSize = averageTradeSize,
                    TotalFees = totalFees,
                    LastTradeTime = lastTrade != null ? DateTimeExtensions.FromUnixTimeMilliseconds(lastTrade.ExecutedAt) : (DateTime?)null,
                    ProfitLoss = 0,
                    WinRate = 0,
                    LargestWin = 0,
                    LargestLoss = 0
                };

                return ApiResponseDto<TradeStatisticsDto>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trade statistics for user {UserId}", userId);
                return ApiResponseDto<TradeStatisticsDto>.CreateError("获取交易统计失败");
            }
        }

        #endregion

        #region 订单簿相关

        public async Task<ApiResponseDto<OrderBookDepthDto>> GetOrderBookDepthAsync(string symbol, int depth = 20)
        {
            try
            {
                var orderBook = await _matchingEngine.GetOrderBookDepthAsync(symbol, depth);
                if (orderBook == null)
                {
                    return ApiResponseDto<OrderBookDepthDto>.CreateError("获取订单簿失败");
                }
                var dto = _mappingService.MapToDto(orderBook);
                return ApiResponseDto<OrderBookDepthDto>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order book depth for {Symbol}", symbol);
                return ApiResponseDto<OrderBookDepthDto>.CreateError("获取订单簿深度失败");
            }
        }

        #endregion

        #region 订单测试相关

        public async Task<ApiResponseDto<TestOrderResultDto>> TestOrderAsync(int userId, CreateOrderRequestDto request)
        {
            try
            {
                var result = new TestOrderResultDto();

                if (request.Quantity <= 0)
                {
                    return ApiResponseDto<TestOrderResultDto>.CreateError("数量必须大于0");
                }
                if (request.Type == OrderTypeDto.Limit && (!request.Price.HasValue || request.Price <= 0))
                {
                    return ApiResponseDto<TestOrderResultDto>.CreateError("限价单需提供有效价格");
                }
                var pair = await _tradingPairService.GetTradingPairAsync(request.Symbol);
                if (pair == null)
                {
                    return ApiResponseDto<TestOrderResultDto>.CreateError("交易对不存在");
                }
                decimal? needQuote = null; decimal? needBase = null;
                if (request.Side == OrderSideDto.Buy)
                {
                    if (request.Type == OrderTypeDto.Limit)
                        needQuote = request.Quantity * request.Price!.Value;
                }
                else
                {
                    needBase = request.Quantity;
                }
                result.Success = true;
                result.RequiredQuoteAmount = needQuote;
                result.RequiredBaseAmount = needBase;
                result.EstimatedTotal = needQuote ?? needBase ?? 0;
                return ApiResponseDto<TestOrderResultDto>.CreateSuccess(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing order for user {UserId}", userId);
                return ApiResponseDto<TestOrderResultDto>.CreateError("订单测试失败");
            }
        }

        #endregion

        #region 批量操作相关

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetOrderHistoryAsync(int userId, PagedRequestDto request)
        {
            try
            {
                var orders = await _orderService.GetUserOrdersAsync(userId);
                var paged = orders.Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize);
                var dtoList = _mappingService.MapToDto(paged);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order history for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取订单历史失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradeHistoryAsync(int userId, PagedRequestDto request)
        {
            try
            {
                var trades = await _tradeService.GetTradeHistoryAsync(userId, null);
                var paged = trades.Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize);
                var dtoList = _mappingService.MapToDto(paged);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trade history for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取交易历史失败");
            }
        }
        #endregion
    }
}
