using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Domain.Entities; // 保留仅供 Raw 方法使用

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface ITradeService
    {
        // ========== Raw 执行 (撮合核心内部使用) ==========
        Task<Trade> ExecuteTradeRawAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity);

        // ========== DTO 查询接口 ==========
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100);
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(int userId, string symbol = "", int limit = 100);
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetRecentTradesAsync(string symbol, int limit = 50);
        Task<ApiResponseDto<TradeDto?>> GetTradeByIdAsync(long tradeId);
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradesByOrderIdAsync(int orderId);
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetOrderTradesAsync(int orderId) => GetTradesByOrderIdAsync(orderId);
        Task<ApiResponseDto<decimal>> GetTradingVolumeAsync(string symbol, TimeSpan timeRange);
        Task<ApiResponseDto<(decimal high, decimal low)>> GetPriceRangeAsync(string symbol, TimeSpan timeRange);
    }
}
