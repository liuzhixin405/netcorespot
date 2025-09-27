using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.MarketData
{
    /// <summary>
    /// K线数据服务接口 - 使用DTO
    /// </summary>
    public interface IKLineDataServiceV2
    {
        // 基础查询
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string interval, long? startTime, long? endTime, int limit = 100);
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetHistoricalKLineDataAsync(string symbol, string interval, long startTime, long endTime);
        Task<ApiResponseDto<KLineDataDto?>> GetLatestKLineDataAsync(string symbol, string interval);

        // 批量操作
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> BatchGetKLineDataAsync(IEnumerable<string> symbols, string interval, int limit = 100);

        // 统计信息
        Task<ApiResponseDto<KLineDataStatisticsDto>> GetKLineDataStatisticsAsync(string symbol, string interval);

        // 支持的参数
        Task<ApiResponseDto<IEnumerable<string>>> GetSupportedSymbolsAsync();
        Task<ApiResponseDto<IEnumerable<string>>> GetSupportedIntervalsAsync();

        // 实时数据（如果需要）
        Task<ApiResponseDto<bool>> SubscribeKLineDataAsync(string symbol, string interval);
        Task<ApiResponseDto<bool>> UnsubscribeKLineDataAsync(string symbol, string interval);
    }
}
