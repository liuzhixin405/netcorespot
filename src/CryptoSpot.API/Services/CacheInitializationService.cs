using CryptoSpot.Core.Interfaces.Caching;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.API.Services
{
    /// <summary>
    /// 缓存初始化后台服务
    /// </summary>
    public class CacheInitializationService : BackgroundService
    {
        private readonly ICacheService _cacheService;
        private readonly ILogger<CacheInitializationService> _logger;

        public CacheInitializationService(
            ICacheService cacheService,
            ILogger<CacheInitializationService> logger)
        {
            _cacheService = cacheService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("开始初始化缓存服务");

               

                // 初始化所有缓存
                await _cacheService.RefreshAllCacheAsync();

                _logger.LogInformation("缓存服务初始化完成");

                // 保持服务运行，定期刷新缓存
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // 每5分钟刷新一次缓存
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                        
                        if (!stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogDebug("执行定期缓存刷新");
                            await _cacheService.RefreshAllCacheAsync();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "定期缓存刷新失败");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("缓存初始化服务已取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "缓存初始化服务执行失败");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止缓存初始化服务");
            
            try
            {
                await _cacheService.ClearAllCacheAsync();
                _logger.LogInformation("缓存已清除");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除缓存时出错");
            }
            
            await base.StopAsync(cancellationToken);
        }
    }
}
