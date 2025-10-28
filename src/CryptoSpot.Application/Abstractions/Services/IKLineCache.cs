using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services
{
    public interface IKLineCache
    {
        Task UpdateKLineDataAsync(string symbol, string timeFrame, KLineData kline);
        Task MarkKLineDirtyAsync(string symbol, string timeFrame);
    }
}
