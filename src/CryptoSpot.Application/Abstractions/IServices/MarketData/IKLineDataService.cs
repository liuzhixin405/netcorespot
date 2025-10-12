using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.MarketData
{
    /// <summary>
    /// K线数据 DTO 服务接口 (统一命名)。
    /// </summary>
    public interface IKLineDataService
    {
        // DTO 方法
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string interval, long? startTime, long? endTime, int limit = 100);
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetHistoricalKLineDataAsync(string symbol, string interval, long startTime, long endTime);
        Task<ApiResponseDto<KLineDataDto?>> GetLatestKLineDataAsync(string symbol, string interval);
        Task<ApiResponseDto<IEnumerable<KLineDataDto>>> BatchGetKLineDataAsync(IEnumerable<string> symbols, string interval, int limit = 100);
        Task<ApiResponseDto<KLineDataStatisticsDto>> GetKLineDataStatisticsAsync(string symbol, string interval);        Task<ApiResponseDto<IEnumerable<string>>> GetSupportedSymbolsAsync();
        Task<ApiResponseDto<IEnumerable<string>>> GetSupportedIntervalsAsync();
        Task<ApiResponseDto<bool>> SubscribeKLineDataAsync(string symbol, string interval);
        Task<ApiResponseDto<bool>> UnsubscribeKLineDataAsync(string symbol, string interval);
    }
}
