using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Domain.Entities; // 保留仅供 Raw 方法使用

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface ITradeService
    {
        // ========== DTO 执行与查询接口 ==========
        Task<ApiResponseDto<TradeDto?>> ExecuteTradeAsync(ExecuteTradeRequestDto request); // 新增: 替代 Raw 执行

        // ========== DTO 查询接口 ==========
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100);
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetUserTradesAsync(int userId, string symbol = "", int limit = 100);
        Task<ApiResponseDto<IEnumerable<TradeDto>>> GetRecentTradesAsync(string symbol, int limit = 50);
    // 保留订单成交查询 (被 TradingService 使用)
    Task<ApiResponseDto<IEnumerable<TradeDto>>> GetTradesByOrderIdAsync(int orderId);
    Task<ApiResponseDto<IEnumerable<TradeDto>>> GetOrderTradesAsync(int orderId) => GetTradesByOrderIdAsync(orderId);
        
        // ========== 市场公开数据接口 ==========
        /// <summary>
        /// 获取市场最近成交记录 (公开数据,不包含用户信息)
        /// </summary>
        Task<ApiResponseDto<IEnumerable<MarketTradeDto>>> GetMarketRecentTradesAsync(string symbol, int limit = 50);
    }
}
