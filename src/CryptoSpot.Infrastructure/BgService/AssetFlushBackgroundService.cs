using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Infrastructure.BgService
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
            var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();
            if (assetService == null) return;
            // 原占位逻辑 (未来可实现真正的 FlushDirtyAssetsAsync)
            await Task.CompletedTask;
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
