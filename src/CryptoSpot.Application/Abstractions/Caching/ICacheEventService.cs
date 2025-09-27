namespace CryptoSpot.Application.Abstractions.Caching
{
    public interface ICacheEventService
    {
        Task NotifyUserChangedAsync(int userId);
        Task NotifyTradingPairChangedAsync(string symbol);
        event Func<int, Task>? UserChanged;
        event Func<string, Task>? TradingPairChanged;
    }
}
