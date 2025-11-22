using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    /// <summary>
    /// 交易服务接口
    /// </summary>
    public interface ITradingService
    {
        // 交易对相关
        Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTradingPairsAsync();
        Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol);
        Task<ApiResponseDto<IEnumerable<TradingPairSummaryDto>>> GetTradingPairSummariesAsync();

        // K线数据相关
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string timeFrame, int limit = 100);
        Task<ApiResponseDto<KLineDataDto?>> GetLatestKLineDataAsync(string symbol, string timeFrame);

        // 用户资产相关
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(long userId);
        Task<ApiResponseDto<AssetSummaryDto>> GetUserAssetSummaryAsync(long userId);

        // 订单相关
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersAsync(long userId, string? symbol = null);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetOpenOrdersAsync(long userId, string? symbol = null);
        Task<ApiResponseDto<OrderDto?>> GetOrderAsync(long userId, long orderId);
        Task<ApiResponseDto<OrderDto?>> SubmitOrderAsync(long userId, CreateOrderRequestDto request);
        Task<ApiResponseDto<bool>> CancelOrderAsync(long userId, long orderId);
        Task<ApiResponseDto<BatchCancelOrdersResultDto>> CancelAllOrdersAsync(long userId, BatchCancelOrdersRequestDto? request = null);

        // 交易记录相关
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(long userId, string? symbol = null);
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetOrderTradesAsync(long userId, long orderId);
        Task<ApiResponseDto<TradeStatisticsDto>> GetUserTradeStatisticsAsync(long userId);

        // 订单簿相关
        Task<ApiResponseDto<OrderBookDepthDto>> GetOrderBookDepthAsync(string symbol, int depth = 20);

        // 市场公开数据
        Task<ApiResponseDto<IEnumerable<MarketTradeDto>>> GetMarketRecentTradesAsync(string symbol, int limit = 50);

        Task<ApiResponseDto<TestOrderResultDto>> TestOrderAsync(long userId, CreateOrderRequestDto request);
    }
}
