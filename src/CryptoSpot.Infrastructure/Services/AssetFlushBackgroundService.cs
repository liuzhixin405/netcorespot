using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.Users; // 添加以启用 CreateScope 扩展

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 每3分钟批量将 Redis 中标记为 dirty 的资产写回数据库；在优雅关闭时执行最后一次 flush。
    /// </summary>
    public class AssetFlushBackgroundService : BackgroundService
    {
        private readonly ILogger<AssetFlushBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(3);

        public AssetFlushBackgroundService(ILogger<AssetFlushBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Asset flush background service started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, stoppingToken);
                    await FlushAsync(stoppingToken);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Asset flush cycle failed");
                }
            }
        }

        private async Task FlushAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var assetService = scope.ServiceProvider.GetRequiredService<IAssetDomainService>() as AssetDomainService;
            if (assetService == null) return; // 只在具体实现可用时 flush
            await assetService.FlushDirtyAssetsAsync(ct);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Asset flush background service stopping - final flush");
            try
            {
                await FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Final asset flush failed");
            }
            await base.StopAsync(cancellationToken);
        }
    }
}
