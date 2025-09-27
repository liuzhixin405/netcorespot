using CryptoSpot.Application.Abstractions.Caching; // migrated from CryptoSpot.Core.Interfaces.Caching
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 缓存事件服务实现
    /// </summary>
    public class CacheEventService : ICacheEventService
    {
        private readonly ILogger<CacheEventService> _logger;

        public CacheEventService(ILogger<CacheEventService> logger)
        {
            _logger = logger;
        }

        public event Func<int, Task>? UserChanged;
        public event Func<string, Task>? TradingPairChanged;

        public async Task NotifyUserChangedAsync(int userId)
        {
            try
            {
                _logger.LogDebug("通知用户数据变更: UserId={UserId}", userId);
                
                if (UserChanged != null)
                {
                    await UserChanged(userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通知用户数据变更失败: UserId={UserId}", userId);
            }
        }

        public async Task NotifyTradingPairChangedAsync(string symbol)
        {
            try
            {
                _logger.LogDebug("通知交易对数据变更: Symbol={Symbol}", symbol);
                
                if (TradingPairChanged != null)
                {
                    await TradingPairChanged(symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通知交易对数据变更失败: Symbol={Symbol}", symbol);
            }
        }
    }
}
