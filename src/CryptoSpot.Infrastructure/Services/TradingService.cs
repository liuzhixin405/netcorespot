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

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 交易服务实现
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
        private readonly IAssetService _assetService;
        private readonly IKLineDataService _klineDataService;

        public TradingService(
            IDtoMappingService mappingService,
            ILogger<TradingService> logger,
            ICommandBus commandBus,
            ITradingPairService tradingPairService,
            IOrderService orderService,
            ITradeService tradeService,
            IOrderMatchingEngine matchingEngine,
            IAssetService assetService,
            IKLineDataService klineDataService)
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
                var topResp = await _tradingPairService.GetTopTradingPairsAsync(5);
                if (!topResp.Success || topResp.Data == null)
                    return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateError(topResp.Error ?? "获取交易对失败", topResp.ErrorCode);
                return ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(topResp.Data);
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
                var resp = await _tradingPairService.GetTradingPairAsync(symbol);
                return resp;
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
                var topResp = await _tradingPairService.GetTopTradingPairsAsync(20);
                if (!topResp.Success || topResp.Data == null)
                    return ApiResponseDto<IEnumerable<TradingPairSummaryDto>>.CreateError(topResp.Error ?? "获取交易对摘要失败", topResp.ErrorCode);
                var summaries = topResp.Data.Select(tp => new TradingPairSummaryDto
                {
                    Symbol = tp.Symbol,
                    BaseAsset = tp.BaseAsset,
                    QuoteAsset = tp.QuoteAsset,
                    Price = tp.Price,
                    Change24hPercent = tp.Price > 0 ? (tp.Change24h / tp.Price) * 100 : 0,
                    IsActive = tp.IsActive
                }).ToList();
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
                var dtoResponse = await _klineDataService.GetKLineDataAsync(symbol, timeFrame, limit);
                return dtoResponse;
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
                var dtoResponse = await _klineDataService.GetKLineDataAsync(symbol, timeFrame, 1);
                if (!dtoResponse.Success || dtoResponse.Data == null)
                    return ApiResponseDto<KLineDataDto?>.CreateError("获取最新K线数据失败");
                var latest = dtoResponse.Data.FirstOrDefault();
                return ApiResponseDto<KLineDataDto?>.CreateSuccess(latest);
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
                var assetsResp = await _assetService.GetUserAssetsAsync(userId);
                var assets = assetsResp.Success && assetsResp.Data != null ? assetsResp.Data : Enumerable.Empty<AssetDto>();
                var dtoList = assets.ToList();
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
                var assetsResp = await _assetService.GetUserAssetsAsync(userId);
                var assetList = (assetsResp.Success && assetsResp.Data != null ? assetsResp.Data : Enumerable.Empty<AssetDto>()).ToList();

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
                var resp = await _orderService.GetUserOrdersDtoAsync(userId);
                if (!resp.Success || resp.Data == null)
                    return ApiResponseDto<IEnumerable<OrderDto>>.CreateError(resp.Error ?? "获取用户订单失败", resp.ErrorCode);
                var orders = resp.Data;
                if (!string.IsNullOrEmpty(symbol))
                    orders = orders.Where(o => o.Symbol == symbol);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(orders.ToList());
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
                var resp = await _orderService.GetUserOrdersDtoAsync(userId, null, 500);
                if (!resp.Success || resp.Data == null)
                    return ApiResponseDto<IEnumerable<OrderDto>>.CreateError(resp.Error ?? "获取开放订单失败", resp.ErrorCode);
                var filtered = resp.Data.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled)
                                         .Where(o => string.IsNullOrEmpty(symbol) || o.Symbol == symbol);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(filtered.ToList());
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
                var resp = await _orderService.GetOrderByIdDtoAsync(orderId, userId);
                if (!resp.Success) return ApiResponseDto<OrderDto?>.CreateError(resp.Error ?? "获取订单失败", resp.ErrorCode);
                return ApiResponseDto<OrderDto?>.CreateSuccess(resp.Data);
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

                var orderResp = await _orderService.GetOrderByIdDtoAsync(result.OrderId.Value, userId);
                if (!orderResp.Success)
                    return ApiResponseDto<OrderDto?>.CreateError(orderResp.Error ?? "订单不存在", orderResp.ErrorCode);

                return ApiResponseDto<OrderDto?>.CreateSuccess(orderResp.Data, "订单提交成功");
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
                    return ApiResponseDto<BatchCancelOrdersResultDto>.CreateError(open.Error ?? "无法获取开放订单");
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

        // Removed unused GetOrderHistoryAsync

        #endregion

        #region 交易记录相关

        public async Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(int userId, string? symbol = null)
        {
            try
            {
                var resp = await _tradeService.GetTradeHistoryAsync(userId, symbol);
                if (!resp.Success || resp.Data == null)
                {
                    return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取用户交易失败");
                }
                return resp;
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
                var orderResp = await _orderService.GetOrderByIdDtoAsync(orderId, userId);
                if (!orderResp.Success || orderResp.Data == null)
                {
                    return ApiResponseDto<IEnumerable<TradeDto>>.CreateError(orderResp.Error ?? "订单不存在", orderResp.ErrorCode);
                }
                // 接口方法已精简，改用 GetTradesByOrderIdAsync (仍保留交易记录查询能力)
                var resp = await _tradeService.GetTradesByOrderIdAsync(orderId);
                if (!resp.Success || resp.Data == null)
                {
                    return ApiResponseDto<IEnumerable<TradeDto>>.CreateError("获取订单交易失败");
                }
                return resp;
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
                var resp = await _tradeService.GetTradeHistoryAsync(userId, null);
                var trades = resp.Success && resp.Data != null ? resp.Data : Enumerable.Empty<TradeDto>();
                var list = trades.ToList();
                var totalTrades = list.Count;
                var totalVolume = list.Sum(t => t.Quantity);
                var totalValue = list.Sum(t => t.Price * t.Quantity);
                var averageTradeSize = totalTrades > 0 ? totalValue / totalTrades : 0;
                var totalFees = list.Sum(t => t.Fee);
                var lastTrade = list.OrderByDescending(t => t.ExecutedDateTime).FirstOrDefault();

                var dto = new TradeStatisticsDto
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
                return ApiResponseDto<OrderBookDepthDto>.CreateSuccess(orderBook);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order book depth for {Symbol}", symbol);
                return ApiResponseDto<OrderBookDepthDto>.CreateError("获取订单簿深度失败");
            }
        }

        #endregion

        #region 市场公开数据

        public async Task<ApiResponseDto<IEnumerable<MarketTradeDto>>> GetMarketRecentTradesAsync(string symbol, int limit = 50)
        {
            try
            {
                var result = await _tradeService.GetMarketRecentTradesAsync(symbol, limit);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting market recent trades for {Symbol}", symbol);
                return ApiResponseDto<IEnumerable<MarketTradeDto>>.CreateError("获取市场成交记录失败");
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
                if (request.Type == OrderType.Limit && (!request.Price.HasValue || request.Price <= 0))
                {
                    return ApiResponseDto<TestOrderResultDto>.CreateError("限价单需提供有效价格");
                }
                var pairResp = await _tradingPairService.GetTradingPairAsync(request.Symbol);
                if (!pairResp.Success || pairResp.Data == null)
                {
                    return ApiResponseDto<TestOrderResultDto>.CreateError(pairResp.Error ?? "交易对不存在", pairResp.ErrorCode);
                }
                var tradingPair = pairResp.Data;
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

        // Removed unused paged trade history
        #endregion
    }
}
